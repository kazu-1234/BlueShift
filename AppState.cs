using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace App1
{
    /// <summary>
    /// 各ページで共有するアプリ状態。
    /// </summary>
    public sealed class AppState : INotifyPropertyChanged
    {
        public Settings Settings { get; }
        public ObservableCollection<Pattern> Patterns { get; }

        public Action? SavePatterns { get; set; }
        public Action? RefreshGamma { get; set; }
        public Action<GammaSettings>? PreviewGamma { get; set; }
        public Action? RescheduleTimer { get; set; }
        public Action? ApplyTrayIconVisibility { get; set; }

        private string _statusMessage = string.Empty;
        private InfoBarSeverity _statusSeverity = InfoBarSeverity.Informational;
        private string _currentIntensityText = "—";
        private string _currentColorTemperatureText = "—";
        private string _activeScheduleText = "—";
        private string _nextTransitionText = "—";
        private bool _isFilterEnabled = true;

        public AppState(Settings settings, ObservableCollection<Pattern> patterns)
        {
            Settings = settings;
            Patterns = patterns;
            _isFilterEnabled = settings.IsFilterEnabled;
        }

        public bool IsFilterEnabled
        {
            get => _isFilterEnabled;
            set
            {
                if (_isFilterEnabled == value) return;
                _isFilterEnabled = value;
                Settings.IsFilterEnabled = value;
                Settings.Save();
                OnPropertyChanged();
                RefreshGamma?.Invoke();
                RescheduleTimer?.Invoke();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public InfoBarSeverity StatusSeverity
        {
            get => _statusSeverity;
            private set { _statusSeverity = value; OnPropertyChanged(); }
        }

        public string CurrentIntensityText
        {
            get => _currentIntensityText;
            private set { _currentIntensityText = value; OnPropertyChanged(); }
        }

        public string CurrentColorTemperatureText
        {
            get => _currentColorTemperatureText;
            private set { _currentColorTemperatureText = value; OnPropertyChanged(); }
        }

        public string ActiveScheduleText
        {
            get => _activeScheduleText;
            private set { _activeScheduleText = value; OnPropertyChanged(); }
        }

        public string NextTransitionText
        {
            get => _nextTransitionText;
            private set { _nextTransitionText = value; OnPropertyChanged(); }
        }

        public void UpdateRuntimeStatus(
            string message,
            InfoBarSeverity severity,
            GammaSettings? appliedSettings,
            Pattern? activePattern)
        {
            StatusMessage = message;
            StatusSeverity = severity;
            CurrentIntensityText = appliedSettings.HasValue
                ? $"{appliedSettings.Value.Intensity}%"
                : Strings.Get("NotAvailable");
            CurrentColorTemperatureText = appliedSettings.HasValue
                ? $"{appliedSettings.Value.ColorTemperatureKelvin}K"
                : Strings.Get("NotAvailable");
            ActiveScheduleText = activePattern?.TimeRangeDisplay ?? Strings.Get("NotAvailable");

            var delay = ScheduleHelper.GetDelayUntilNextTransition(Patterns, DateTime.Now);
            NextTransitionText = delay.HasValue
                ? FormatDelay(delay.Value)
                : Strings.Get("NotAvailable");
        }

        public void NotifyPatternsChanged()
        {
            OnPropertyChanged(nameof(Patterns));
            RefreshGamma?.Invoke();
            RescheduleTimer?.Invoke();
        }

        public void PersistPatterns()
        {
            Settings.Patterns = Patterns.ToList();
            Settings.Save();
            SavePatterns?.Invoke();
        }

        private static string FormatDelay(TimeSpan delay)
        {
            if (delay.TotalHours >= 1)
                return Strings.Format("Delay_HoursMinutes", (int)delay.TotalHours, delay.Minutes);
            if (delay.TotalMinutes >= 1)
                return Strings.Format("Delay_Minutes", (int)delay.TotalMinutes);
            return Strings.Format("Delay_Seconds", (int)delay.TotalSeconds);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
