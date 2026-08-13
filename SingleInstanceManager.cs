using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace App1
{
    /// <summary>
    /// 二重起動時に既存インスタンスへ GUI 表示／終了を依頼する。
    /// </summary>
    internal static class SingleInstanceManager
    {
#if DEBUG
        private const string MutexName = "Global\\BlueShift_SingleInstance_v1_DEBUG";
        private const string InteractiveShowEventName = "Global\\BlueShift_ShowInteractive_v1_DEBUG";
        private const string ExitEventName = "Global\\BlueShift_Exit_v1_DEBUG";
#else
        private const string MutexName = "Global\\BlueShift_SingleInstance_v1";
        private const string InteractiveShowEventName = "Global\\BlueShift_ShowInteractive_v1";
        private const string ExitEventName = "Global\\BlueShift_Exit_v1";
#endif

        private static Mutex? _mutex;
        private static EventWaitHandle? _interactiveShowEvent;
        private static EventWaitHandle? _exitEvent;
        private static bool _ownsMutex;

        private static string AppDataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueShift");

        private static string PidFilePath => Path.Combine(AppDataDirectory, ".instance_pid");

        private static string SignalFilePath => Path.Combine(AppDataDirectory, ".show_signal");

        private static string ExitSignalFilePath => Path.Combine(AppDataDirectory, ".exit_signal");

        /// <param name="requestInteractiveShow">
        /// true のとき、既存インスタンスへ「ユーザー操作で GUI を開く」ことを通知する。
        /// --background の二重起動では false（通知しない）。
        /// </param>
        public static bool TryBecomePrimaryInstance(bool requestInteractiveShow)
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                if (TryTakeOverAbandonedOrDeadPrimary())
                {
                    createdNew = true;
                }
                else
                {
                    if (requestInteractiveShow)
                        SignalInteractiveShow();

                    _mutex.Dispose();
                    _mutex = null;
                    return false;
                }
            }
            else
            {
                _ownsMutex = true;
            }

            _interactiveShowEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                InteractiveShowEventName);
            _exitEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                ExitEventName);

            TryWritePidFile();
            return true;
        }

        /// <summary>
        /// 放棄 Mutex、または PID ファイルのプロセスが死んでいる場合に primary を奪取する。
        /// </summary>
        private static bool TryTakeOverAbandonedOrDeadPrimary()
        {
            if (_mutex == null)
                return false;

            // 生きている primary があれば奪取しない
            if (IsPrimaryProcessAlive())
                return false;

            try
            {
                if (_mutex.WaitOne(0))
                {
                    _ownsMutex = true;
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                // 前オーナーが異常終了 → こちらが所有権を得た
                _ownsMutex = true;
                return true;
            }

            // WaitOne(0) 失敗でも PID が死んでいれば短時間待って Abandoned を拾う
            try
            {
                if (_mutex.WaitOne(TimeSpan.FromMilliseconds(200)))
                {
                    _ownsMutex = true;
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                _ownsMutex = true;
                return true;
            }

            return false;
        }

        private static bool IsPrimaryProcessAlive()
        {
            try
            {
                if (!File.Exists(PidFilePath))
                    return false;

                if (!int.TryParse(File.ReadAllText(PidFilePath).Trim(), out int pid))
                    return false;

                if (pid == Environment.ProcessId)
                    return false;

                using Process process = Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                // プロセスが存在しない
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch
            {
                return true;
            }
        }

        public static EventWaitHandle? InteractiveShowEvent => _interactiveShowEvent;
        public static EventWaitHandle? ExitEvent => _exitEvent;

        public static void SignalInteractiveShow()
        {
            TryAllowForegroundForPrimary();

            bool signaled = false;
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(InteractiveShowEventName);
                showEvent.Set();
                signaled = true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }

            // Event 失敗時のフォールバック（成功時は二重表示を避ける）
            if (signaled)
                return;

            try
            {
                Directory.CreateDirectory(AppDataDirectory);
                File.WriteAllText(SignalFilePath, DateTime.UtcNow.ToString("O"));
            }
            catch
            {
            }
        }

        /// <summary>既存インスタンスへ終了を依頼（インストーラ用）。ファイルが本体、イベントは起床用。</summary>
        public static void SignalExit()
        {
            try
            {
                Directory.CreateDirectory(AppDataDirectory);
                File.WriteAllText(ExitSignalFilePath, DateTime.UtcNow.ToString("O"));
            }
            catch
            {
            }

            try
            {
                using var exitEvent = EventWaitHandle.OpenExisting(ExitEventName);
                exitEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
        }

        /// <summary>ファイル信号があれば消費して true。</summary>
        public static bool TryConsumeShowSignal()
        {
            return TryConsumeFile(SignalFilePath);
        }

        /// <summary>終了握手ファイルがあれば消費して true。イベントだけの起床は無視する。</summary>
        public static bool TryConsumeExitSignal()
        {
            return TryConsumeFile(ExitSignalFilePath);
        }

        private static bool TryConsumeFile(string path)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Release()
        {
            TryDeletePidFile();

            _interactiveShowEvent?.Dispose();
            _interactiveShowEvent = null;
            _exitEvent?.Dispose();
            _exitEvent = null;

            if (_mutex != null)
            {
                if (_ownsMutex)
                {
                    try { _mutex.ReleaseMutex(); } catch { }
                    _ownsMutex = false;
                }
                _mutex.Dispose();
                _mutex = null;
            }
        }

        private static void TryWritePidFile()
        {
            try
            {
                Directory.CreateDirectory(AppDataDirectory);
                File.WriteAllText(PidFilePath, Environment.ProcessId.ToString());
            }
            catch
            {
            }
        }

        private static void TryDeletePidFile()
        {
            try
            {
                if (File.Exists(PidFilePath))
                    File.Delete(PidFilePath);
            }
            catch
            {
            }
        }

        private static void TryAllowForegroundForPrimary()
        {
            try
            {
                if (!File.Exists(PidFilePath))
                    return;

                if (!int.TryParse(File.ReadAllText(PidFilePath).Trim(), out int pid))
                    return;

                AllowSetForegroundWindow(pid);
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);
    }
}
