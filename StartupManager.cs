using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace App1
{
    /// <summary>
    /// ログオンタスクによる自動起動を管理する（レジストリ Run は使用しない）。
    /// </summary>
    public static class StartupManager
    {
        private const string RegistryName = "BlueShift";
        private const string LegacyRegistryName = "App1_BlueLightCut";
        private const string LogonTaskName = "BlueShift Logon";
        private const string BackgroundArg = "--background";
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        private static string TaskFolder => $"BlueShift_{Environment.UserName}";
        private static string TaskFolderPath => Path.Combine(TaskFolder, LogonTaskName);

        public static bool IsAutoStartEnabled() => GetLogonTaskCommand() != null;

        /// <summary>設定のオン／オフに合わせてタスク・レジストリ・スタートアップショートカットを同期する。</summary>
        public static bool SyncAutostartWithSettings(bool enable)
        {
            if (!enable)
            {
                RemoveAllBlueShiftAutostartTasks();
                RemoveLegacyAutostartArtifacts();
                return true;
            }

            RemoveLegacyAutostartArtifacts();
            bool created = CreateLogonTask();
            RemoveForeignAutostartTasks();
            return created;
        }

        /// <inheritdoc cref="SyncAutostartWithSettings"/>
        public static bool ApplyAutoStart(bool enable) => SyncAutostartWithSettings(enable);

        public static void ValidateAutoStart(bool autoStartEnabled) =>
            SyncAutostartWithSettings(autoStartEnabled);

        public static string? GetRegisteredCommand() => GetLogonTaskCommand();

        /// <summary>旧レジストリ Run / 旧タスクからの移行。</summary>
        public static void MigrateFromLegacyIfNeeded()
        {
            RemoveObsoleteRootTasks();

            bool hadRegistry = IsRegistryAutoStartPresent();
            if (hadRegistry && !IsAutoStartEnabled())
            {
                SyncAutostartWithSettings(true);
                return;
            }

            RemoveLegacyAutostartArtifacts();
        }

        /// <summary>アプリ削除前など、自動起動登録だけ除去する。</summary>
        public static void CleanupAutostartOnly()
        {
            SyncAutostartWithSettings(false);
        }

        private static bool CreateLogonTask()
        {
            try
            {
                string exePath = GetExecutablePath();
                string? workingDir = Path.GetDirectoryName(exePath);

                using TaskService taskService = new();
                TaskFolder folder;
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
                definition.Settings.Priority = ProcessPriorityClass.AboveNormal;
                definition.Settings.AllowDemandStart = true;
                definition.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;

                definition.Triggers.Add(new LogonTrigger
                {
                    UserId = Environment.UserDomainName + @"\" + Environment.UserName,
                    Delay = TimeSpan.Zero
                });
                definition.Actions.Add(new ExecAction(exePath, BackgroundArg, workingDir));

                folder.RegisterTaskDefinition(
                    LogonTaskName,
                    definition,
                    TaskCreation.CreateOrUpdate,
                    null,
                    null,
                    TaskLogonType.InteractiveToken);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create logon task: {ex.Message}");
                return false;
            }
        }

        private static void RemoveAllBlueShiftAutostartTasks()
        {
            try
            {
                using TaskService taskService = new();
                foreach (TaskFolder folder in GetBlueShiftTaskFolders(taskService).ToList())
                {
                    foreach (Microsoft.Win32.TaskScheduler.Task task in folder.GetTasks().ToList())
                    {
                        if (!IsBlueShiftAutostartTask(task))
                            continue;

                        try
                        {
                            folder.DeleteTask(task.Name, false);
                        }
                        catch
                        {
                        }
                    }

                    TryDeleteEmptyFolder(taskService, folder.Name);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to remove logon tasks: {ex.Message}");
            }
        }

        /// <summary>現行 exe 以外の BlueShift 自動起動タスクを削除する。</summary>
        private static void RemoveForeignAutostartTasks()
        {
            try
            {
                string currentExe = Path.GetFullPath(GetExecutablePath());
                string currentFolder = TaskFolder;

                using TaskService taskService = new();
                foreach (TaskFolder folder in GetBlueShiftTaskFolders(taskService).ToList())
                {
                    foreach (Microsoft.Win32.TaskScheduler.Task task in folder.GetTasks().ToList())
                    {
                        if (!IsBlueShiftAutostartTask(task))
                            continue;

                        ExecAction? action = task.Definition.Actions.OfType<ExecAction>().FirstOrDefault();
                        bool isCurrent =
                            string.Equals(folder.Name, currentFolder, StringComparison.Ordinal) &&
                            string.Equals(task.Name, LogonTaskName, StringComparison.Ordinal) &&
                            action != null &&
                            string.Equals(Path.GetFullPath(action.Path), currentExe, StringComparison.OrdinalIgnoreCase);

                        if (isCurrent)
                            continue;

                        try
                        {
                            folder.DeleteTask(task.Name, false);
                        }
                        catch
                        {
                        }
                    }

                    TryDeleteEmptyFolder(taskService, folder.Name);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to remove foreign logon tasks: {ex.Message}");
            }
        }

        private static IEnumerable<TaskFolder> GetBlueShiftTaskFolders(TaskService taskService)
        {
            foreach (TaskFolder folder in taskService.RootFolder.SubFolders)
            {
                if (folder.Name.StartsWith("BlueShift", StringComparison.OrdinalIgnoreCase) ||
                    folder.Name.StartsWith("App1_BlueLightCut", StringComparison.OrdinalIgnoreCase))
                {
                    yield return folder;
                }
            }
        }

        private static bool IsBlueShiftAutostartTask(Microsoft.Win32.TaskScheduler.Task task)
        {
            if (string.Equals(task.Name, LogonTaskName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(task.Name, "BlueShift_AutoStart", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(task.Name, "App1_BlueLightCut", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            ExecAction? action = task.Definition.Actions.OfType<ExecAction>().FirstOrDefault();
            return IsBlueShiftAutostartAction(action);
        }

        private static bool IsBlueShiftAutostartAction(ExecAction? action)
        {
            if (action == null)
                return false;

            string path = action.Path ?? string.Empty;
            string args = action.Arguments ?? string.Empty;
            if (path.Contains("BlueShift", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("App1_BlueLightCut", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return args.Contains("BlueShift", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDeleteEmptyFolder(TaskService taskService, string folderName)
        {
            try
            {
                TaskFolder folder = taskService.GetFolder(folderName);
                if (!folder.GetTasks().Any())
                    taskService.RootFolder.DeleteFolder(folderName, false);
            }
            catch
            {
            }
        }

        private static string? GetLogonTaskCommand()
        {
            try
            {
                using TaskService taskService = new();
                Microsoft.Win32.TaskScheduler.Task? task = taskService.GetTask(TaskFolderPath);
                ExecAction? action = task?.Definition.Actions.OfType<ExecAction>().FirstOrDefault();
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

        private static void RemoveLegacyAutostartArtifacts()
        {
            RemoveRegistryAutoStart();
            CleanStartupShortcuts();
        }

        private static bool IsRegistryAutoStartPresent()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
                if (key == null)
                    return false;

                return key.GetValue(RegistryName) != null || key.GetValue(LegacyRegistryName) != null;
            }
            catch
            {
                return false;
            }
        }

        private static void RemoveRegistryAutoStart()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null)
                    return;

                key.DeleteValue(RegistryName, false);
                key.DeleteValue(LegacyRegistryName, false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to remove autostart registry: {ex.Message}");
            }
        }

        private static void CleanStartupShortcuts()
        {
            try
            {
                string startupPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs\Startup");

                string[] targets =
                [
                    "BlueShift.lnk",
                    "BlueShift - Shortcut.lnk",
                    "App1_BlueLightCut.lnk"
                ];

                foreach (string name in targets)
                {
                    string path = Path.Combine(startupPath, name);
                    if (File.Exists(path))
                        File.Delete(path);
                }

                foreach (string path in Directory.EnumerateFiles(startupPath, "BlueShift_v*.lnk"))
                {
                    try { File.Delete(path); } catch { }
                }
            }
            catch
            {
            }
        }

        private static void RemoveObsoleteRootTasks()
        {
            try
            {
                using TaskService taskService = new();
                foreach (string name in new[] { "BlueShift_AutoStart", "App1_BlueLightCut" })
                {
                    try
                    {
                        taskService.RootFolder.DeleteTask(name, false);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static string GetExecutablePath()
        {
            return Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");
        }
    }
}
