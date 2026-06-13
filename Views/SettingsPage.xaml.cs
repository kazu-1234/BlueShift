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
            AutoStartToggle.OnContent = Strings.Get("Toggle_On");
            AutoStartToggle.OffContent = Strings.Get("Toggle_Off");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _state = e.Parameter as AppState;
            if (_state == null) return;

            _isInitializing = true;
            AutoStartToggle.IsOn = _state.Settings.AutoStart;
            UseLogonTaskCheckBox.IsChecked = _state.Settings.UseLogonTask;
            VersionText.Text = Strings.Format("Version_Format", UpdateChecker.CurrentVersion);
            UpdateAutoStartDetails();
            UpdateLogonTaskCheckBoxEnabled();
            _isInitializing = false;
        }

        private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null) return;

            bool requested = AutoStartToggle.IsOn;
            if (!StartupManager.ApplyAutoStart(requested, _state.Settings.UseLogonTask))
            {
                _isInitializing = true;
                AutoStartToggle.IsOn = !requested;
                _isInitializing = false;
                return;
            }

            _state.Settings.AutoStart = requested;
            _state.Settings.Save();
            UpdateAutoStartDetails();
            UpdateLogonTaskCheckBoxEnabled();
        }

        private void UseLogonTaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null) return;

            bool useLogonTask = UseLogonTaskCheckBox.IsChecked == true;
            _state.Settings.UseLogonTask = useLogonTask;
            _state.Settings.Save();

            if (!_state.Settings.AutoStart)
                return;

            if (!StartupManager.ApplyAutoStart(true, useLogonTask))
            {
                _isInitializing = true;
                UseLogonTaskCheckBox.IsChecked = !useLogonTask;
                _state.Settings.UseLogonTask = UseLogonTaskCheckBox.IsChecked == true;
                _state.Settings.Save();
                _isInitializing = false;
                return;
            }

            UpdateAutoStartDetails();
        }

        private void UpdateLogonTaskCheckBoxEnabled()
        {
            UseLogonTaskCheckBox.IsEnabled = AutoStartToggle.IsOn;
        }

        private void UpdateAutoStartDetails()
        {
            if (_state == null)
                return;

            if (!_state.Settings.AutoStart)
            {
                AutostartModeText.Text = Strings.Get("Settings_AutostartMode_Disabled");
                AutostartPathText.Text = Strings.Get("NotAvailable");
                return;
            }

            bool useLogonTask = _state.Settings.UseLogonTask;
            AutostartModeText.Text = useLogonTask
                ? Strings.Get("Settings_AutostartMode_Task")
                : Strings.Get("Settings_AutostartMode_Registry");

            string? command = StartupManager.GetRegisteredCommand(useLogonTask);
            AutostartPathText.Text = string.IsNullOrWhiteSpace(command)
                ? Strings.Get("NotAvailable")
                : command.Replace("\"", string.Empty);
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
