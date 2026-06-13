using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Core;

namespace App1.Views
{
    public sealed partial class TimePage : Page
    {
        private AppState? _state;
        private bool _isInitializing;
        private readonly HashSet<Slider> _adjustingSliders = new();
        private readonly HashSet<Slider> _wheelAdjustingSliders = new();
        private readonly HashSet<Slider> _bindingSliders = new();

        public TimePage()
        {
            _pointerPressedHandler = Slider_PointerPressed;
            _pointerReleasedHandler = Slider_PointerReleased;
            _pointerCaptureLostHandler = Slider_PointerCaptureLost;

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

            _state.PropertyChanged -= State_PropertyChanged;
            _state.PropertyChanged += State_PropertyChanged;

            UpdateEmptyState();
            UpdateStatusFromState();

            // ListView のバインド完了後に現在時刻の強度へ復帰する
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _isInitializing = false;
                _state?.RefreshGamma?.Invoke();
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_state != null)
            {
                _state.PropertyChanged -= State_PropertyChanged;
                _state.RefreshGamma?.Invoke();
            }

            base.OnNavigatedFrom(e);
        }

        private void PatternsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                if (args.ItemContainer is ListViewItem recycledContainer
                    && FindDescendantSlider(recycledContainer) is Slider recycledSlider)
                {
                    DetachSliderInteractions(recycledSlider);
                }

                return;
            }

            if (args.ItemContainer is not ListViewItem container)
                return;

            if (args.Phase == 0)
            {
                args.RegisterUpdateCallback(PatternsList_ContainerContentChanging);
                return;
            }

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
            DetachSliderInteractions(slider);

            // Thumb 等がイベントを処理済みでもドラッグ開始を検知する
            slider.AddHandler(UIElement.PointerPressedEvent, _pointerPressedHandler, true);
            slider.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler, true);
            slider.AddHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, true);
            slider.PointerWheelChanged += Slider_PointerWheelChanged;
            slider.ValueChanged += AnySlider_ValueChanged;
        }

        private void DetachSliderInteractions(Slider slider)
        {
            slider.RemoveHandler(UIElement.PointerPressedEvent, _pointerPressedHandler);
            slider.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler);
            slider.RemoveHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
            slider.PointerWheelChanged -= Slider_PointerWheelChanged;
            slider.ValueChanged -= AnySlider_ValueChanged;
        }

        private void Slider_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Slider slider)
                _adjustingSliders.Add(slider);
        }

        private void Slider_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Slider slider)
                EndSliderAdjustment(slider);
        }

        private void Slider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Slider slider)
                EndSliderAdjustment(slider);
        }

        private readonly PointerEventHandler _pointerPressedHandler;
        private readonly PointerEventHandler _pointerReleasedHandler;
        private readonly PointerEventHandler _pointerCaptureLostHandler;

        private void Slider_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Slider slider)
                return;

            _wheelAdjustingSliders.Add(slider);
            SliderWheelHelper.HandlePointerWheelChanged(sender, e);

            if (GetPatternFromSlider(slider) is Pattern pattern)
            {
                pattern.Intensity = (int)slider.Value;
                _state?.PersistPatterns();
            }

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

        private void AnySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isInitializing || _state == null || sender is not Slider slider)
                return;

            if (_bindingSliders.Contains(slider))
                return;

            if (ReferenceEquals(slider, NewIntensitySlider))
                NewIntensityValue.Text = $"{(int)e.NewValue}%";

            if (!IsUserAdjustingSlider(slider))
                return;

            _adjustingSliders.Add(slider);
            ApplySliderGammaFeedback(slider, (int)e.NewValue);
        }

        private void EndSliderAdjustment(Slider slider)
        {
            if (!_adjustingSliders.Remove(slider))
                return;

            if (GetPatternFromSlider(slider) is Pattern pattern)
            {
                pattern.Intensity = (int)slider.Value;
                _state?.PersistPatterns();
            }

            _state?.RefreshGamma?.Invoke();
        }

        /// <summary>
        /// マウス操作中のみ一時プレビュー（有効/無効に関わらず画面ガンマを反映）。
        /// ホイール・ページ表示時のバインドではガンマを変えない。
        /// </summary>
        private void ApplySliderGammaFeedback(Slider slider, int intensity)
        {
            if (_state == null || _wheelAdjustingSliders.Contains(slider))
                return;

            if (IsUserAdjustingSlider(slider))
                _state.PreviewGamma?.Invoke(intensity);
        }

        private bool IsUserAdjustingSlider(Slider slider)
        {
            if (_wheelAdjustingSliders.Contains(slider))
                return false;

            return _adjustingSliders.Contains(slider) || IsPrimaryPointerPressed();
        }

        private static bool IsPrimaryPointerPressed()
        {
            var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.LeftButton);
            return state.HasFlag(CoreVirtualKeyStates.Down);
        }

        private Pattern? GetPatternFromSlider(Slider slider)
        {
            if (_state == null)
                return null;

            if (slider.Tag is string id)
                return _state.Patterns.FirstOrDefault(p => p.Id == id);

            if (slider.Tag is Pattern tagPattern)
                return tagPattern;

            if (slider.DataContext is Pattern dataPattern)
                return dataPattern;

            return null;
        }
    }
}
