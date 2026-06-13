// v1.0.13

using Microsoft.UI.Xaml;
using System;

namespace App1
{
    public partial class App : Application
    {
        private Window? m_window;
        private static bool _gammaResetRegistered;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            UpdateChecker.LatestReleaseApiUrl =
                "https://api.github.com/repos/kazu-1234/BlueShift/releases/latest";

            bool launchInBackground = HasCommandLineArg("--background");
            bool requestInteractiveShow = !launchInBackground;

            if (!SingleInstanceManager.TryBecomePrimaryInstance(requestInteractiveShow))
            {
                Exit();
                return;
            }

            RegisterGammaResetOnExit();

            m_window = new MainWindow(
                launchInBackground,
                requestInteractiveShow,
                SingleInstanceManager.InteractiveShowEvent);
            m_window.Activate();
        }

        private static bool HasCommandLineArg(string arg)
        {
            foreach (string item in Environment.GetCommandLineArgs())
            {
                if (string.Equals(item, arg, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void RegisterGammaResetOnExit()
        {
            if (_gammaResetRegistered)
                return;

            _gammaResetRegistered = true;

            AppDomain.CurrentDomain.ProcessExit += (_, _) => GammaController.ResetGamma();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => GammaController.ResetGamma();
        }
    }
}
