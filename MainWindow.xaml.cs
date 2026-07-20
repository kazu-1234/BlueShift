using App1.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.Graphics;

namespace App1
{
    public sealed partial class MainWindow : Window
    {
        private const int DefaultClientWidth = 960;
        private const int DefaultClientHeight = 680;
        private const double MinimumWindowWidth = 870;
        private const double MinimumWindowHeight = 600;

        private readonly AppRuntime _runtime;
        private readonly AppState _appState;
        private bool _windowBoundsReady;
        private string _currentPageTag = "Time";
        private TitleBarThemeHelper? _titleBarThemeHelper;

        public MainWindow(AppRuntime runtime)
        {
            _runtime = runtime;
            _appState = runtime.AppState;

            InitializeComponent();
            Title = Strings.Get("AppName");
            AppTitleBar.Title = Strings.Get("AppName");
            ApplyWindowIcon();

            ThemeService.AttachRoot(RootGrid);
            _titleBarThemeHelper = new TitleBarThemeHelper(this, RootGrid, AppTitleBar);

            AppWindow.Closing += AppWindow_Closing;
            AppWindow.Changed += AppWindow_Changed;
            ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;

            ConfigureMinimumWindowSize();
        }

        /// <summary>
        /// Auto Dark Mode と同じ順序: Navigate → 位置サイズ復元 → Activate。
        /// </summary>
        public void PrepareAndActivate(string? initialPageTag = null)
        {
            string tag = string.IsNullOrEmpty(initialPageTag) ? GetDefaultPageTag() : initialPageTag;
            NavigateToPage(tag, force: true, suppressTransition: true);
            RestoreWindowBounds();
            _windowBoundsReady = true;

            AppWindow.IsShownInSwitchers = true;
            Activate();
        }

        public void NavigateToPageTag(string tag) => NavigateToPage(tag, force: false, suppressTransition: true);

        internal void SaveWindowBoundsFromRuntime()
        {
            SaveWindowBounds();
        }

        private static string GetDefaultPageTag() => "Time";

        private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            Title = $"BlueShift - {e.Exception?.Message}";
        }

        private void ApplyWindowIcon()
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "BlueShift.ico");
            if (!File.Exists(iconPath))
                return;

            try
            {
                AppWindow.SetIcon(iconPath);
            }
            catch
            {
                // SetIcon 失敗時はトレイ側のフォールバックに任せる
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

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (_runtime.IsExitingProcess || !_windowBoundsReady)
                return;

            if (args.DidSizeChange || args.DidPositionChange)
                SaveWindowBounds();
        }

        private void SaveWindowBounds()
        {
            if (!_windowBoundsReady)
                return;

            WindowPlacementHelper.Save(this, _runtime.Settings);
        }

        private void RestoreWindowBounds()
        {
            WindowPlacementHelper.Restore(this, _runtime.Settings, DefaultClientWidth, DefaultClientHeight);
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (_runtime.IsExitingProcess)
                return;

            SaveWindowBounds();
            _runtime.OnMainWindowClosing(this);

#if DEBUG
            if (Debugger.IsAttached)
            {
                args.Cancel = false;
                Closed += MainWindow_ClosedDebugExit;
                AppWindow.Closing -= AppWindow_Closing;
                AppWindow.Changed -= AppWindow_Changed;
                return;
            }
#endif

            // × は本当に Close（Hide しない）。プロセス・トレイ・ガンマは AppRuntime が継続
            args.Cancel = false;
            AppWindow.Closing -= AppWindow_Closing;
            AppWindow.Changed -= AppWindow_Changed;
        }

        private void MainWindow_ClosedDebugExit(object sender, WindowEventArgs e)
        {
            Closed -= MainWindow_ClosedDebugExit;
            if (!_runtime.IsExitingProcess)
                _runtime.ExitApplication();
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
                NavigateToPage(tag);
        }

        private void NavigateToPage(string tag, bool force = false, bool suppressTransition = false)
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
            {
                if (suppressTransition)
                    ContentFrame.Navigate(pageType, _appState, new SuppressNavigationTransitionInfo());
                else
                    ContentFrame.Navigate(pageType, _appState);
            }

            UpdateNavSelection(tag);
        }

        private void UpdateNavSelection(string tag)
        {
            NavigationViewItem? match = null;
            foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
            {
                if (item.Tag as string == tag)
                    match = item;
            }

            foreach (var item in NavView.FooterMenuItems.OfType<NavigationViewItem>())
            {
                if (item.Tag as string == tag)
                    match = item;
            }

            if (match != null)
                NavView.SelectedItem = match;
            else if (NavItemHome != null)
                NavView.SelectedItem = NavItemHome;
        }
    }
}
