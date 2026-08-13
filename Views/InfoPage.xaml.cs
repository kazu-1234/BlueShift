using App1;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Windows.System;
using WinUiShared;

namespace App1.Views
{
    public sealed partial class InfoPage : Page
    {
        private AppState? _state;
        private UpdateCheckResult? _lastResult;
        private bool _isInitializing;

        public InfoPage()
        {
            InitializeComponent();
            ToggleSwitchClickHelper.BindCardClick(AutoUpdateToggleCard, AutoUpdateToggle);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _state = e.Parameter as AppState;
            if (_state == null) return;

            _isInitializing = true;
            VersionText.Text = Strings.Format("Version_Format", UpdateChecker.CurrentVersion);
            RefreshLastUpdateCheckText();
            AutoUpdateToggle.IsOn = _state.Settings.AutoCheckUpdateOnStartup;

            _state.PropertyChanged -= State_PropertyChanged;
            _state.PropertyChanged += State_PropertyChanged;
            RefreshDisplay();
            _isInitializing = false;
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

        private void RefreshLastUpdateCheckText()
        {
            LastUpdateCheckText.Text = UpdateFlowService.FormatLastUpdateCheckDisplay(
                _state?.Settings.LastUpdateCheckUtc);
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
            if (_state == null)
                return;

            CheckUpdateButton.IsEnabled = false;
            UpdateInfoBar.IsOpen = false;
            InstallUpdateCard.Visibility = Visibility.Collapsed;
            _lastResult = null;

            UpdateCheckResult result = await UpdateFlowService.CheckAndRecordAsync(_state.Settings);
            _lastResult = result;
            RefreshLastUpdateCheckText();

            InfoUpdateUi.ApplyManualCheckResult(
                result,
                UpdateInfoBar,
                InstallUpdateCard,
                InstallStatusText,
                InstallUpdateButton,
                (key, args) => Strings.Format(key, args));

            CheckUpdateButton.IsEnabled = true;

            if (result.Status == UpdateCheckStatus.UpdateAvailable
                && (string.IsNullOrWhiteSpace(result.DownloadUrl) || string.IsNullOrWhiteSpace(result.AssetFileName))
                && !string.IsNullOrWhiteSpace(result.ReleasePageUrl))
            {
                var dialog = new ContentDialog
                {
                    Title = Strings.Get("Update_AvailableTitle"),
                    Content = result.Message,
                    PrimaryButtonText = Strings.Get("Update_OpenRelease"),
                    CloseButtonText = Strings.Get("Common_Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                    await Launcher.LaunchUriAsync(new Uri(result.ReleasePageUrl));
            }
        }

        private void AutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null)
                return;

            _state.Settings.AutoCheckUpdateOnStartup = AutoUpdateToggle.IsOn;
            _state.Settings.Save();
        }

        private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_lastResult?.DownloadUrl == null || _lastResult.AssetFileName == null)
                return;

            InstallUpdateButton.IsEnabled = false;
            InstallStatusText.Text = Strings.Get("Update_Preparing");

            try
            {
                var progress = new Progress<string>(msg => InstallStatusText.Text = msg);
                string message = await UpdateInstallerService.DownloadAndInstallAsync(
                    _lastResult.DownloadUrl,
                    _lastResult.AssetFileName,
                    progress);
                InstallStatusText.Text = message;
            }
            catch (Exception ex)
            {
                InstallStatusText.Text = Strings.Format("Update_Failed", ex.Message);
                InstallUpdateButton.IsEnabled = true;
            }
        }
    }
}
