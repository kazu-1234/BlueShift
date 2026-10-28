// v1.3.1

using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using WinUiShared;

namespace App1
{
    public partial class App : Application
    {
        private AppRuntime? _runtime;

        internal static AppRuntime Runtime =>
            (Current as App)?._runtime
            ?? throw new InvalidOperationException("App runtime is not initialized.");

        public App()
        {
            InitializeComponent();
            // × で MainWindow を破棄してもトレイ常駐を続ける（明示 Exit まで終了しない）
            DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
            // 未処理例外は全アプリ共通の処理へ（lifetime.log に記録し、落とさずに続ける）
            UnhandledException += (_, e) => AppBootstrap.HandleUnhandledException(e);
        }

        internal static void ReportStartupError(string message)
        {
            Debug.WriteLine($"BlueShift error: {message}");
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // --cleanup-autostart / --sync-autostart は Program.Main（WinUI 起動前）で処理済み

            var settings = Settings.Load();
            bool launchInBackground = AppBootstrap.HasCommandLineArg(AppBootstrap.BackgroundArg);
            bool requestInteractiveShow = !launchInBackground;

            if (!SingleInstance.TryBecomePrimaryInstance(requestInteractiveShow))
            {
                Exit();
                return;
            }

            StartupManager.MigrateFromLegacyIfNeeded();
#if !DEBUG
            // 起動時にログオンタスクを「今動いている exe」へ合わせ直す。
            // Debug ビルドで行うと、インストール版の登録が bin\Debug の exe に書き換わってしまうため Release のみ。
            // （設定画面で自動起動を切り替えたときや --sync-autostart の明示同期は Debug でも行う）
            StartupManager.SyncAutostartWithSettings(settings.AutoStart, settings.UseLogonTask);
#endif

            try
            {
                // 起動判定で読んだ設定をそのまま渡す（以前は AppRuntime 内で読み直していた。同じ内容の 2 個目を作らない）
                _runtime = new AppRuntime(this, settings);
                _runtime.Start(launchInBackground, requestInteractiveShow);
            }
            catch (Exception ex)
            {
                ReportStartupError(ex.ToString());
                throw;
            }
        }
    }
}
