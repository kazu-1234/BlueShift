using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinRT.Interop;

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
        private readonly DispatcherTimer _gammaWatchdogTimer = new() { Interval = TimeSpan.FromSeconds(30) };

        /// <summary>開始時点からの絶対遅延（累積 Delay にしない）。SPM と同型。</summary>
        private static readonly int[] GammaReapplyDelaysMs = { 800, 2000, 5000, 15000 };

        private MainWindow? _mainWindow;
        private TrayMessageWindow? _trayMessageWindow;
        private SystemEventWindow? _systemEventWindow;
        private CancellationTokenSource? _listenerCts;
        private CancellationTokenSource? _gammaReapplyCts;
        private bool _gammaInitialized;
        private bool _gammaPreviewActive;
        private bool _trayInitialized;
        private bool _isExitingProcess;
        /// <summary>スリープ復帰後など、IsLikelyApplied を信用せず Force する期限（UTC）。</summary>
        private DateTime _unconditionalReapplyUntil = DateTime.MinValue;

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

            if (!ShouldUseTray())
            {
                EnsureGamma();
                if (requestInteractiveShow || !launchInBackground)
                    ShowOrCreateMainWindow();
                return;
            }

            EnsureTray();
            EnsureGamma();

            if (requestInteractiveShow || !launchInBackground)
                ShowOrCreateMainWindow();
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
        }

        public void OnMainWindowClosing(MainWindow window)
        {
            if (_isExitingProcess || window != _mainWindow)
                return;

            window.SaveWindowBoundsFromRuntime();
        }

        public void ExitApplication()
        {
            if (_isExitingProcess)
                return;

            _isExitingProcess = true;
            _listenerCts?.Cancel();
            _listenerCts?.Dispose();
            _listenerCts = null;
            _gammaReapplyCts?.Cancel();
            _gammaReapplyCts?.Dispose();
            _gammaReapplyCts = null;

            _gammaScheduleTimer.Stop();
            _gammaWatchdogTimer.Stop();
            _gammaTransition.Stop();
            _systemEventWindow?.Dispose();
            _systemEventWindow = null;
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
            ExitApplication();
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

        private void EnsureTray()
        {
            if (_trayInitialized)
                return;

            _trayInitialized = true;
            try
            {
                _trayMessageWindow = new TrayMessageWindow();
                var tray = _trayMessageWindow.TrayIcon;
                tray.OpenMainWindowRequested += () => ShowOrCreateMainWindow();
                tray.OpenSettingsRequested += () => ShowOrCreateMainWindow("Settings");
                tray.ExitRequested += () => GetDispatcherQueue()?.TryEnqueue(ExitApplication);
                ApplyTrayIconVisibility();
            }
            catch
            {
                _trayMessageWindow?.Dispose();
                _trayMessageWindow = null;
                _trayInitialized = false;
            }
        }

        private void EnsureGamma()
        {
            if (_gammaInitialized)
                return;

            _gammaInitialized = true;
            try
            {
                _systemEventWindow = new SystemEventWindow();
                _systemEventWindow.SystemDisplayStateChanged += () =>
                    GetDispatcherQueue()?.TryEnqueue(RequestGammaReapply);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"System event monitor init failed: {ex.Message}");
            }

            _gammaScheduleTimer.Tick += (_, _) =>
            {
                if (_gammaPreviewActive)
                    return;
                ApplyCurrentGamma();
                ScheduleNextGammaCheck();
            };
            _gammaWatchdogTimer.Tick += (_, _) => EnsureGammaApplied();

            // 起動時は Off→目標をゆっくり遷移し、アニメ完了後に遅延 Force で GDI/OS 上書きを吸収する。
            _gammaTransition.ResetToOff(applyToDisplay: true);
            ApplyCurrentGamma(forceReapply: false);
            ScheduleDelayedGammaReapplies(
                baseOffsetMs: (int)GammaTransitionService.DefaultAnimationDuration.TotalMilliseconds);
            ScheduleNextGammaCheck();
            _gammaWatchdogTimer.Start();
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
                Task.Run(() => ListenLoop(exitEvent, token, () => GetDispatcherQueue()?.TryEnqueue(ExitApplication)), token);
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
                    if (!handle.WaitOne(500))
                        continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (token.IsCancellationRequested)
                    break;

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

        private void RequestGammaReapply()
        {
            if (_isExitingProcess || !_gammaInitialized || _gammaPreviewActive)
                return;

            // ドライバが GetDeviceGammaRamp を嘘で返す間は IsLikelyApplied を信用しない
            _unconditionalReapplyUntil = DateTime.UtcNow.AddSeconds(60);

            // スリープ復帰後は DispatcherTimer が止まることがあるため、再適用前に明示的に再始動する
            RestartGammaTimers();
            ApplyCurrentGamma(forceReapply: true);
            ScheduleDelayedGammaReapplies();
        }

        /// <summary>スリープ復帰後などにスケジュール／ウォッチドッグ用 DispatcherTimer を起こす。</summary>
        private void RestartGammaTimers()
        {
            if (_gammaWatchdogTimer.IsEnabled)
                _gammaWatchdogTimer.Stop();
            _gammaWatchdogTimer.Start();
            ScheduleNextGammaCheck();
        }

        private void EnsureGammaApplied()
        {
            if (_isExitingProcess || !_gammaInitialized || _gammaPreviewActive)
                return;

            if (DateTime.UtcNow < _unconditionalReapplyUntil)
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

        /// <param name="baseOffsetMs">起動アニメ完了待ちなど、遅延列全体を後ろへずらす量。</param>
        private void ScheduleDelayedGammaReapplies(int baseOffsetMs = 0)
        {
            _gammaReapplyCts?.Cancel();
            _gammaReapplyCts?.Dispose();
            _gammaReapplyCts = new CancellationTokenSource();
            var token = _gammaReapplyCts.Token;

            Task.Run(async () =>
            {
                var start = DateTime.UtcNow;
                foreach (int delayMs in GammaReapplyDelaysMs)
                {
                    try
                    {
                        var wait = start.AddMilliseconds(baseOffsetMs + delayMs) - DateTime.UtcNow;
                        if (wait > TimeSpan.Zero)
                            await Task.Delay(wait, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }

                    if (token.IsCancellationRequested || _isExitingProcess)
                        break;

                    GetDispatcherQueue()?.TryEnqueue(() =>
                    {
                        if (_isExitingProcess || !_gammaInitialized || _gammaPreviewActive)
                            return;
                        // 起動アニメ中に Force すると遷移が潰れるためスキップ（オフセット後は通常完了済み）
                        if (_gammaTransition.IsAnimating)
                            return;
                        ApplyCurrentGamma(forceReapply: true);
                    });
                }
            }, token);
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
    }
}
