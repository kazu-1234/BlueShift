using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace App1
{
  /// <summary>
  /// 自動起動の登録方式（Auto Dark Mode と同様にレジストリ / ログオンタスクを排他使用）。
  /// </summary>
  public enum AutoStartMode
  {
    Registry,
    LogonTask
  }

  /// <summary>
  /// ログオン時の自動起動を管理する。
  /// </summary>
  public static class StartupManager
  {
    private const string RegistryName = "BlueShift";
    private const string LegacyRegistryName = "App1_BlueLightCut";
    private const string LegacyTaskName = "App1_BlueLightCut";
    private const string ObsoleteTaskName = "BlueShift_AutoStart";
    private const string LogonTaskName = "BlueShift Logon";
    private const string BackgroundArg = "--background";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static string TaskFolder =>
      $"BlueShift_{Environment.UserName}";

    private static string TaskFolderPath =>
      Path.Combine(TaskFolder, LogonTaskName);

    /// <summary>現在の自動起動方式を返す（未登録時は preferred を返す）。</summary>
    public static AutoStartMode GetActiveMode(bool preferredUseLogonTask)
    {
      if (IsLogonTaskEnabled())
        return AutoStartMode.LogonTask;

      if (IsRegistryAutoStartEnabled())
        return AutoStartMode.Registry;

      return preferredUseLogonTask ? AutoStartMode.LogonTask : AutoStartMode.Registry;
    }

    /// <summary>自動起動が有効か（いずれかの方式で登録済み）。</summary>
    public static bool IsAutoStartEnabled()
    {
      return IsRegistryAutoStartEnabled() || IsLogonTaskEnabled();
    }

    /// <summary>設定に従って自動起動を適用する。成功時 true。</summary>
    public static bool ApplyAutoStart(bool enable, bool useLogonTask)
    {
      RemoveObsoleteScheduledTasks();

      if (!enable)
        return RemoveAutoStartEntries();

      return useLogonTask ? EnableLogonTaskMode() : EnableRegistryMode();
    }

    /// <summary>登録済みエントリのパスを現在の exe に合わせて検証・修正する。</summary>
    public static void ValidateAutoStart(bool autoStartEnabled, bool useLogonTask)
    {
      if (!autoStartEnabled)
        return;

      string currentCommand = BuildLaunchCommand();

      if (useLogonTask)
      {
        if (!IsLogonTaskCommandValid(currentCommand))
          EnableLogonTaskMode();
        else if (IsRegistryAutoStartEnabled())
          RemoveRegistryAutoStart();

        return;
      }

      if (!IsRegistryCommandValid(currentCommand))
        EnableRegistryMode();
      else if (IsLogonTaskEnabled())
        RemoveLogonTask();
    }

    /// <summary>レジストリ方式で登録されているか。</summary>
    public static bool IsRegistryAutoStartEnabled()
    {
      return GetRegistryCommand() != null;
    }

    /// <summary>ログオンタスク方式で登録されているか。</summary>
    public static bool IsLogonTaskEnabled()
    {
      return GetLogonTaskCommand() != null;
    }

    /// <summary>表示用: 現在の登録コマンド（exe パス等）。</summary>
    public static string? GetRegisteredCommand(bool useLogonTask)
    {
      return useLogonTask ? GetLogonTaskCommand() : GetRegistryCommand();
    }

    /// <summary>旧方式からの移行。</summary>
    public static void MigrateFromLegacyIfNeeded()
    {
      RemoveObsoleteScheduledTasks();

      if (IsLegacyTaskPresent())
      {
        RemoveLegacyTask();
        if (!IsAutoStartEnabled())
          ApplyAutoStart(true, useLogonTask: true);
        return;
      }

      if (IsObsoleteTaskPresent())
      {
        RemoveObsoleteTask();
        if (!IsAutoStartEnabled())
          ApplyAutoStart(true, useLogonTask: true);
        return;
      }

      MigrateFromLegacyRegistryIfNeeded();
    }

    private static bool EnableRegistryMode()
    {
      bool taskRemoved = RemoveLogonTask();
      bool regOk = SetRegistryAutoStart();
      Debug.WriteLine($"EnableRegistryMode: regOk={regOk}, taskRemoved={taskRemoved}");
      return regOk;
    }

    private static bool EnableLogonTaskMode()
    {
      bool taskOk = CreateLogonTask();
      bool regRemoved = RemoveRegistryAutoStart();

      if (taskOk)
      {
        Debug.WriteLine($"EnableLogonTaskMode: taskOk=true, regRemoved={regRemoved}");
        return true;
      }

      // タスク作成失敗時はレジストリ方式へフォールバック（Auto Dark Mode と同様）
      bool regOk = SetRegistryAutoStart();
      Debug.WriteLine($"EnableLogonTaskMode fallback: regOk={regOk}");
      return regOk;
    }

    private static bool RemoveAutoStartEntries()
    {
      bool regOk = RemoveRegistryAutoStart();
      bool taskOk = RemoveLogonTask();
      return regOk && taskOk;
    }

    private static bool SetRegistryAutoStart()
    {
      try
      {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
          ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key == null)
          return false;

        key.SetValue(RegistryName, BuildLaunchCommand());
        return true;
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Failed to set autostart registry: {ex.Message}");
        return false;
      }
    }

    private static bool RemoveRegistryAutoStart()
    {
      try
      {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        if (key == null)
          return true;

        key.DeleteValue(RegistryName, false);
        key.DeleteValue(LegacyRegistryName, false);
        return true;
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Failed to remove autostart registry: {ex.Message}");
        return false;
      }
    }

    private static string? GetRegistryCommand()
    {
      try
      {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(RegistryName) as string;
      }
      catch
      {
        return null;
      }
    }

    private static bool IsRegistryCommandValid(string expectedCommand)
    {
      string? current = GetRegistryCommand();
      return current != null
        && string.Equals(NormalizeCommand(current), NormalizeCommand(expectedCommand), StringComparison.OrdinalIgnoreCase);
    }

    private static bool CreateLogonTask()
    {
      try
      {
        string exePath = GetExecutablePath();
        string? workingDir = Path.GetDirectoryName(exePath);

        using TaskService taskService = new();
        TaskFolder folder = taskService.RootFolder;
        try
        {
          folder = taskService.RootFolder.CreateFolder(TaskFolder, null, false);
        }
        catch
        {
          folder = taskService.GetFolder(TaskFolder);
        }

        TaskDefinition definition = taskService.NewTask();
        definition.RegistrationInfo.Description =
          "BlueShift をログオン時にバックグラウンド起動します。";
        definition.RegistrationInfo.Author = "BlueShift";
        definition.RegistrationInfo.Source = "BlueShift";
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        definition.Settings.AllowHardTerminate = false;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.IdleSettings.StopOnIdleEnd = false;

        definition.Triggers.Add(new LogonTrigger
        {
          UserId = Environment.UserDomainName + @"\" + Environment.UserName
        });
        definition.Actions.Add(new ExecAction(exePath, BackgroundArg, workingDir));

        folder.RegisterTaskDefinition(LogonTaskName, definition);
        return true;
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Failed to create logon task: {ex.Message}");
        return false;
      }
    }

    private static bool RemoveLogonTask()
    {
      try
      {
        using TaskService taskService = new();
        TaskFolder? folder = taskService.GetFolder(TaskFolder);
        if (folder == null)
          return true;

        folder.DeleteTask(LogonTaskName, false);
        return true;
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Failed to remove logon task: {ex.Message}");
        return false;
      }
    }

    private static string? GetLogonTaskCommand()
    {
      try
      {
        using TaskService taskService = new();
        Microsoft.Win32.TaskScheduler.Task? task = taskService.GetTask(TaskFolderPath);
        if (task == null)
          return null;

        ExecAction? action = task.Definition.Actions
          .OfType<ExecAction>()
          .FirstOrDefault();
        if (action == null)
          return null;

        return string.IsNullOrWhiteSpace(action.Arguments)
          ? action.Path
          : $"{action.Path} {action.Arguments}";
      }
      catch
      {
        return null;
      }
    }

    private static bool IsLogonTaskCommandValid(string expectedCommand)
    {
      string? current = GetLogonTaskCommand();
      return current != null
        && string.Equals(NormalizeCommand(current), NormalizeCommand(expectedCommand), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildLaunchCommand()
    {
      return $"\"{GetExecutablePath()}\" {BackgroundArg}";
    }

    private static string GetExecutablePath()
    {
      return Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");
    }

    private static string NormalizeCommand(string command)
    {
      return command.Trim().Replace('/', '\\');
    }

    private static bool IsLegacyTaskPresent()
    {
      return QuerySchtasks($"/Query /TN \"{LegacyTaskName}\"");
    }

    private static bool IsObsoleteTaskPresent()
    {
      return QuerySchtasks($"/Query /TN \"{ObsoleteTaskName}\"");
    }

    private static void RemoveLegacyTask()
    {
      try { RunSchtasks($"/Delete /TN \"{LegacyTaskName}\" /F"); }
      catch (Exception ex) { Debug.WriteLine($"Failed to remove legacy task: {ex.Message}"); }
    }

    private static void RemoveObsoleteTask()
    {
      try { RunSchtasks($"/Delete /TN \"{ObsoleteTaskName}\" /F"); }
      catch { }
    }

    private static void RemoveObsoleteScheduledTasks()
    {
      RemoveObsoleteTask();
    }

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

        key.SetValue(RegistryName, MigrateLegacyCommand(legacyCommand));
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

    private static bool QuerySchtasks(string arguments)
    {
      try
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
        return process?.ExitCode == 0;
      }
      catch
      {
        return false;
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
