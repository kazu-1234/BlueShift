using System;
using WinUiShared;

namespace App1
{
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // --exit（インストーラからの終了依頼）、--sync-autostart / --cleanup-autostart、WinUI の起動は共通処理
            // （WinUiShared.AppBootstrap）。このアプリ固有の引数（--reset-gamma）だけ HandlePreLaunchArgs で処理する
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

            return false;
        }
    }
}
