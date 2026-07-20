using System;
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

        private static string AppDataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueShift");

        private static string PidFilePath => Path.Combine(AppDataDirectory, ".instance_pid");

        private static string SignalFilePath => Path.Combine(AppDataDirectory, ".show_signal");

        /// <param name="requestInteractiveShow">
        /// true のとき、既存インスタンスへ「ユーザー操作で GUI を開く」ことを通知する。
        /// --background の二重起動では false（通知しない）。
        /// </param>
        public static bool TryBecomePrimaryInstance(bool requestInteractiveShow)
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                if (requestInteractiveShow)
                    SignalInteractiveShow();

                return false;
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

        /// <summary>既存インスタンスへ終了を依頼（インストーラ用）。</summary>
        public static void SignalExit()
        {
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
            if (!File.Exists(SignalFilePath))
                return false;

            try
            {
                File.Delete(SignalFilePath);
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
                try { _mutex.ReleaseMutex(); } catch { }
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
