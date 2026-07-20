using App1;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

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
            HideTrayIconCheckBox.IsChecked = _state.Settings.HideTrayIcon;
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

        private void HideTrayIconCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null)
                return;

            bool hide = HideTrayIconCheckBox.IsChecked == true;
            if (hide == _state.Settings.HideTrayIcon)
                return;

            _state.Settings.HideTrayIcon = hide;
            _state.Settings.Save();
            _state.ApplyTrayIconVisibility?.Invoke();
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
    }
}
