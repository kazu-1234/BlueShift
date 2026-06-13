using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace App1
{
  /// <summary>
  /// スタートアップ登録（レジストリ）の代わりに、ログオン時タスクで起動する。
  /// </summary>
  public static class StartupManager
  {
    private const string TaskName = "BlueShift_AutoStart";
    private const string LegacyTaskName = "App1_BlueLightCut";
    private const string LegacyRegistryName = "App1_BlueLightCut";
    private const string BackgroundArg = "--background";

    public static void SetAutoStart(bool enable)
    {
      RemoveLegacyRegistryAutoStart();

      try
      {
        if (enable)
        {
          string exePath = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");

          string taskAction = $"\"{exePath}\" {BackgroundArg}";
          RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"{taskAction}\" /SC ONLOGON /RL LIMITED /F");
        }
        else
        {
          RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Failed to set logon task: {ex.Message}");
      }
    }

    public static bool IsAutoStartEnabled()
    {
      try
      {
        using var process = Process.Start(new ProcessStartInfo
        {
          FileName = "schtasks.exe",
          Arguments = $"/Query /TN \"{TaskName}\"",
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

    /// <summary>旧タスク名・レジストリ方式からの移行用。</summary>
    public static void MigrateFromLegacyIfNeeded()
    {
      if (IsLegacyTaskPresent())
      {
        RemoveLegacyTask();
        if (!IsAutoStartEnabled())
          SetAutoStart(true);
        return;
      }

      MigrateFromRegistryIfNeeded();
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

    /// <summary>旧レジストリ方式からの移行用。</summary>
    private static void MigrateFromRegistryIfNeeded()
    {
      if (!IsLegacyRegistryAutoStartEnabled()) return;

      RemoveLegacyRegistryAutoStart();
      if (!IsAutoStartEnabled())
        SetAutoStart(true);
    }

    private static bool IsLegacyRegistryAutoStartEnabled()
    {
      try
      {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
          @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
        return key?.GetValue(LegacyRegistryName) != null;
      }
      catch
      {
        return false;
      }
    }

    private static void RemoveLegacyRegistryAutoStart()
    {
      try
      {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
          @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        key?.DeleteValue(LegacyRegistryName, false);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Failed to remove legacy registry autostart: {ex.Message}");
      }
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
