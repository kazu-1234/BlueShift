using App1;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.UI.Core;
using WinUiShared;

namespace App1.Views
{
    public sealed partial class TimePage : Page
    {
        private enum SliderAdjustmentKind
        {
            Intensity,
            ColorTemperature
        }

        private AppState? _state;
        private bool _isInitializing;
        private bool _syncingTimePickers;
        private bool _newHasEndTime;
        private readonly HashSet<Slider> _adjustingSliders = new();
        private readonly HashSet<Slider> _wheelAdjustingSliders = new();
        private readonly HashSet<Slider> _bindingSliders = new();
        private readonly Dictionary<Slider, SliderAdjustmentKind> _sliderKinds = new();

        public TimePage()
        {
            _pointerPressedHandler = Slider_PointerPressed;
            _pointerReleasedHandler = Slider_PointerReleased;
            _pointerCaptureLostHandler = Slider_PointerCaptureLost;

            InitializeComponent();
            ToggleSwitchClickHelper.BindCardClick(FilterToggleCard, FilterToggle);
            AttachSliderInteractions(NewIntensitySlider, SliderAdjustmentKind.Intensity);
            AttachSliderInteractions(NewColorTemperatureSlider, SliderAdjustmentKind.ColorTemperature);
            PatternsList.ContainerContentChanging += PatternsList_ContainerContentChanging;
            UpdateNewEndTimeSlot();
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
            UpdateNewColorTemperatureValue();

            _state.PropertyChanged -= State_PropertyChanged;
            _state.PropertyChanged += State_PropertyChanged;

            UpdateEmptyState();
            UpdateStatusFromState();
            // トグル操作を握りつぶさないよう、IsOn 代入直後に初期化フラグを下ろす。
            _isInitializing = false;

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                SyncPatternsListHostHeight();
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

        private void ScheduleLeftPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SyncPatternsListHostHeight();
        }

        private void ScheduleLayoutGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SyncPatternsListHostHeight();
        }

        /// <summary>
        /// 横並び時は左パネル高さに合わせ、縦並び時は行の残り高さ内で一覧だけスクロールする。
        /// </summary>
        private void SyncPatternsListHostHeight()
        {
            if (PatternsListHost == null || ScheduleLeftPanel == null || ScheduleLayoutGrid == null)
                return;

            bool sideBySide = Grid.GetRow(ScheduleRightPanel) == 0
                && Grid.GetColumn(ScheduleRightPanel) == 1;

            if (sideBySide)
            {
                double leftHeight = ScheduleLeftPanel.ActualHeight;
                if (leftHeight > 0)
                    PatternsListHost.MaxHeight = leftHeight;
                else
                    PatternsListHost.ClearValue(FrameworkElement.MaxHeightProperty);
                return;
            }

            double row1Height = ScheduleLayoutGrid.RowDefinitions[1].ActualHeight;
            if (row1Height > 0)
                PatternsListHost.MaxHeight = row1Height;
            else
                PatternsListHost.ClearValue(FrameworkElement.MaxHeightProperty);
        }

        private void PatternsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                if (args.ItemContainer is ListViewItem recycledContainer)
                {
                    foreach (var slider in FindDescendantSliders(recycledContainer))
                        DetachSliderInteractions(slider);
                    foreach (var picker in FindDescendantTimePickers(recycledContainer))
                        DetachTimePickerInteractions(picker);
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

            var sliders = FindDescendantSliders(container);
            if (sliders.Count >= 1)
                AttachSliderInteractions(sliders[0], SliderAdjustmentKind.Intensity);
            if (sliders.Count >= 2)
                AttachSliderInteractions(sliders[1], SliderAdjustmentKind.ColorTemperature);

            if (args.Item is not Pattern pattern)
                return;

            var pickers = FindDescendantTimePickers(container);
            if (pickers.Count >= 1)
                AttachStartTimePicker(pickers[0], pattern);
            if (pickers.Count >= 2)
                AttachEndTimePicker(pickers[1], pattern);
        }

        private static List<Slider> FindDescendantSliders(DependencyObject root)
        {
            var sliders = new List<Slider>();
            CollectDescendants(root, sliders);
            return sliders;
        }

        private static List<TimePicker> FindDescendantTimePickers(DependencyObject root)
        {
            var pickers = new List<TimePicker>();
            CollectDescendants(root, pickers);
            return pickers;
        }

        private static void CollectDescendants<T>(DependencyObject root, List<T> results)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    results.Add(match);
                else
                    CollectDescendants(child, results);
            }
        }

        private void AttachSliderInteractions(Slider slider, SliderAdjustmentKind kind)
        {
            DetachSliderInteractions(slider);
            _sliderKinds[slider] = kind;

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
            _sliderKinds.Remove(slider);
        }

        private void AttachStartTimePicker(TimePicker picker, Pattern pattern)
        {
            DetachTimePickerInteractions(picker);
            picker.Tag = pattern.Id;
            _syncingTimePickers = true;
            try
            {
                picker.Time = ParseTimeOrDefault(pattern.Time);
            }
            finally
            {
                _syncingTimePickers = false;
            }

            picker.SelectedTimeChanged += PatternStartTimePicker_SelectedTimeChanged;
        }

        private void AttachEndTimePicker(TimePicker picker, Pattern pattern)
        {
            DetachTimePickerInteractions(picker);
            picker.Tag = pattern.Id;
            _syncingTimePickers = true;
            try
            {
                picker.Time = ParseTimeOrDefault(pattern.EndTime);
            }
            finally
            {
                _syncingTimePickers = false;
            }

            picker.SelectedTimeChanged += PatternEndTimePicker_SelectedTimeChanged;
        }

        private void DetachTimePickerInteractions(TimePicker picker)
        {
            picker.SelectedTimeChanged -= PatternStartTimePicker_SelectedTimeChanged;
            picker.SelectedTimeChanged -= PatternEndTimePicker_SelectedTimeChanged;
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
                ApplySliderValueToPattern(slider, pattern, (int)slider.Value);
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
            SyncPatternsListHostHeight();
        }

        private void FilterToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _state == null) return;
            _state.IsFilterEnabled = FilterToggle.IsOn;
        }

        private void UpdateNewEndTimeSlot()
        {
            NewEndTimePicker.Visibility = _newHasEndTime ? Visibility.Visible : Visibility.Collapsed;
            NewEndTimeDashButton.Visibility = _newHasEndTime ? Visibility.Collapsed : Visibility.Visible;
            NewClearEndTimeButton.Visibility = _newHasEndTime ? Visibility.Visible : Visibility.Collapsed;
        }

        private void NewEndTimeDashButton_Click(object sender, RoutedEventArgs e)
        {
            _newHasEndTime = true;
            UpdateNewEndTimeSlot();
        }

        private void NewClearEndTimeButton_Click(object sender, RoutedEventArgs e)
        {
            _newHasEndTime = false;
            UpdateNewEndTimeSlot();
        }

        private void AddPattern_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null) return;

            var newTime = NewTimePicker.Time;
            string timeStr = FormatTime(newTime);

            string endTimeStr = "00:00";
            if (_newHasEndTime)
                endTimeStr = FormatTime(NewEndTimePicker.Time);

            _state.Patterns.Add(new Pattern
            {
                Time = timeStr,
                HasEndTime = _newHasEndTime,
                EndTime = endTimeStr,
                Intensity = (int)NewIntensitySlider.Value,
                ColorTemperatureKelvin = (int)NewColorTemperatureSlider.Value
            });

            SortPatternsByStartTime();

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

        private void PatternStartTimePicker_SelectedTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
        {
            if (_syncingTimePickers || _isInitializing || _state == null)
                return;

            if (GetPatternFromTag(sender.Tag) is not Pattern pattern)
                return;

            string timeStr = FormatTime(sender.Time);
            if (pattern.Time == timeStr)
                return;

            pattern.Time = timeStr;
            SortPatternsByStartTime();
            _state.PersistPatterns();
            _state.NotifyPatternsChanged();
        }

        private void PatternEndTimePicker_SelectedTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
        {
            if (_syncingTimePickers || _isInitializing || _state == null)
                return;

            if (GetPatternFromTag(sender.Tag) is not Pattern pattern)
                return;

            if (!pattern.HasEndTime)
                return;

            string timeStr = FormatTime(sender.Time);
            if (pattern.EndTime == timeStr)
                return;

            pattern.EndTime = timeStr;
            _state.PersistPatterns();
            _state.NotifyPatternsChanged();
        }

        private void PatternEndTimeDashButton_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null || sender is not Button btn)
                return;

            if (GetPatternFromTag(btn.Tag) is not Pattern pattern)
                return;

            pattern.HasEndTime = true;
            if (string.IsNullOrWhiteSpace(pattern.EndTime))
                pattern.EndTime = "00:00";

            _state.PersistPatterns();
            _state.NotifyPatternsChanged();
        }

        private void PatternClearEndTimeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null || sender is not Button btn)
                return;

            if (GetPatternFromTag(btn.Tag) is not Pattern pattern)
                return;

            pattern.HasEndTime = false;
            _state.PersistPatterns();
            _state.NotifyPatternsChanged();
        }

        private void SortPatternsByStartTime()
        {
            if (_state == null) return;

            var sorted = _state.Patterns.OrderBy(p => p.Time).ToList();
            _state.Patterns.Clear();
            foreach (var p in sorted)
                _state.Patterns.Add(p);
        }

        private static string FormatTime(TimeSpan time) =>
            $"{time.Hours:D2}:{time.Minutes:D2}";

        private static TimeSpan ParseTimeOrDefault(string value)
        {
            if (TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var time))
                return time;

            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time))
                return time;

            return TimeSpan.Zero;
        }

        private Pattern? GetPatternFromTag(object? tag)
        {
            if (_state == null || tag is not string id)
                return null;

            return _state.Patterns.FirstOrDefault(p => p.Id == id);
        }

        private void AnySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isInitializing || _state == null || sender is not Slider slider)
                return;

            if (_bindingSliders.Contains(slider))
                return;

            UpdateSliderValueLabel(slider, (int)e.NewValue);

            if (!IsUserAdjustingSlider(slider))
                return;

            _adjustingSliders.Add(slider);
            ApplySliderGammaFeedback(slider);
        }

        private void EndSliderAdjustment(Slider slider)
        {
            if (!_adjustingSliders.Remove(slider))
                return;

            if (GetPatternFromSlider(slider) is Pattern pattern)
            {
                ApplySliderValueToPattern(slider, pattern, (int)slider.Value);
                _state?.PersistPatterns();
            }

            _state?.RefreshGamma?.Invoke();
        }

        private void ApplySliderGammaFeedback(Slider slider)
        {
            if (_state == null || _wheelAdjustingSliders.Contains(slider))
                return;

            if (IsUserAdjustingSlider(slider))
                _state.PreviewGamma?.Invoke(BuildPreviewSettings(slider));
        }

        private GammaSettings BuildPreviewSettings(Slider adjustedSlider)
        {
            if (ReferenceEquals(adjustedSlider, NewIntensitySlider))
            {
                return new GammaSettings
                {
                    Intensity = (int)NewIntensitySlider.Value,
                    ColorTemperatureKelvin = (int)NewColorTemperatureSlider.Value
                };
            }

            if (ReferenceEquals(adjustedSlider, NewColorTemperatureSlider))
            {
                return new GammaSettings
                {
                    Intensity = (int)NewIntensitySlider.Value,
                    ColorTemperatureKelvin = (int)NewColorTemperatureSlider.Value
                };
            }

            if (GetPatternFromSlider(adjustedSlider) is Pattern pattern)
            {
                if (GetSliderKind(adjustedSlider) == SliderAdjustmentKind.ColorTemperature)
                {
                    return new GammaSettings
                    {
                        Intensity = pattern.Intensity,
                        ColorTemperatureKelvin = (int)adjustedSlider.Value
                    };
                }

                return new GammaSettings
                {
                    Intensity = (int)adjustedSlider.Value,
                    ColorTemperatureKelvin = pattern.ColorTemperatureKelvin
                };
            }

            return GammaSettings.Off;
        }

        private void ApplySliderValueToPattern(Slider slider, Pattern pattern, int value)
        {
            if (GetSliderKind(slider) == SliderAdjustmentKind.ColorTemperature)
                pattern.ColorTemperatureKelvin = value;
            else
                pattern.Intensity = value;
        }

        private void UpdateSliderValueLabel(Slider slider, int value)
        {
            if (ReferenceEquals(slider, NewIntensitySlider))
                NewIntensityValue.Text = $"{value}%";
            else if (ReferenceEquals(slider, NewColorTemperatureSlider))
                NewColorTemperatureValue.Text = $"{value}K";
        }

        private void UpdateNewColorTemperatureValue()
        {
            NewColorTemperatureValue.Text = $"{(int)NewColorTemperatureSlider.Value}K";
        }

        private SliderAdjustmentKind GetSliderKind(Slider slider)
        {
            return _sliderKinds.TryGetValue(slider, out var kind)
                ? kind
                : SliderAdjustmentKind.Intensity;
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
