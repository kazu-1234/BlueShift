using System;
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

            return true;
        }

        public static EventWaitHandle? InteractiveShowEvent => _interactiveShowEvent;
        public static EventWaitHandle? ExitEvent => _exitEvent;

        public static void SignalInteractiveShow()
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(InteractiveShowEventName);
                showEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
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

        public static void Release()
        {
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
    }
}
