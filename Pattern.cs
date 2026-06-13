using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace App1
{
    public class Pattern : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        private string _time = "00:00";
        public string Time
        {
            get => _time;
            set
            {
                if (_time != value)
                {
                    _time = value;
                    NotifyDisplayPropertiesChanged();
                }
            }
        }

        private bool _hasEndTime;
        public bool HasEndTime
        {
            get => _hasEndTime;
            set
            {
                if (_hasEndTime != value)
                {
                    _hasEndTime = value;
                    NotifyDisplayPropertiesChanged();
                }
            }
        }

        private string _endTime = "00:00";
        public string EndTime
        {
            get => _endTime;
            set
            {
                if (_endTime != value)
                {
                    _endTime = value;
                    NotifyDisplayPropertiesChanged();
                }
            }
        }

        private int _intensity = 50;
        public int Intensity
        {
            get => _intensity;
            set
            {
                _intensity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IntensityDisplay));
            }
        }

        private int _colorTemperatureKelvin = GammaSettings.DefaultColorTemperatureKelvin;
        /// <summary>色温度（K）。2700（暖色）〜 6500（標準）。</summary>
        public int ColorTemperatureKelvin
        {
            get => _colorTemperatureKelvin;
            set
            {
                _colorTemperatureKelvin = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorTemperatureDisplay));
            }
        }

        public string IntensityDisplay => $"{Intensity}%";

        public string ColorTemperatureDisplay => $"{ColorTemperatureKelvin}K";

        public string TimeRangeDisplay =>
            HasEndTime
                ? Strings.Format("Pattern_TimeRange", Time, EndTime)
                : Strings.Format("Pattern_TimeRangeOpen", Time);

        public bool ShowEndTime => HasEndTime;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyDisplayPropertiesChanged()
        {
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimeRangeDisplay));
            OnPropertyChanged(nameof(ShowEndTime));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
