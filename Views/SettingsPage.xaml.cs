using App1;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinUiShared;

namespace App1.Views
{
    public sealed partial class SettingsPage : Page
    {
        private AppState? _state;
        private bool _isInitializing;

        public SettingsPage()
        {
            InitializeComponent();
            CompactComboBoxHelper.AttachFitToSelectedText(ThemeComboBox);
            AutoStartToggle.OnContent = Strings.Get("Toggle_On");
            AutoStartToggle.OffContent = Strings.Get("Toggle_Off");
            ToggleSwitchClickHelper.ProtectFromParentCapture(AutoStartToggle);
            AutostartExpandHelper.AttachSkipInitialAnimation(AutoStartExpander);
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
            AutostartTaskOnlyCheckBox.IsChecked = _state.Settings.UseLogonTask;
            HideTrayIconCheckBox.IsChecked = _state.Settings.HideTrayIcon;
            AutoStartExpander.IsExpanded = true;
            RefreshAutostartInfo();
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

            ThemeService.SetPreference(preference, save: false);
            _state.Settings.ThemePreference = preference;
            _state.Settings.Save();
        }

        private void RefreshAutostartInfo()
        {
            bool enabled = _state?.Settings.AutoStart ?? false;
            bool useLogonTask = _state?.Settings.UseLogonTask ?? true;

            AutostartTypeLine.Text = AutostartInfoFormatter.FormatTypeLine(
                enabled,
                useLogonTask,
                Strings.Get,
                (key, args) => Strings.Format(key, args));

            AutostartPathLine.Text = AutostartInfoFormatter.FormatPathLine(
                enabled,
                StartupManager.GetRegisteredCommand(preferLogonTask: useLogonTask),
                Strings.Get,
                (key, args) => Strings.Format(key, args));
        }

        private void RefreshAutostartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null)
                return;

            if (_state.Settings.AutoStart)
                StartupManager.SyncAutostartWithSettings(true, _state.Settings.UseLogonTask);

            RefreshAutostartInfo();
        }

        private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null) return;

            bool requested = AutoStartToggle.IsOn;
            bool useLogonTask = _state.Settings.UseLogonTask;
            if (!StartupManager.SyncAutostartWithSettings(requested, useLogonTask) && requested)
            {
                _isInitializing = true;
                AutoStartToggle.IsOn = false;
                _isInitializing = false;
                return;
            }

            _state.Settings.AutoStart = requested;
            _state.Settings.Save();
            RefreshAutostartInfo();
        }

        private void AutostartTaskOnlyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null)
                return;

            bool useLogonTask = AutostartTaskOnlyCheckBox.IsChecked == true;
            if (useLogonTask == _state.Settings.UseLogonTask)
                return;

            _state.Settings.UseLogonTask = useLogonTask;

            if (_state.Settings.AutoStart)
            {
                bool ok = StartupManager.SyncAutostartWithSettings(true, useLogonTask);
                if (!ok)
                {
                    _isInitializing = true;
                    _state.Settings.UseLogonTask = !useLogonTask;
                    AutostartTaskOnlyCheckBox.IsChecked = !useLogonTask;
                    _isInitializing = false;
                    return;
                }
            }

            _state.Settings.Save();
            RefreshAutostartInfo();
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
    }
}
