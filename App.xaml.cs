// v1.4.0

using Microsoft.UI.Xaml;
using System;
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
            // × で MainWindow を破棄してもプロセスは継続する（トレイ常駐 / 明示 Exit まで終了しない）。
            // ウィンドウ型でも同じ設定にし、終了は AppRuntimeBase.ExitApplication の 1 経路に揃える
            DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
            UnhandledException += (_, e) => AppBootstrap.HandleUnhandledException(e);
            // UI スレッド以外の例外と、待たれずに失敗した Task の例外も lifetime.log に残す
            AppBootstrap.InstallGlobalExceptionLogging();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            var settings = Settings.Load();

            // 単一インスタンス・自動起動の移行と同期は共通処理。二重起動なら null（既存の窓に表示を頼んで終了済み）
            PrimaryLaunch? launch = AppBootstrap.BeginPrimaryLaunch(this, settings);
            if (launch == null)
                return;

            _runtime = new AppRuntime(this, settings);
            _runtime.Start(launch.LaunchInBackground, launch.RequestInteractiveShow);
        }
    }
}
