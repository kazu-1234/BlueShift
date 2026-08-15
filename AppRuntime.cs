using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinRT.Interop;
using WinUiShared;

namespace App1
{
    /// <summary>
    /// プロセス寿命: トレイ・ガンマ・二重起動イベント。MainWindow は都度生成（ADM 同等）。
    /// </summary>
    public sealed class AppRuntime : IDisposable
    {
        private readonly Application _app;
        private readonly DispatcherQueue _uiDispatcher;
        private readonly Settings _settings;
        private readonly ObservableCollection<Pattern> _patterns;
        private readonly AppState _appState;
        private readonly GammaTransitionService _gammaTransition = new();
        private readonly DispatcherTimer _gammaScheduleTimer = new();

        private MainWindow? _mainWindow;
        private TrayMessageWindow? _trayMessageWindow;
        private SystemEventWindow? _systemEventWindow;
        private ResumeReapplyCoordinator? _resumeCoordinator;
        private CancellationTokenSource? _listenerCts;
        private bool _gammaInitialized;
        private bool _systemEventInitialized;
        private bool _gammaPreviewActive;
        private bool _trayInitialized;
        private bool _isExitingProcess;
        private bool _startupUpdateCheckScheduled;
        private bool _startupReadyNotified;

        public AppRuntime(Application app)
        {
            _app = app;
            // 二重起動リスナーは BG スレッドから来るため、UI Dispatcher を起動時に保持する
            _uiDispatcher = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("AppRuntime must be created on the UI thread.");
            _settings = Settings.Load();
            _patterns = new ObservableCollection<Pattern>(_settings.Patterns.OrderBy(p => p.Time));
            _appState = new AppState(_settings, _patterns);
            WireAppStateGammaHooks();
        }

        public AppState AppState => _appState;
        public Settings Settings => _settings;
        public bool IsExitingProcess => _isExitingProcess;

        public void Start(bool launchInBackground, bool requestInteractiveShow)
        {
            ThemeService.Initialize(_settings.ThemePreference);
            RegisterGammaResetOnExit();
            StartListeners();

            // コア（ガンマ／復帰監視）をトレイより先に初期化（SPM 同型・ログオン直後の未適用を防ぐ）
            EnsureGamma();

            if (!ShouldUseTray())
            {
                if (requestInteractiveShow || !launchInBackground)
                    ShowOrCreateMainWindow();
                ScheduleStartupReadyNotification();
                return;
            }

            EnsureTray();
            if (!_trayInitialized)
                ScheduleTrayRetries();

            if (requestInteractiveShow || !launchInBackground)
                ShowOrCreateMainWindow();

            ScheduleStartupReadyNotification();
        }

        public void ShowOrCreateMainWindow(string? pageTag = null)
        {
            if (_isExitingProcess)
                return;

            GetDispatcherQueue()?.TryEnqueue(() => ShowOrCreateMainWindowCore(pageTag));
        }

        private void ShowOrCreateMainWindowCore(string? pageTag = null)
        {
            if (_isExitingProcess)
                return;

            if (_mainWindow != null)
            {
                BringWindowToForeground(_mainWindow);
                if (pageTag != null)
                    _mainWindow.NavigateToPageTag(pageTag);
                return;
            }

            _mainWindow = new MainWindow(this);
            _mainWindow.Closed += MainWindow_Closed;
            _mainWindow.PrepareAndActivate(pageTag);
            ScheduleStartupUpdateCheckIfNeeded();
        }

        private void ScheduleStartupUpdateCheckIfNeeded()
        {
            if (_startupUpdateCheckScheduled || _mainWindow == null)
                return;

            _startupUpdateCheckScheduled = true;
            _ = UpdateFlowService.TryStartupCheckAsync(_mainWindow, _settings);
        }

        public void OnMainWindowClosing(MainWindow window)
        {
            if (_isExitingProcess || window != _mainWindow)
                return;

            window.SaveWindowBoundsFromRuntime();
        }

        public void ExitApplication(string reason = "unknown")
        {
            if (_isExitingProcess)
                return;

            _isExitingProcess = true;
            AppendLifetimeLog(reason);
            _listenerCts?.Cancel();
            _listenerCts?.Dispose();
            _listenerCts = null;
            _resumeCoordinator?.Dispose();
            _resumeCoordinator = null;

            _gammaScheduleTimer.Stop();
            _gammaTransition.Stop();
            _systemEventWindow?.Dispose();
            _systemEventWindow = null;
            _systemEventInitialized = false;
            _trayMessageWindow?.Dispose();
            _trayMessageWindow = null;

            if (!_settings.AutoStart)
                StartupManager.SyncAutostartWithSettings(false);

            GammaController.ResetGamma();
            SingleInstanceManager.Release();

            if (_mainWindow != null)
            {
                try { _mainWindow.Close(); } catch { }
                _mainWindow = null;
            }

            _app.Exit();
        }

        public void ApplyTrayIconVisibility()
        {
            if (_trayMessageWindow == null)
                return;

            if (_settings.HideTrayIcon)
                _trayMessageWindow.TrayIcon.Hide();
            else
                _trayMessageWindow.TrayIcon.Show();
        }

        public void Dispose()
        {
            ExitApplication("dispose");
        }

        private void MainWindow_Closed(object sender, WindowEventArgs e)
        {
            if (ReferenceEquals(_mainWindow, sender))
                _mainWindow = null;
        }

        private void WireAppStateGammaHooks()
        {
            _appState.ApplyTrayIconVisibility = ApplyTrayIconVisibility;
            _appState.SavePatterns = () => { };
            _appState.PreviewGamma = settings =>
            {
                _gammaPreviewActive = true;
                _gammaTransition.ApplyImmediate(settings);
            };
            _appState.RefreshGamma = () =>
            {
                _gammaPreviewActive = false;
                ApplyCurrentGamma();
            };
            _appState.RescheduleTimer = ScheduleNextGammaCheck;
        }

        private static bool ShouldUseTray()
        {
#if DEBUG
            if (Debugger.IsAttached)
                return false;
#endif
            return true;
        }

        /// <summary>コア初期化後にトレイバルーンで常駐準備完了を知らせる（VS 外での動作確認用）。</summary>
        private void ScheduleStartupReadyNotification()
        {
            if (_startupReadyNotified)
                return;
            _startupReadyNotified = true;

            _ = Task.Run(async () =>
            {
                int[] delaysMs = { 1500, 2500, 5000 };
                var start = DateTime.UtcNow;
                foreach (int delayMs in delaysMs)
                {
                    try
                    {
                        var wait = start.AddMilliseconds(delayMs) - DateTime.UtcNow;
                        if (wait > TimeSpan.Zero)
                            await Task.Delay(wait).ConfigureAwait(false);
                    }
                    catch
                    {
                        return;
                    }

                    if (_isExitingProcess)
                        return;

                    var shown = new TaskCompletionSource<bool>();
                    if (!(GetDispatcherQueue()?.TryEnqueue(() =>
                        {
                            try
                            {
                                if (_isExitingProcess || _settings.HideTrayIcon
                                    || !_trayInitialized || _trayMessageWindow == null)
                                {
                                    shown.TrySetResult(false);
                                    return;
                                }

                                _trayMessageWindow.TrayIcon.ShowBalloon(
                                    Strings.Get("Tray_StartupReadyTitle"),
                                    Strings.Get("Tray_StartupReadyBody"));
                                shown.TrySetResult(true);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Startup ready balloon failed: {ex.Message}");
                                shown.TrySetResult(false);
                            }
                        }) ?? false))
                    {
                        shown.TrySetResult(false);
                    }

                    if (await shown.Task.ConfigureAwait(false))
                        return;
                }
            });
        }

        private void EnsureTray()
        {
            if (_trayInitialized)
                return;

            try
            {
                _trayMessageWindow = new TrayMessageWindow();
                var tray = _trayMessageWindow.TrayIcon;
                tray.OpenMainWindowRequested += () => ShowOrCreateMainWindow();
                tray.OpenSettingsRequested += () => ShowOrCreateMainWindow("Settings");
                tray.ExitRequested += () => GetDispatcherQueue()?.TryEnqueue(() => ExitApplication("tray-menu"));
                ApplyTrayIconVisibility();
                _trayInitialized = true;
            }
            catch
            {
                _trayMessageWindow?.Dispose();
                _trayMessageWindow = null;
                _trayInitialized = false;
            }
        }

        /// <summary>ログオン直後などシェル未準備でトレイ初期化が失敗した場合の遅延再試行。</summary>
        private void ScheduleTrayRetries()
        {
            int[] delaysMs = { 2000, 5000, 15000 };
            foreach (int delayMs in delaysMs)
            {
                int captured = delayMs;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(captured).ConfigureAwait(false);
                    }
                    catch
                    {
                        return;
                    }

                    if (_isExitingProcess || _trayInitialized)
                        return;

                    GetDispatcherQueue()?.TryEnqueue(() =>
                    {
                        if (_isExitingProcess || _trayInitialized)
                            return;
                        EnsureTray();
                    });
                });
            }
        }

        private void EnsureGamma()
        {
            if (_gammaInitialized)
                return;

            _gammaInitialized = true;
            EnsureResumeCoordinator();
            EnsureSystemEventMonitor();

            _gammaScheduleTimer.Tick += (_, _) =>
            {
                if (_gammaPreviewActive)
                    return;
                ApplyCurrentGamma();
                ScheduleNextGammaCheck();
            };

            _gammaTransition.ResetToOff(applyToDisplay: true);
            _resumeCoordinator?.BeginStartupPeriod();
            ScheduleNextGammaCheck();
        }

        private void EnsureResumeCoordinator()
        {
            if (_resumeCoordinator != null)
                return;

            _resumeCoordinator = new ResumeReapplyCoordinator(
                _uiDispatcher,
                () => _isExitingProcess,
                OnResumeApply,
                OnResumeApplyDirect,
                OnResumeWatchdog,
                NeedsGammaForce);
            _resumeCoordinator.Start();
        }

        /// <summary>復帰監視窓。HWND が死んでいたら作り直す。</summary>
        private void EnsureSystemEventMonitor()
        {
            if (_systemEventInitialized)
            {
                if (_systemEventWindow is { IsAlive: true })
                    return;

                _systemEventWindow?.Dispose();
                _systemEventWindow = null;
                _systemEventInitialized = false;
            }

            try
            {
                EnsureResumeCoordinator();
                _systemEventWindow = new SystemEventWindow("BlueShiftSystemEventWindow_v2");
                _systemEventWindow.SystemDisplayStateChanged += () => _resumeCoordinator?.NotifyResume();
                _systemEventWindow.SystemSuspending += () => _resumeCoordinator?.NotifySuspend();
                _systemEventInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"System event monitor init failed: {ex.Message}");
                _systemEventWindow?.Dispose();
                _systemEventWindow = null;
                _systemEventInitialized = false;
            }
        }

        /// <summary>トレイ HWND が死んでいたら作り直し、生きていればアイコンを付け直す。</summary>
        private void EnsureTrayAlive(bool reAddIcon = false)
        {
            if (_isExitingProcess || !ShouldUseTray())
                return;

            if (_trayInitialized && _trayMessageWindow is { IsAlive: true })
            {
                if (reAddIcon && !_settings.HideTrayIcon)
                {
                    try { _trayMessageWindow.TrayIcon.ReAdd(); }
                    catch (Exception ex) { Debug.WriteLine($"Tray ReAdd failed: {ex.Message}"); }
                }
                return;
            }

            _trayMessageWindow?.Dispose();
            _trayMessageWindow = null;
            _trayInitialized = false;
            EnsureTray();
        }

        private void EnsureResidentLifetime()
        {
            EnsureSystemEventMonitor();
            EnsureTrayAlive(reAddIcon: true);
        }

        private void StartListeners()
        {
            var showEvent = SingleInstanceManager.InteractiveShowEvent;
            var exitEvent = SingleInstanceManager.ExitEvent;
            if (showEvent == null && exitEvent == null)
                return;

            _listenerCts = new CancellationTokenSource();
            var token = _listenerCts.Token;

            if (showEvent != null)
            {
                Task.Run(() => ListenShowLoop(showEvent, token, () => ShowOrCreateMainWindow()), token);
            }

            if (exitEvent != null)
            {
                Task.Run(() => ListenLoop(exitEvent, token, () => GetDispatcherQueue()?.TryEnqueue(() => ExitApplication("exit-signal"))), token);
            }
        }

        private static void ListenShowLoop(EventWaitHandle handle, CancellationToken token, Action action)
        {
            while (!token.IsCancellationRequested)
            {
                bool signaled = false;
                try
                {
                    signaled = handle.WaitOne(500);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (!signaled)
                    signaled = SingleInstanceManager.TryConsumeShowSignal();

                if (token.IsCancellationRequested)
                    break;

                if (signaled)
                    action();
            }
        }

        private static void ListenLoop(EventWaitHandle handle, CancellationToken token, Action action)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    handle.WaitOne(500);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (token.IsCancellationRequested)
                    break;

                if (SingleInstanceManager.TryConsumeExitSignal())
                    action();
            }
        }

        private DispatcherQueue GetDispatcherQueue() => _uiDispatcher;

        private static void BringWindowToForeground(Window window)
        {
            try
            {
                if (window.AppWindow.Presenter is OverlappedPresenter presenter
                    && presenter.State == OverlappedPresenterState.Minimized)
                {
                    presenter.Restore();
                }

                window.AppWindow.IsShownInSwitchers = true;
                window.AppWindow.Show();
                window.Activate();

                IntPtr hwnd = WindowNative.GetWindowHandle(window);
                if (hwnd != IntPtr.Zero)
                    SetForegroundWindow(hwnd);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"BringWindowToForeground failed: {ex.Message}");
            }
        }

        private void OnResumeApply()
        {
            if (_isExitingProcess || !_gammaInitialized)
                return;

            try
            {
                EnsureResidentLifetime();
                _gammaPreviewActive = false;
                RestartGammaTimers();
                ApplyCurrentGamma(forceReapply: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnResumeApply failed: {ex.Message}");
            }
        }

        private void OnResumeApplyDirect()
        {
            if (_isExitingProcess || !_gammaInitialized)
                return;

            _gammaPreviewActive = false;
            ForceApplyExpectedGammaDirect();
        }

        private void OnResumeWatchdog()
        {
            if (_isExitingProcess || !_gammaInitialized)
                return;

            EnsureResidentLifetime();
            EnsureGammaApplied();
            RestartGammaTimers();
        }

        private bool NeedsGammaForce()
        {
            if (_isExitingProcess || !_gammaInitialized || _gammaPreviewActive)
                return false;
            if (!TryGetExpectedGammaSettings(out var expected))
                return false;
            return !GammaController.IsLikelyApplied(expected);
        }

        /// <summary>UI 更新なしで期待ガンマを ForceApply（任意スレッド可）。</summary>
        private void ForceApplyExpectedGammaDirect()
        {
            try
            {
                if (!TryGetExpectedGammaSettings(out var expected))
                {
                    if (!_settings.IsFilterEnabled || !_patterns.Any())
                        _gammaTransition.ForceApply(GammaSettings.Off);
                    return;
                }

                _gammaTransition.ForceApply(expected);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ForceApplyExpectedGammaDirect failed: {ex.Message}");
            }
        }

        /// <summary>スリープ復帰後などにスケジュール用 DispatcherTimer を起こす。</summary>
        private void RestartGammaTimers()
        {
            ScheduleNextGammaCheck();
        }

        private void EnsureGammaApplied()
        {
            if (_isExitingProcess || !_gammaInitialized || _gammaPreviewActive)
                return;

            if (_resumeCoordinator?.ShouldForceApply == true)
            {
                ApplyCurrentGamma(forceReapply: true);
                return;
            }

            if (!TryGetExpectedGammaSettings(out var expected))
                return;

            if (GammaController.IsLikelyApplied(expected))
                return;

            _gammaTransition.ForceApply(expected);
        }

        private bool TryGetExpectedGammaSettings(out GammaSettings settings)
        {
            settings = GammaSettings.Off;

            if (!_settings.IsFilterEnabled || !_patterns.Any())
                return false;

            var currentPattern = ScheduleHelper.ResolveActivePattern(_patterns, DateTime.Now);
            if (currentPattern == null)
                return false;

            settings = GammaSettings.FromPattern(currentPattern);
            return true;
        }

        private void ScheduleNextGammaCheck()
        {
            _gammaScheduleTimer.Stop();
            var delay = ScheduleHelper.GetDelayUntilNextTransition(_patterns, DateTime.Now);
            _gammaScheduleTimer.Interval = delay ?? TimeSpan.FromHours(1);
            _gammaScheduleTimer.Start();
        }

        private void ApplyCurrentGamma(bool forceReapply = false)
        {
            try
            {
                ApplyCurrentGammaCore(forceReapply);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyCurrentGamma failed: {ex.Message}");
            }
        }

        private void ApplyCurrentGammaCore(bool forceReapply)
        {
            if (_gammaPreviewActive)
                return;

            if (!_settings.IsFilterEnabled)
            {
                ApplyGamma(GammaSettings.Off, forceReapply);
                _appState.UpdateRuntimeStatus(
                    Strings.Get("Status_FilterDisabled"),
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
                    null,
                    null);
                return;
            }

            if (!_patterns.Any())
            {
                ApplyGamma(GammaSettings.Off, forceReapply);
                _appState.UpdateRuntimeStatus(
                    Strings.Get("Status_NoSchedule"),
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
                    null,
                    null);
                return;
            }

            var currentPattern = ScheduleHelper.ResolveActivePattern(_patterns, DateTime.Now);
            if (currentPattern == null)
            {
                ApplyGamma(GammaSettings.Off, forceReapply);
                return;
            }

            var gamma = GammaSettings.FromPattern(currentPattern);
            ApplyGamma(gamma, forceReapply);
            _appState.UpdateRuntimeStatus(
                Strings.Format(
                    "Status_Applied",
                    gamma.Intensity,
                    gamma.ColorTemperatureKelvin,
                    currentPattern.TimeRangeDisplay),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success,
                gamma,
                currentPattern);
        }

        private void ApplyGamma(GammaSettings settings, bool forceReapply)
        {
            if (forceReapply)
                _gammaTransition.ForceApply(settings);
            else
                _gammaTransition.AnimateTo(settings);
        }

        private static void RegisterGammaResetOnExit()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => GammaController.ResetGamma();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => GammaController.ResetGamma();
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        internal static void AppendLifetimeLog(string reason)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "BlueShift");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "lifetime.log"),
                    $"{DateTime.UtcNow:O} {reason}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
