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
        private readonly bool _startInBackground;

        private TrayMessageWindow? _trayMessageWindow;
        private bool _canHideToTray;
        private bool _isExiting;
        private bool _initialSetupDone;
        private bool _rootGridReady;
        private bool _windowActivated;
        private bool _gammaInitialized;
        private IntPtr _hwnd;
        private string _currentPageTag = "Time";
        private CancellationTokenSource? _showWindowListenerCts;

        public MainWindow(bool startInBackground = false, EventWaitHandle? showWindowEvent = null)
        {
            _startInBackground = startInBackground;

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
            Activated += MainWindow_Activated;
            RootGrid.Loaded += RootGrid_Loaded;
            ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;

            SetupTrayIcon();
            EnsureTrayIconVisible();
            StartShowWindowListener(showWindowEvent);
        }

        private void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            RootGrid.Loaded -= RootGrid_Loaded;
            _rootGridReady = true;
            TryCompleteInitialSetup();
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
                return;

            _windowActivated = true;
            TryCompleteInitialSetup();
        }

        /// <summary>
        /// ルート UI とウィンドウの両方が準備できてからページ表示する。
        /// 片方だけだと NavigationView が描画されないことがある。
        /// </summary>
        private void TryCompleteInitialSetup()
        {
            if (_initialSetupDone || !_rootGridReady || !_windowActivated)
                return;

            _initialSetupDone = true;
            Activated -= MainWindow_Activated;

            AppWindow.ResizeClient(new SizeInt32(DefaultClientWidth, DefaultClientHeight));
            ConfigureMinimumWindowSize();
            NavigateToPage("Time");

            if (_startInBackground && _canHideToTray)
                HideToTray();
            else
                ShowMainWindow();

            // 初回フレーム描画後にガンマを適用する（GDI 操作が WinUI 合成を壊さないようにする）。
            CompositionTarget.Rendering += OnFirstFrameRendered;

            // 描画が遅延した環境向けに、1 フレーム後にもう一度レイアウトと表示を試みる。
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (_isExiting)
                    return;

                if (!_startInBackground || !_canHideToTray)
                    ShowMainWindow();

                RootGrid.UpdateLayout();
                ContentFrame.UpdateLayout();
            });
        }

        private void OnFirstFrameRendered(object? sender, object e)
        {
            if (_gammaInitialized)
                return;

            _gammaInitialized = true;
            CompositionTarget.Rendering -= OnFirstFrameRendered;

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

        private void StartShowWindowListener(EventWaitHandle? showWindowEvent)
        {
            if (showWindowEvent == null) return;

            _showWindowListenerCts = new CancellationTokenSource();
            var token = _showWindowListenerCts.Token;

            Task.Run(() =>
            {
                while (!token.IsCancellationRequested && !_isExiting)
                {
                    try
                    {
                        if (!showWindowEvent.WaitOne(500))
                            continue;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    if (token.IsCancellationRequested || _isExiting)
                        break;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ShowMainWindow(bringToForeground: true);
                        NavigateToPage("Time");
                    });
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

        /// <summary>
        /// WinUI は Win32 の ShowWindow を使わず AppWindow API のみで表示する。
        /// ShowWindow を HWND に対して呼ぶと白画面になる既知の不具合がある。
        /// </summary>
        private void ShowMainWindow(bool bringToForeground = false)
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.Restore();

            AppWindow.IsShownInSwitchers = true;
            AppWindow.Show();
            Activate();

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
            if (!_canHideToTray)
                return;

            AppWindow.IsShownInSwitchers = false;
            AppWindow.Hide();
            EnsureTrayIconVisible();
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_isExiting) return;

            if (!_canHideToTray)
                return;

            args.Cancel = true;
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
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ShowMainWindow(bringToForeground: true);
                        NavigateToPage("Time");
                    });
                };
                tray.OpenSettingsRequested += () =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ShowMainWindow(bringToForeground: true);
                        NavigateToPage("Settings");
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
            _showWindowListenerCts?.Cancel();
            _showWindowListenerCts?.Dispose();
            _showWindowListenerCts = null;

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

        private void NavigateToPage(string tag)
        {
            if (_currentPageTag == tag && ContentFrame.CurrentSourcePageType != null)
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

            if (ContentFrame.CurrentSourcePageType != pageType)
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
