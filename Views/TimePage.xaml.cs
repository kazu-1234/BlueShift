using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace App1.Views
{
    public sealed partial class TimePage : Page
    {
        private AppState? _state;
        private bool _isInitializing;
        private readonly HashSet<Slider> _draggingSliders = new();
        private readonly HashSet<Slider> _wheelAdjustingSliders = new();

        public TimePage()
        {
            InitializeComponent();
            AttachSliderInteractions(NewIntensitySlider);
            PatternsList.ContainerContentChanging += PatternsList_ContainerContentChanging;
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

        private void PatternsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
                return;

            if (args.ItemContainer is not ListViewItem container)
                return;

            container.Loaded -= PatternItemContainer_Loaded;
            container.Loaded += PatternItemContainer_Loaded;
        }

        private void PatternItemContainer_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListViewItem container)
                return;

            container.Loaded -= PatternItemContainer_Loaded;
            if (FindDescendantSlider(container) is Slider slider)
                AttachSliderInteractions(slider);
        }

        private static Slider? FindDescendantSlider(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Slider slider)
                    return slider;

                if (FindDescendantSlider(child) is Slider found)
                    return found;
            }

            return null;
        }

        private void AttachSliderInteractions(Slider slider)
        {
            slider.PointerPressed -= Slider_PointerPressed;
            slider.PointerCaptureLost -= Slider_DragCaptureLost;
            slider.PointerWheelChanged -= Slider_PointerWheelChanged;

            slider.PointerPressed += Slider_PointerPressed;
            slider.PointerCaptureLost += Slider_DragCaptureLost;
            slider.PointerWheelChanged += Slider_PointerWheelChanged;
        }

        private void Slider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Slider slider)
                _draggingSliders.Add(slider);
        }

        private void Slider_DragCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Slider slider)
                return;

            _draggingSliders.Remove(slider);
        }

        private void Slider_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Slider slider)
                return;

            _wheelAdjustingSliders.Add(slider);
            SliderWheelHelper.HandlePointerWheelChanged(sender, e);
            DispatcherQueue.TryEnqueue(() => _wheelAdjustingSliders.Remove(slider));
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

            ApplySliderGammaFeedback(slider, (int)e.NewValue);
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
            if (sender is Slider slider)
                ApplySliderGammaFeedback(slider, (int)e.NewValue);
        }

        private void NewIntensitySlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            _state?.RefreshGamma?.Invoke();
        }

        /// <summary>
        /// ドラッグ中は一時プレビュー。ホイール操作時はフィルター無効ならガンマを変えない。
        /// </summary>
        private void ApplySliderGammaFeedback(Slider slider, int intensity)
        {
            if (_state == null)
                return;

            if (_wheelAdjustingSliders.Contains(slider))
            {
                if (_state.IsFilterEnabled && _state.Patterns.Any())
                    _state.PreviewGamma?.Invoke(intensity);
                return;
            }

            if (_draggingSliders.Contains(slider))
            {
                _state.PreviewGamma?.Invoke(intensity);
                return;
            }

            if (_state.IsFilterEnabled && _state.Patterns.Any())
                _state.PreviewGamma?.Invoke(intensity);
        }

        private static Pattern? GetPatternFromSlider(Slider slider)
        {
            if (slider.Tag is Pattern tagPattern) return tagPattern;
            if (slider.DataContext is Pattern dataPattern) return dataPattern;
            return null;
        }
    }
}
