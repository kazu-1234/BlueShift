using App1;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinUiShared;

namespace App1.Views
{
    /// <summary>
    /// 設定ページ。テーマ選択と自動起動欄の処理は共通部（ThemeComboBoxBinder / AutostartSettingsController）が行う。
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private AppState? _state;
        private readonly ThemeComboBoxBinder _theme;
        private readonly AutostartSettingsController _autostart;

        public SettingsPage()
        {
            InitializeComponent();
            _theme = new ThemeComboBoxBinder(
                ThemeComboBox,
                get: () => _state?.Settings.ThemePreference ?? AppThemePreference.System,
                set: value => { if (_state != null) _state.Settings.ThemePreference = value; },
                save: () => _state?.Settings.Save());

            _autostart = new AutostartSettingsController(new AutostartSettingsControls
            {
                AutoStartToggle = AutoStartToggle,
                Expander = AutoStartExpander,
                UseLogonTaskCheckBox = AutostartTaskOnlyCheckBox,
                TypeLine = AutostartTypeLine,
                PathLine = AutostartPathLine,
                RefreshButton = RefreshAutostartButton,
                HideTrayIconCheckBox = HideTrayIconCheckBox,
                ShowStartupNotificationCheckBox = ShowStartupNotificationCheckBox
            }, AppSetup.Autostart);
            AutoStartToggle.OnContent = Strings.Get("Toggle_On");
            AutoStartToggle.OffContent = Strings.Get("Toggle_Off");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _state = e.Parameter as AppState;
            if (_state == null) return;

            _theme.Load();
            _autostart.Bind(AutostartSettingsAccess.For(_state.Settings, () => _state.ApplyTrayIconVisibility?.Invoke()));
        }
    }
}
