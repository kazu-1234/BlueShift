using App1;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinUiShared;

namespace App1.Views
{
    /// <summary>
    /// 情報ページ。上の「状態」欄はこのアプリ固有（AppState の変化に合わせて表示し直す）。
    /// 「更新の確認」欄の処理は共通部の UpdateInfoController が行う（XAML の部品を渡すだけ）。
    /// </summary>
    public sealed partial class InfoPage : Page
    {
        private readonly UpdateInfoController _update;
        private AppState? _state;

        public InfoPage()
        {
            InitializeComponent();
            _update = new UpdateInfoController(new UpdateInfoControls
            {
                VersionText = VersionText,
                LastUpdateCheckText = LastUpdateCheckText,
                CheckUpdateButton = CheckUpdateButton,
                UpdateInfoBar = UpdateInfoBar,
                AutoUpdateToggleCard = AutoUpdateToggleCard,
                AutoUpdateToggle = AutoUpdateToggle,
                InstallUpdateCard = InstallUpdateCard,
                InstallStatusText = InstallStatusText,
                InstallUpdateButton = InstallUpdateButton
            });
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _state = e.Parameter as AppState;
            if (_state == null) return;

            _update.Bind(_state.Settings);

            // 二重に登録しないよう、外してから付ける（同じページに戻ってきたときのため）
            _state.PropertyChanged -= State_PropertyChanged;
            _state.PropertyChanged += State_PropertyChanged;
            RefreshDisplay();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // ページを離れたら購読をやめる（閉じたページが AppState から参照され続けないように）
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
    }
}
