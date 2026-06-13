using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Threading;

namespace App1
{
    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // WinUI を起動せず、画面のガンマだけ即座に戻す。
            if (HasArg(args, "--reset-gamma"))
            {
                GammaController.ResetGamma();
                return;
            }

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }

        private static bool HasArg(string[] args, string arg)
        {
            foreach (string item in args)
            {
                if (string.Equals(item, arg, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
