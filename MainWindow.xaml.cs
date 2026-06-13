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

        /// <summary>ログオン時タスクなど --background で起動した場合 true。</summary>
        private readonly bool _launchInBackgroundMode;

        /// <summary>起動直後にユーザーが GUI を見たい場合 true（通常起動・二重起動）。</summary>
        private bool _userWantsVisible;

        private TrayMessageWindow? _trayMessageWindow;
        private bool _canHideToTray;
        private bool _isExiting;
        private bool _uiInitialized;
        private bool _uiRenderedOnce;
        private bool _trayInitialized;
        private bool _gammaInitialized;
        private IntPtr _hwnd;
        private string _currentPageTag = "Time";
        private CancellationTokenSource? _interactiveShowListenerCts;

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
            _patterns = new ObservableCollection<Pattern>(_settings.Patterns.OrderBy(p => p.Time));
            _appState = new AppState(_settings, _patterns);
            _appState.SavePatterns = () => { };
            _appState.RefreshGamma = ApplyCurrentGamma;
            _appState.RescheduleTimer = ScheduleNextGammaCheck;

            StartupManager.MigrateFromLegacyIfNeeded();
            SyncAutoStartSetting();

            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;

            AppWindow.Closing += AppWindow_Closing;
            RootGrid.Loaded += RootGrid_Loaded;
            ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;

            StartInteractiveShowListener(interactiveShowEvent);
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

            AppWindow.ResizeClient(new SizeInt32(DefaultClientWidth, DefaultClientHeight));
            ConfigureMinimumWindowSize();

            // Loaded 処理中の Navigate は MeasureOverride を壊すため低優先度で defer する。
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_isExiting)
                    return;

                try
                {
                    NavigateToPage("Time", force: true);
                    ShowMainWindow();
                    CompositionTarget.Rendering += OnFirstFrameRendered;
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
        /// バックグラウンド起動時のみ、UI が一度描画されたあとでタスクトレイへ隠す。
        /// </summary>
        private void ApplyBackgroundVisibilityPolicy()
        {
            if (_userWantsVisible)
                return;

            if (_launchInBackgroundMode && _canHideToTray)
                HideToTray();
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
            GammaController.ResetGamma();
            ApplyCurrentGamma();
            ScheduleNextGammaCheck();
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

        private void SyncAutoStartSetting()
        {
            bool isAutoStart = StartupManager.IsAutoStartEnabled();
            if (isAutoStart != _settings.AutoStart)
            {
                _settings.AutoStart = isAutoStart;
                _settings.Save();
            }
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

            AppWindow.IsShownInSwitchers = false;
            AppWindow.Hide();
            EnsureTrayIconVisible();
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

        private void ExitApplication()
        {
            _isExiting = true;
            CompositionTarget.Rendering -= OnFirstFrameRendered;
            _interactiveShowListenerCts?.Cancel();
            _interactiveShowListenerCts?.Dispose();
            _interactiveShowListenerCts = null;

            _timer.Stop();
            _trayMessageWindow?.Dispose();
            _trayMessageWindow = null;
            GammaController.ResetGamma();
            AppWindow.Closing -= AppWindow_Closing;
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

        private void ApplyCurrentGamma()
        {
            if (!_settings.IsFilterEnabled)
            {
                GammaController.ResetGamma();
                _appState.UpdateRuntimeStatus(
                    Strings.Get("Status_FilterDisabled"),
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
                    null,
                    null);
                return;
            }

            if (!_patterns.Any())
            {
                GammaController.ResetGamma();
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
                GammaController.ResetGamma();
                return;
            }

            GammaController.SetGamma(currentPattern.Intensity);
            _appState.UpdateRuntimeStatus(
                Strings.Format("Status_Applied", currentPattern.Intensity, currentPattern.TimeRangeDisplay),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success,
                currentPattern.Intensity,
                currentPattern);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
