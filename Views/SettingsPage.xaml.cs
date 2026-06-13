using App1;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.System;

namespace App1.Views
{
    public sealed partial class SettingsPage : Page
    {
        private AppState? _state;
        private bool _isInitializing;

        public SettingsPage()
        {
            InitializeComponent();
        }

        private void UpdateAutoStartToggleLabel()
        {
            AutoStartToggleLabel.Text = AutoStartToggle.IsOn
                ? Strings.Get("Toggle_On")
                : Strings.Get("Toggle_Off");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _state = e.Parameter as AppState;
            if (_state == null) return;

            _isInitializing = true;
            AutoStartToggle.IsOn = _state.Settings.AutoStart;
            UpdateAutoStartToggleLabel();
            VersionText.Text = Strings.Format("Version_Format", UpdateChecker.CurrentVersion);
            _isInitializing = false;
        }

        private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateAutoStartToggleLabel();
            if (_isInitializing || _state == null) return;

            StartupManager.SetAutoStart(AutoStartToggle.IsOn);
            _state.Settings.AutoStart = AutoStartToggle.IsOn;
            _state.Settings.Save();
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            UpdateInfoBar.IsOpen = false;

            var result = await UpdateChecker.CheckForUpdateAsync();

            UpdateInfoBar.Message = result.Message;
            UpdateInfoBar.Severity = result.Status switch
            {
                UpdateCheckStatus.UpdateAvailable => InfoBarSeverity.Warning,
                UpdateCheckStatus.UpToDate => InfoBarSeverity.Success,
                UpdateCheckStatus.NotConfigured => InfoBarSeverity.Informational,
                _ => InfoBarSeverity.Error
            };
            UpdateInfoBar.IsOpen = true;

            if (result.Status == UpdateCheckStatus.UpdateAvailable
                && !string.IsNullOrWhiteSpace(result.ReleasePageUrl))
            {
                var dialog = new ContentDialog
                {
                    Title = Strings.Get("Update_DialogTitle"),
                    Content = Strings.Format("Update_DialogContent", result.Message),
                    PrimaryButtonText = Strings.Get("Update_DialogOpen"),
                    CloseButtonText = Strings.Get("Update_DialogClose"),
                    XamlRoot = Content.XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    await Launcher.LaunchUriAsync(new Uri(result.ReleasePageUrl));
            }

            CheckUpdateButton.IsEnabled = true;
        }
    }
}
