using App1;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.System;

namespace App1.Views
{
    public sealed partial class InfoPage : Page
    {
        private AppState? _state;

        public InfoPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _state = e.Parameter as AppState;
            if (_state == null) return;

            VersionText.Text = Strings.Format("Version_Format", UpdateChecker.CurrentVersion);

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
            ColorTemperatureStatusText.Text = _state.CurrentColorTemperatureText;
            ScheduleStatusText.Text = _state.ActiveScheduleText;
            NextTransitionStatusText.Text = _state.NextTransitionText;

            DetailInfoBar.Message = _state.StatusMessage;
            DetailInfoBar.Severity = _state.StatusSeverity;
            DetailInfoBar.IsOpen = !string.IsNullOrEmpty(_state.StatusMessage);
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
