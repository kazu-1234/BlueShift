using System;
using WinUiShared;

namespace App1
{
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // --exit（インストーラからの終了依頼）と WinUI の起動は共通処理（WinUiShared.AppBootstrap）
            AppBootstrap.RunMain(args, AppSetup.Profile, HandlePreLaunchArgs, () => new App());
        }

        /// <summary>WinUI を起動せずに済む引数を処理する。処理したら true（そのまま終了）。</summary>
        private static bool HandlePreLaunchArgs(string[] args)
        {
            // WinUI を起動せず、画面のガンマだけ即座に戻す。
            if (AppBootstrap.HasArg(args, "--reset-gamma"))
            {
                GammaController.ResetGamma();
                return true;
            }

            // 自動起動登録だけ削除して終了（アプリ削除時の掃除用）。
            if (AppBootstrap.HasArg(args, AppBootstrap.CleanupAutostartArg))
            {
                StartupManager.CleanupAutostartOnly();
                return true;
            }

            // インストール直後など、設定に合わせてログオンタスクを現行 exe へ同期して終了。
            if (AppBootstrap.HasArg(args, AppBootstrap.SyncAutostartArg))
            {
                var settings = Settings.Load();
                StartupManager.SyncAutostartWithSettings(settings.AutoStart, settings.UseLogonTask);
                return true;
            }

            return false;
        }
    }
}
