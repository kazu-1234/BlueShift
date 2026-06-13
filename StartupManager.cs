using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace App1
{
  /// <summary>
  /// ログオン時の自動起動を HKCU Run レジストリで登録する。
  /// </summary>
  public static class StartupManager
  {
    private const string RegistryName = "BlueShift";
    private const string LegacyRegistryName = "App1_BlueLightCut";
    private const string LegacyTaskName = "App1_BlueLightCut";
    private const string ObsoleteTaskName = "BlueShift_AutoStart";
    private const string BackgroundArg = "--background";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>自動起動を有効/無効にする。成功時 true。</summary>
    public static bool SetAutoStart(bool enable)
    {
      RemoveObsoleteScheduledTasks();

      try
      {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key == null)
          return false;

        if (enable)
        {
          string exePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");

          string command = $"\"{exePath}\" {BackgroundArg}";
          key.SetValue(RegistryName, command);
        }
        else
        {
          key.DeleteValue(RegistryName, false);
          key.DeleteValue(LegacyRegistryName, false);
        }

        return true;
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Failed to set autostart registry: {ex.Message}");
        return false;
      }
    }

    public static bool IsAutoStartEnabled()
    {
      try
      {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        if (key == null)
          return false;

        return key.GetValue(RegistryName) != null;
      }
      catch
      {
        return false;
      }
    }

    /// <summary>旧タスク名・旧レジストリ名からの移行用。</summary>
    public static void MigrateFromLegacyIfNeeded()
    {
      RemoveObsoleteScheduledTasks();

      if (IsLegacyTaskPresent())
      {
        RemoveLegacyTask();
        if (!IsAutoStartEnabled())
          SetAutoStart(true);
        return;
      }

      MigrateFromLegacyRegistryIfNeeded();
    }

    private static bool IsLegacyTaskPresent()
    {
      try
      {
        using var process = Process.Start(new ProcessStartInfo
        {
          FileName = "schtasks.exe",
          Arguments = $"/Query /TN \"{LegacyTaskName}\"",
          CreateNoWindow = true,
          UseShellExecute = false,
          RedirectStandardOutput = true,
          RedirectStandardError = true
        });

        process?.WaitForExit();
        return process?.ExitCode == 0;
      }
      catch
      {
        return false;
      }
    }

    private static void RemoveLegacyTask()
    {
      try
      {
        RunSchtasks($"/Delete /TN \"{LegacyTaskName}\" /F");
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Failed to remove legacy logon task: {ex.Message}");
      }
    }

    private static void RemoveObsoleteScheduledTasks()
    {
      try
      {
        RunSchtasks($"/Delete /TN \"{ObsoleteTaskName}\" /F");
      }
      catch
      {
      }
    }

    /// <summary>旧レジストリ名 App1_BlueLightCut から BlueShift へ移行する。</summary>
    private static void MigrateFromLegacyRegistryIfNeeded()
    {
      try
      {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key == null)
          return;

        if (key.GetValue(RegistryName) != null)
        {
          key.DeleteValue(LegacyRegistryName, false);
          return;
        }

        if (key.GetValue(LegacyRegistryName) is not string legacyCommand)
          return;

        string migrated = MigrateLegacyCommand(legacyCommand);
        key.SetValue(RegistryName, migrated);
        key.DeleteValue(LegacyRegistryName, false);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Failed to migrate legacy autostart registry: {ex.Message}");
      }
    }

    private static string MigrateLegacyCommand(string legacyCommand)
    {
      string trimmed = legacyCommand.Trim();
      if (trimmed.Contains(BackgroundArg, StringComparison.OrdinalIgnoreCase))
        return trimmed;

      if (trimmed.Length >= 2 && trimmed.StartsWith('"'))
      {
        int closingQuote = trimmed.IndexOf('"', 1);
        if (closingQuote > 0)
        {
          string exePart = trimmed[..(closingQuote + 1)];
          string argsPart = trimmed[(closingQuote + 1)..].TrimStart();
          return string.IsNullOrEmpty(argsPart)
            ? $"{exePart} {BackgroundArg}"
            : $"{exePart} {argsPart} {BackgroundArg}";
        }
      }

      return $"{trimmed} {BackgroundArg}";
    }

    private static void RunSchtasks(string arguments)
    {
      using var process = Process.Start(new ProcessStartInfo
      {
        FileName = "schtasks.exe",
        Arguments = arguments,
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
      });

      process?.WaitForExit();
      if (process?.ExitCode != 0)
      {
        string err = process?.StandardError.ReadToEnd() ?? string.Empty;
        throw new InvalidOperationException($"schtasks failed ({process?.ExitCode}): {err}");
      }
    }
  }
}
