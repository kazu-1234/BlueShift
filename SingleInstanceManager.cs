using System;
using System.Threading;

namespace App1
{
    /// <summary>
    /// 二重起動時に既存インスタンスへ GUI 表示を依頼する。
    /// </summary>
    internal static class SingleInstanceManager
    {
        private const string MutexName = "Global\\BlueShift_SingleInstance_v1";
        private const string InteractiveShowEventName = "Global\\BlueShift_ShowInteractive_v1";

        private static Mutex? _mutex;
        private static EventWaitHandle? _interactiveShowEvent;

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

            return true;
        }

        public static EventWaitHandle? InteractiveShowEvent => _interactiveShowEvent;

        private static void SignalInteractiveShow()
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

        public static void Release()
        {
            _interactiveShowEvent?.Dispose();
            _interactiveShowEvent = null;

            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
