using App1.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using WinRT.Interop;

namespace App1
{
    public sealed partial class MainWindow : Window
    {
        private const int DefaultClientWidth = 960;
        private const int DefaultClientHeight = 680;
        private const double MinimumWindowWidth = 870;
        private const double MinimumWindowHeight = 600;

        private readonly Settings _settings;
        private readonly ObservableCollection<Pattern> _patterns;
        private readonly AppState _appState;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _gammaWatchdogTimer;
        private readonly GammaTransitionService _gammaTransition;

        /// <summary>ログオン時タスクなど --background で起動した場合 true。</summary>
        private readonly bool _launchInBackgroundMode;

        /// <summary>起動直後にユーザーが GUI を見たい場合 true（通常起動・二重起動）。</summary>
        private bool _userWantsVisible;

        private TrayMessageWindow? _trayMessageWindow;
        private SystemEventWindow? _systemEventWindow;
        private bool _canHideToTray;
        private bool _isExiting;
        private bool _uiInitialized;
        private bool _uiRenderedOnce;
        private bool _trayInitialized;
        private bool _gammaInitialized;
        private bool _gammaPreviewActive;
        private IntPtr _hwnd;
        private string _currentPageTag = "Time";
        private CancellationTokenSource? _interactiveShowListenerCts;
        private CancellationTokenSource? _gammaReapplyCts;
        private TitleBarThemeHelper? _titleBarThemeHelper;

        public MainWindow(
            bool launchInBackground = false,
            bool requestVisibleOnLaunch = true,
            EventWaitHandle? interactiveShowEvent = null)
        {
            _launchInBackgroundMode = launchInBackground;
            _userWantsVisible = requestVisibleOnLaunch;

            InitializeComponent();
            Title = Strings.Get("AppName");
            ApplyWindowIcon();

            _settings = Settings.Load();
            ThemeService.AttachRoot(RootGrid);
            _titleBarThemeHelper = new TitleBarThemeHelper(this, RootGrid);

            _patterns = new ObservableCollection<Pattern>(_settings.Patterns.OrderBy(p => p.Time));
            _appState = new AppState(_settings, _patterns);
            _appState.SavePatterns = () => { };
            _gammaTransition = new GammaTransitionService();
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

            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;

            _gammaWatchdogTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _gammaWatchdogTimer.Tick += GammaWatchdogTimer_Tick;

            AppWindow.Closing += AppWindow_Closing;
            AppWindow.Changed += AppWindow_Changed;
            RootGrid.Loaded += RootGrid_Loaded;
            ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;
            Activated += MainWindow_Activated;

            StartInteractiveShowListener(interactiveShowEvent);
        }

        /// <summary>--background 起動時、Activate 直後にウィンドウを隠してフラッシュを防ぐ。</summary>
        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                _titleBarThemeHelper?.ScheduleUpdate();
                EnsureGammaApplied();
            }

            if (_userWantsVisible || !_launchInBackgroundMode)
                return;

            AppWindow.IsShownInSwitchers = false;
            AppWindow.Hide();
        }

        /// <summary>
        /// UI 初期化の唯一の入口。Win32 / タスクトレイ / ガンマはここより前に実行しない。
        /// </summary>
        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_uiInitialized)
                return;

            _uiInitialized = true;
            RootGrid.Loaded -= RootGrid_Loaded;

            RestoreWindowBounds();
            ConfigureMinimumWindowSize();

            // Loaded 処理中の Navigate は MeasureOverride を壊すため低優先度で defer する。
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_isExiting)
                    return;

                try
                {
                    NavigateToPage("Time", force: true);

                    if (_userWantsVisible)
                    {
                        ShowMainWindow();
                        CompositionTarget.Rendering += OnFirstFrameRendered;
                    }
                    else
                    {
                        // ログオン自動起動: ウィンドウ非表示のままトレイ・ガンマを初期化
                        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, InitializeBackgroundServices);
                    }
                }
                catch (Exception ex)
                {
                    Title = $"BlueShift - {ex.Message}";
                }
            });
        }

        private void OnFirstFrameRendered(object? sender, object e)
        {
            if (_uiRenderedOnce)
                return;

            _uiRenderedOnce = true;
            CompositionTarget.Rendering -= OnFirstFrameRendered;

            InitializeTrayIfNeeded();
            InitializeGammaIfNeeded();
            ApplyBackgroundVisibilityPolicy();
        }

        /// <summary>
        /// --background 起動時: 画面描画を待たずタスクトレイとガンマを有効化する。
        /// </summary>
        private void InitializeBackgroundServices()
        {
            if (_isExiting || _uiRenderedOnce)
                return;

            _uiRenderedOnce = true;
            InitializeTrayIfNeeded();
            InitializeGammaIfNeeded();
            ApplyBackgroundVisibilityPolicy();
        }

        /// <summary>
        /// バックグラウンド起動時のみ、UI が一度描画されたあとでタスクトレイへ隠す。
        /// </summary>
        private void ApplyBackgroundVisibilityPolicy()
        {
            if (_userWantsVisible)
                return;

            if (!_launchInBackgroundMode)
                return;

            // ログオン自動起動: タスクトレイ常駐のみ（ウィンドウは非表示のまま）
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Hide();
            EnsureTrayIconVisible();
        }

        private void RequestInteractiveShow()
        {
            _userWantsVisible = true;

            if (!_uiInitialized)
                return;

            ShowMainWindow(bringToForeground: true, forceRefresh: true);

            if (_uiRenderedOnce)
                InitializeTrayIfNeeded();
        }

        private void InitializeTrayIfNeeded()
        {
            if (_trayInitialized)
                return;

            _trayInitialized = true;
            SetupTrayIcon();
            EnsureTrayIconVisible();
        }

        private void InitializeGammaIfNeeded()
        {
            if (_gammaInitialized)
                return;

            _gammaInitialized = true;
            InitializeSystemEventMonitor();
            GammaController.ResetGamma();
            ApplyCurrentGamma();
            ScheduleNextGammaCheck();
            _gammaWatchdogTimer.Start();
        }

        private void InitializeSystemEventMonitor()
        {
            if (_systemEventWindow != null)
                return;

            try
            {
                _systemEventWindow = new SystemEventWindow();
                _systemEventWindow.SystemDisplayStateChanged += OnSystemDisplayStateChanged;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"System event monitor init failed: {ex.Message}");
            }
        }

        private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            Title = $"BlueShift - {e.Exception?.Message}";
        }

        private void EnsureTrayIconVisible()
        {
            if (!_canHideToTray)
                return;

            _trayMessageWindow?.TrayIcon.Show();
        }

        private void StartInteractiveShowListener(EventWaitHandle? interactiveShowEvent)
        {
            if (interactiveShowEvent == null)
                return;

            _interactiveShowListenerCts = new CancellationTokenSource();
            var token = _interactiveShowListenerCts.Token;

            Task.Run(() =>
            {
                while (!token.IsCancellationRequested && !_isExiting)
                {
                    try
                    {
                        if (!interactiveShowEvent.WaitOne(500))
                            continue;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (token.IsCancellationRequested || _isExiting)
                        break;

                    DispatcherQueue.TryEnqueue(RequestInteractiveShow);
                }
            }, token);
        }

        private void ApplyWindowIcon()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "BlueShift.ico");
            if (File.Exists(iconPath))
                AppWindow.SetIcon(iconPath);
        }

        private void EnsureHwnd()
        {
            if (_hwnd == IntPtr.Zero)
                _hwnd = WindowNative.GetWindowHandle(this);
        }

        private void ShowMainWindow(bool bringToForeground = false, bool forceRefresh = false)
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.Restore();

            AppWindow.IsShownInSwitchers = true;
            AppWindow.Show();
            Activate();

            if (forceRefresh && _uiInitialized)
                NavigateToPage(_currentPageTag, force: true);

            if (bringToForeground)
            {
                EnsureHwnd();
                SetForegroundWindow(_hwnd);
            }
        }

        private void ConfigureMinimumWindowSize()
        {
            if (AppWindow.Presenter is not OverlappedPresenter presenter)
                return;

            presenter.IsResizable = true;
            double scaleFactor = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            presenter.PreferredMinimumWidth = (int)(MinimumWindowWidth * scaleFactor);
            presenter.PreferredMinimumHeight = (int)(MinimumWindowHeight * scaleFactor);
            presenter.PreferredMaximumWidth = 10000;
            presenter.PreferredMaximumHeight = 10000;
        }

        private void HideToTray()
        {
            if (!_canHideToTray || !_uiRenderedOnce)
                return;

            SaveWindowBounds();
            AppWindow.IsShownInSwitchers = false;
            AppWindow.Hide();
            EnsureTrayIconVisible();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (_isExiting)
                return;

            if (args.DidSizeChange || args.DidPositionChange)
                SaveWindowBounds();
        }

        private void SaveWindowBounds()
        {
            if (AppWindow.Presenter is not OverlappedPresenter presenter)
                return;

            if (presenter.State == OverlappedPresenterState.Maximized)
            {
                // 最大化中は復元用の通常サイズ・位置を上書きしない
                _settings.WindowMaximized = true;
                _settings.Save();
                return;
            }

            if (presenter.State != OverlappedPresenterState.Restored)
                return;

            var size = AppWindow.Size;
            if (size.Width < 400 || size.Height < 300)
                return;

            // 最大化遷移中に「作業領域ほぼいっぱい」のサイズが来ても復元サイズを壊さない
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            if (display != null)
            {
                var work = display.WorkArea;
                if (size.Width >= work.Width - 8 && size.Height >= work.Height - 8)
                {
                    _settings.WindowMaximized = true;
                    _settings.Save();
                    return;
                }
            }

            var pos = AppWindow.Position;
            _settings.WindowMaximized = false;
            _settings.WindowWidth = size.Width;
            _settings.WindowHeight = size.Height;
            _settings.WindowX = pos.X;
            _settings.WindowY = pos.Y;
            _settings.Save();
        }

        private void RestoreWindowBounds()
        {
            int width = _settings.WindowWidth > 0 ? _settings.WindowWidth : DefaultClientWidth;
            int height = _settings.WindowHeight > 0 ? _settings.WindowHeight : DefaultClientHeight;

            DisplayArea? display = null;
            if (_settings.WindowX >= 0 && _settings.WindowY >= 0)
                display = DisplayArea.GetFromPoint(new PointInt32(_settings.WindowX, _settings.WindowY), DisplayAreaFallback.Nearest);
            display ??= DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);

            if (display != null)
            {
                var work = display.WorkArea;

                // 以前最大化サイズが保存されていた場合はデフォルトに戻す
                if (width >= work.Width - 8 || height >= work.Height - 8)
                {
                    width = DefaultClientWidth;
                    height = DefaultClientHeight;
                }

                width = Math.Clamp(width, 400, work.Width);
                height = Math.Clamp(height, 300, work.Height);

                if (_settings.WindowMaximized)
                {
                    // 作業領域内に通常サイズを置いてから Maximize（タスクバー重なり防止）
                    AppWindow.Resize(new SizeInt32(width, height));
                    int x = work.X + Math.Max(0, (work.Width - width) / 2);
                    int y = work.Y + Math.Max(0, (work.Height - height) / 2);
                    AppWindow.Move(new PointInt32(x, y));
                    if (AppWindow.Presenter is OverlappedPresenter maximizedPresenter)
                        maximizedPresenter.Maximize();
                    return;
                }

                AppWindow.Resize(new SizeInt32(width, height));
                if (_settings.WindowX >= 0 && _settings.WindowY >= 0)
                {
                    int maxX = work.X + Math.Max(0, work.Width - width);
                    int maxY = work.Y + Math.Max(0, work.Height - height);
                    int x = Math.Clamp(_settings.WindowX, work.X, maxX);
                    int y = Math.Clamp(_settings.WindowY, work.Y, maxY);
                    AppWindow.Move(new PointInt32(x, y));
                }

                return;
            }

            AppWindow.Resize(new SizeInt32(width, height));
            if (_settings.WindowMaximized && AppWindow.Presenter is OverlappedPresenter fallbackPresenter)
                fallbackPresenter.Maximize();
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isExiting)
                return;

            if (!_canHideToTray)
                return;

            args.Cancel = true;
            _userWantsVisible = false;
            HideToTray();
        }

        private void SetupTrayIcon()
        {
            try
            {
                _trayMessageWindow = new TrayMessageWindow();
                _canHideToTray = true;

                var tray = _trayMessageWindow.TrayIcon;
                tray.OpenMainWindowRequested += () =>
                {
                    DispatcherQueue.TryEnqueue(() => RequestInteractiveShow());
                };
                tray.OpenSettingsRequested += () =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        RequestInteractiveShow();
                        NavigateToPage("Settings", force: true);
                    });
                };
                tray.ExitRequested += () =>
                {
                    DispatcherQueue.TryEnqueue(ExitApplication);
                };
            }
            catch
            {
                _trayMessageWindow?.Dispose();
                _trayMessageWindow = null;
                _canHideToTray = false;
            }
        }

        private void OnSystemDisplayStateChanged()
        {
            DispatcherQueue.TryEnqueue(RequestGammaReapply);
        }

        private void GammaWatchdogTimer_Tick(object? sender, object e)
        {
            EnsureGammaApplied();
        }

        private void RequestGammaReapply()
        {
            if (_isExiting || !_gammaInitialized || _gammaPreviewActive)
                return;

            ApplyCurrentGamma(forceReapply: true);
            ScheduleDelayedGammaReapplies();
        }

        private void EnsureGammaApplied()
        {
            if (_isExiting || !_gammaInitialized || _gammaPreviewActive)
                return;

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

        private void ScheduleDelayedGammaReapplies()
        {
            _gammaReapplyCts?.Cancel();
            _gammaReapplyCts?.Dispose();
            _gammaReapplyCts = new CancellationTokenSource();
            var token = _gammaReapplyCts.Token;

            Task.Run(async () =>
            {
                foreach (int delayMs in new[] { 800, 2000, 5000 })
                {
                    try
                    {
                        await Task.Delay(delayMs, token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }

                    if (token.IsCancellationRequested || _isExiting)
                        break;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_isExiting || !_gammaInitialized || _gammaPreviewActive)
                            return;

                        ApplyCurrentGamma(forceReapply: true);
                    });
                }
            }, token);
        }

        private void ExitApplication()
        {
            _isExiting = true;
            CompositionTarget.Rendering -= OnFirstFrameRendered;
            _interactiveShowListenerCts?.Cancel();
            _interactiveShowListenerCts?.Dispose();
            _interactiveShowListenerCts = null;
            _gammaReapplyCts?.Cancel();
            _gammaReapplyCts?.Dispose();
            _gammaReapplyCts = null;

            SaveWindowBounds();
            if (!_settings.AutoStart)
                StartupManager.SyncAutostartWithSettings(false);

            _timer.Stop();
            _gammaWatchdogTimer.Stop();
            _gammaTransition.Stop();
            _systemEventWindow?.Dispose();
            _systemEventWindow = null;
            _trayMessageWindow?.Dispose();
            _trayMessageWindow = null;
            GammaController.ResetGamma();
            AppWindow.Closing -= AppWindow_Closing;
            AppWindow.Changed -= AppWindow_Changed;
            SingleInstanceManager.Release();
            Close();
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                NavigateToPage("Settings");
                return;
            }

            if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
                NavigateToPage(tag);
        }

        private void NavigateToPage(string tag, bool force = false)
        {
            if (!force && _currentPageTag == tag && ContentFrame.CurrentSourcePageType != null)
            {
                UpdateNavSelection(tag);
                return;
            }

            _currentPageTag = tag;
            Type pageType = tag switch
            {
                "Info" => typeof(InfoPage),
                "Settings" => typeof(SettingsPage),
                _ => typeof(TimePage)
            };

            if (force || ContentFrame.CurrentSourcePageType != pageType)
                ContentFrame.Navigate(pageType, _appState);

            UpdateNavSelection(tag);
        }

        private void UpdateNavSelection(string tag)
        {
            foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
                item.IsSelected = item.Tag as string == tag;

            foreach (var item in NavView.FooterMenuItems.OfType<NavigationViewItem>())
                item.IsSelected = item.Tag as string == tag;
        }

        private void Timer_Tick(object? sender, object e)
        {
            if (_gammaPreviewActive)
                return;

            ApplyCurrentGamma();
            ScheduleNextGammaCheck();
        }

        private void ScheduleNextGammaCheck()
        {
            _timer.Stop();
            var delay = ScheduleHelper.GetDelayUntilNextTransition(_patterns, DateTime.Now);
            _timer.Interval = delay ?? TimeSpan.FromHours(1);
            _timer.Start();
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

            var settings = GammaSettings.FromPattern(currentPattern);
            ApplyGamma(settings, forceReapply);
            _appState.UpdateRuntimeStatus(
                Strings.Format(
                    "Status_Applied",
                    settings.Intensity,
                    settings.ColorTemperatureKelvin,
                    currentPattern.TimeRangeDisplay),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success,
                settings,
                currentPattern);
        }

        private void ApplyGamma(GammaSettings settings, bool forceReapply)
        {
            if (forceReapply)
                _gammaTransition.ForceApply(settings);
            else
                _gammaTransition.AnimateTo(settings);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
