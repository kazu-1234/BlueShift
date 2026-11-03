using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using App1.Views;
using WinUiShared;

namespace App1
{
    /// <summary>
    /// メインウィンドウ。テーマ・タイトルバー・窓の位置の保存と復元・左ナビのページ移動・閉じる処理は
    /// 共通部の MainWindowShell が行う。このファイルには、このアプリ固有の値（サイズ・ページ表）だけを書く。
    /// </summary>
    public sealed partial class MainWindow : Window, IAppMainWindow
    {
        // 保存値が無いときの窓の大きさ（物理ピクセル）と最小サイズ（DIP）
        private const int DefaultClientWidth = 960;
        private const int DefaultClientHeight = 680;
        private const double MinimumWindowWidth = 870;
        private const double MinimumWindowHeight = 600;

        private readonly MainWindowShell _shell;

        public MainWindow(AppRuntime runtime)
        {
            InitializeComponent();

            _shell = new MainWindowShell(this, new MainWindowShellOptions
            {
                RootElement = RootGrid,
                TitleBar = AppTitleBar,
                NavView = NavView,
                ContentFrame = ContentFrame,
                PlacementSettings = runtime.Settings,
                DefaultWidth = DefaultClientWidth,
                DefaultHeight = DefaultClientHeight,
                MinimumWidth = MinimumWindowWidth,
                MinimumHeight = MinimumWindowHeight,
                DefaultPageTag = "Time",
                IconFileName = "BlueShift.ico",
                ResolvePageType = ResolvePageType,
                NavigationParameter = () => runtime.AppState
            });
        }

        public void PrepareAndActivate(string? initialPageTag = null) => _shell.PrepareAndActivate(initialPageTag);

        public void NavigateToPageTag(string tag) => _shell.NavigateToPageTag(tag);

        public void SaveWindowBoundsFromRuntime() => _shell.SaveWindowBounds();

        /// <summary>左ナビのタグ → ページの型（知らないタグは時間のページ）。ページを足したらここと XAML の項目を足す。</summary>
        private static Type ResolvePageType(string tag) => tag switch
        {
            "Info" => typeof(InfoPage),
            "Settings" => typeof(SettingsPage),
            _ => typeof(TimePage)
        };

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args) =>
            _shell.OnNavItemInvoked(sender, args);
    }
}
