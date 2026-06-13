using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;

namespace App1.Views
{
    public sealed partial class TimePage : Page
    {
        private AppState? _state;
        private bool _isInitializing;

        public TimePage()
        {
            InitializeComponent();
        }

        private void ApplyFilterToggleLabels()
        {
            FilterToggle.OnContent = Strings.Get("Toggle_On");
            FilterToggle.OffContent = Strings.Get("Toggle_Off");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _state = e.Parameter as AppState;
            if (_state == null) return;

            _isInitializing = true;
            ApplyFilterToggleLabels();
            PatternsList.ItemsSource = _state.Patterns;
            FilterToggle.IsOn = _state.IsFilterEnabled;
            _isInitializing = false;

            _state.PropertyChanged -= State_PropertyChanged;
            _state.PropertyChanged += State_PropertyChanged;

            UpdateEmptyState();
            UpdateStatusFromState();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_state != null)
                _state.PropertyChanged -= State_PropertyChanged;
            base.OnNavigatedFrom(e);
        }

        private void State_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AppState.StatusMessage)
                or nameof(AppState.StatusSeverity))
            {
                UpdateStatusFromState();
            }
        }

        private void UpdateStatusFromState()
        {
            if (_state == null || StatusInfoBar == null) return;

            StatusInfoBar.Message = _state.StatusMessage;
            StatusInfoBar.Severity = _state.StatusSeverity;
            StatusInfoBar.IsOpen = !string.IsNullOrEmpty(_state.StatusMessage);
        }

        private void UpdateEmptyState()
        {
            if (_state == null) return;
            bool isEmpty = !_state.Patterns.Any();
            EmptyStateText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            PatternsList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        private void FilterToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null) return;
            _state.IsFilterEnabled = FilterToggle.IsOn;
        }

        private void HasEndTimeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            NewEndTimePicker.IsEnabled = HasEndTimeCheckBox.IsChecked == true;
        }

        private void AddPattern_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null) return;

            var newTime = NewTimePicker.Time;
            string timeStr = $"{newTime.Hours:D2}:{newTime.Minutes:D2}";

            bool hasEndTime = HasEndTimeCheckBox.IsChecked == true;
            string endTimeStr = "00:00";
            if (hasEndTime)
            {
                var et = NewEndTimePicker.Time;
                endTimeStr = $"{et.Hours:D2}:{et.Minutes:D2}";
            }

            _state.Patterns.Add(new Pattern
            {
                Time = timeStr,
                HasEndTime = hasEndTime,
                EndTime = endTimeStr,
                Intensity = (int)NewIntensitySlider.Value
            });

            var sorted = _state.Patterns.OrderBy(p => p.Time).ToList();
            _state.Patterns.Clear();
            foreach (var p in sorted) _state.Patterns.Add(p);

            _state.PersistPatterns();
            _state.NotifyPatternsChanged();
            UpdateEmptyState();
        }

        private void DeletePattern_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null || sender is not Button btn || btn.Tag is not string id) return;

            var pattern = _state.Patterns.FirstOrDefault(p => p.Id == id);
            if (pattern == null) return;

            _state.Patterns.Remove(pattern);
            _state.PersistPatterns();
            _state.NotifyPatternsChanged();
            UpdateEmptyState();
        }

        private void Slider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isInitializing || _state == null || sender is not Slider slider) return;

            if (GetPatternFromSlider(slider) is Pattern pattern)
                pattern.Intensity = (int)e.NewValue;

            if (_state.IsFilterEnabled && _state.Patterns.Any())
                GammaController.SetGamma((int)e.NewValue);
        }

        private void Slider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_state == null || sender is not Slider slider) return;
            if (GetPatternFromSlider(slider) is not Pattern pattern) return;

            pattern.Intensity = (int)slider.Value;
            _state.PersistPatterns();
            _state.RefreshGamma?.Invoke();
        }

        private void NewIntensitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            NewIntensityValue.Text = $"{(int)e.NewValue}%";
            if (_state?.IsFilterEnabled == true && _state.Patterns.Any())
                GammaController.SetGamma((int)e.NewValue);
        }

        private void NewIntensitySlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _state?.RefreshGamma?.Invoke();
        }

        private static Pattern? GetPatternFromSlider(Slider slider)
        {
            if (slider.Tag is Pattern tagPattern) return tagPattern;
            if (slider.DataContext is Pattern dataPattern) return dataPattern;
            return null;
        }
    }
}
