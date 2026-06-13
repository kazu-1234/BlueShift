using App1;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace App1.Views
{
    public sealed partial class InfoPage : Page
    {
        private AppState? _state;

        public InfoPage()
        {
            InitializeComponent();
            VersionCaptionText.Text = Strings.Format("Version_Format", UpdateChecker.CurrentVersion);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _state = e.Parameter as AppState;
            if (_state == null) return;

            _state.PropertyChanged -= State_PropertyChanged;
            _state.PropertyChanged += State_PropertyChanged;
            RefreshDisplay();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_state != null)
                _state.PropertyChanged -= State_PropertyChanged;
            base.OnNavigatedFrom(e);
        }

        private void State_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (_state == null) return;

            FilterStatusText.Text = _state.IsFilterEnabled
                ? Strings.Get("Status_Enabled")
                : Strings.Get("Status_Disabled");
            IntensityStatusText.Text = _state.CurrentIntensityText;
            ScheduleStatusText.Text = _state.ActiveScheduleText;
            NextTransitionStatusText.Text = _state.NextTransitionText;

            DetailInfoBar.Message = _state.StatusMessage;
            DetailInfoBar.Severity = _state.StatusSeverity;
            DetailInfoBar.IsOpen = !string.IsNullOrEmpty(_state.StatusMessage);
        }
    }
}
