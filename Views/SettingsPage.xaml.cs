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

            ThemeComboBox.Items.Clear();
            ThemeComboBox.Items.Add(Strings.Get("Settings_Theme_System"));
            ThemeComboBox.Items.Add(Strings.Get("Settings_Theme_Light"));
            ThemeComboBox.Items.Add(Strings.Get("Settings_Theme_Dark"));
            ThemeComboBox.SelectedIndex = _state.Settings.ThemePreference switch
            {
                AppThemePreference.Light => 1,
                AppThemePreference.Dark => 2,
                _ => 0
            };

            AutoStartToggle.IsOn = _state.Settings.AutoStart;
            VersionText.Text = Strings.Format("Version_Format", UpdateChecker.CurrentVersion);
            UpdateAutoStartDetails();
            _isInitializing = false;
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _state == null || ThemeComboBox.SelectedIndex < 0)
                return;

            var preference = ThemeComboBox.SelectedIndex switch
            {
                1 => AppThemePreference.Light,
                2 => AppThemePreference.Dark,
                _ => AppThemePreference.System
            };

            if (preference == _state.Settings.ThemePreference)
                return;

            ThemeService.SetPreference(preference);
            _state.Settings.ThemePreference = preference;
            _state.Settings.Save();
        }

        private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null) return;

            bool requested = AutoStartToggle.IsOn;
            if (!StartupManager.SyncAutostartWithSettings(requested) && requested)
            {
                _isInitializing = true;
                AutoStartToggle.IsOn = false;
                _isInitializing = false;
                return;
            }

            _state.Settings.AutoStart = requested;
            _state.Settings.Save();
            UpdateAutoStartDetails();
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

            AutostartModeText.Text = Strings.Get("Settings_AutostartMode_Task");

            string? command = StartupManager.GetRegisteredCommand();
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
