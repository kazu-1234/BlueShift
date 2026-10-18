using WinUiShared;

namespace App1
{
    /// <summary>
    /// このアプリの「型」と識別名（共通基盤 WinUiShared に渡す値はすべてここに集約する）。
    /// BlueShift は常駐型: トレイ常駐・ログオン自動起動・スリープ復帰時のガンマ再適用を使う。
    /// </summary>
    internal static class AppSetup
    {
        /// <summary>%AppData% のフォルダ名・Mutex 名・User-Agent などに使う識別名（名前空間 App1 とは別）。公開後は変えない。</summary>
        public const string AppName = "BlueShift";

        public static readonly AppProfile Profile = new()
        {
            AppName = AppName,
            // 旧版と同じ Mutex / イベント名（Global\BlueShift_SingleInstance_v1）にして、インストーラの --exit を届かせる
            InstanceNameVersion = "v1",
            LatestReleaseApiUrl = "https://api.github.com/repos/kazu-1234/BlueShift/releases/latest",

            CloseBehavior = WindowCloseBehavior.KeepRunning,
            UseTray = true,
            UseResumeMonitor = true,
            // 終了時に自動起動 OFF なら残骸を消すのに使う（OFF の同期は登録方式によらず全部消す）
            SyncAutostart = enable => StartupManager.SyncAutostartWithSettings(enable),

            SystemEventWindowClassName = "BlueShiftSystemEventWindow_v2",
            TrayWindowClassName = "BlueShiftTrayMessageWindow_v2",
            TrayWindowTitle = "BlueShiftTray",
            TrayIconFileName = "BlueShift.ico",

            GetString = Strings.Get,
            FormatString = Strings.Format
        };
    }
}
