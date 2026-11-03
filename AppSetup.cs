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

        /// <summary>
        /// 自動起動の登録名（タスク名・Run の名前・ショートカット名。公開後は変えない）。
        /// Extra* は旧名（App1_BlueLightCut など）の残骸を見つけて消すための名前。
        /// </summary>
        public static readonly AutostartController Autostart = new(
            new AutostartIdentity
            {
                AppName = AppName,
                LogonTaskName = AppName + " Logon",
                RegistryName = AppName,
                AllowRegistryRun = true,
                ExtraRegistryNames = ["App1_BlueLightCut"],
                ExtraTaskFolderPrefixes = ["App1_BlueLightCut"],
                ExtraTaskNames = ["BlueShift_AutoStart", "App1_BlueLightCut"],
                ExtraPathTokens = ["App1_BlueLightCut"],
                StartupShortcutNames =
                [
                    "BlueShift.lnk",
                    "BlueShift - Shortcut.lnk",
                    "App1_BlueLightCut.lnk"
                ],
                StartupShortcutGlobs = ["BlueShift_v*.lnk"]
            },
            // 旧版がタスクスケジューラの直下に作っていたタスク（起動時の移行で消す）
            rootTasksToRemove: ["BlueShift_AutoStart", "App1_BlueLightCut"]);

        public static readonly AppProfile Profile = new()
        {
            AppName = AppName,
            // 旧版と同じ Mutex / イベント名（Global\BlueShift_SingleInstance_v1）にして、インストーラの --exit を届かせる
            InstanceNameVersion = "v1",
            LatestReleaseApiUrl = "https://api.github.com/repos/kazu-1234/BlueShift/releases/latest",

            CloseBehavior = WindowCloseBehavior.KeepRunning,
            UseTray = true,
            UseResumeMonitor = true,
            // 自動起動の窓口。--sync-autostart / --cleanup-autostart、起動時の移行と同期、終了時の掃除を共通部が行う
            Autostart = Autostart,
            LoadSettings = Settings.Load,

            SystemEventWindowClassName = "BlueShiftSystemEventWindow_v2",
            TrayWindowClassName = "BlueShiftTrayMessageWindow_v2",
            TrayWindowTitle = "BlueShiftTray",
            TrayIconFileName = "BlueShift.ico",

            GetString = Strings.Get,
            FormatString = Strings.Format
        };
    }
}
