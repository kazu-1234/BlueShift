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
        private UpdateCheckResult? _lastResult;

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
            InstallUpdateCard.Visibility = Visibility.Collapsed;
            _lastResult = null;

            var result = await UpdateChecker.CheckForUpdateAsync();
            _lastResult = result;
            UpdateInfoBar.Message = result.Message;
            UpdateInfoBar.IsOpen = true;
            UpdateInfoBar.Severity = result.Status switch
            {
                UpdateCheckStatus.UpdateAvailable => InfoBarSeverity.Informational,
                UpdateCheckStatus.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Success
            };

            CheckUpdateButton.IsEnabled = true;

            if (result.Status == UpdateCheckStatus.UpdateAvailable)
            {
                if (!string.IsNullOrWhiteSpace(result.DownloadUrl) && !string.IsNullOrWhiteSpace(result.AssetFileName))
                {
                    InstallUpdateCard.Visibility = Visibility.Visible;
                    InstallStatusText.Text = Strings.Format("Update_DownloadReady", result.LatestVersion ?? string.Empty);
                }
                else if (!string.IsNullOrWhiteSpace(result.ReleasePageUrl))
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
