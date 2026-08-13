using WinUiShared;

namespace App1;

/// <summary>自動起動のアプリ側入口。実体は WinUiShared.AutostartService（SPM 正本）。</summary>
public static class StartupManager
{
    private static readonly AutostartIdentity Identity = new()
    {
        AppName = "BlueShift",
        LogonTaskName = "BlueShift Logon",
        RegistryName = "BlueShift",
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
    };

    public static bool IsAutoStartEnabled() => AutostartService.IsEnabled(Identity);

    public static bool SyncAutostartWithSettings(bool enable, bool useLogonTask = true) =>
        AutostartService.Sync(Identity, enable, useLogonTask);

    public static bool ApplyAutoStart(bool enable, bool useLogonTask = true) =>
        SyncAutostartWithSettings(enable, useLogonTask);

    public static void ValidateAutoStart(bool autoStartEnabled, bool useLogonTask = true) =>
        SyncAutostartWithSettings(autoStartEnabled, useLogonTask);

    public static string? GetRegisteredCommand(bool preferLogonTask = true) =>
        AutostartService.GetRegisteredCommand(Identity, preferLogonTask);

    public static void MigrateFromLegacyIfNeeded()
    {
        AutostartService.RemoveRootTasks("BlueShift_AutoStart", "App1_BlueLightCut");

        bool hadRegistry = AutostartService.IsRegistryPresent(Identity);
        if (hadRegistry && !IsAutoStartEnabled())
        {
            SyncAutostartWithSettings(true, useLogonTask: true);
            return;
        }

        AutostartService.CleanLegacyArtifactsKeepCurrentRun(Identity);
    }

    public static void CleanupAutostartOnly() => SyncAutostartWithSettings(false);
}
