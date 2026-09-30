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

        private enum TimeComboKind
        {
            StartHour,
            StartMinute,
            EndHour,
            EndMinute
        }

        private sealed class TimeComboTag
        {
            public required string PatternId { get; init; }
            public required TimeComboKind Kind { get; init; }
        }

        private const string DashItem = "-";

        private static readonly string[] HourItems =
            Enumerable.Range(0, 24).Select(h => h.ToString("D2", CultureInfo.InvariantCulture)).ToArray();

        private static readonly string[] MinuteItems =
            Enumerable.Range(0, 60).Select(m => m.ToString("D2", CultureInfo.InvariantCulture)).ToArray();

        private static readonly string[] EndHourItems =
            new[] { DashItem }.Concat(HourItems).ToArray();

        private static readonly string[] EndMinuteItems =
            new[] { DashItem }.Concat(MinuteItems).ToArray();

        private AppState? _state;
        private bool _isInitializing;
        private bool _syncingTimeCombos;
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
                    foreach (var combo in FindDescendantComboBoxes(recycledContainer))
                        DetachTimeCombo(combo);
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

            var combos = FindDescendantComboBoxes(container);
            if (combos.Count < 4)
                return;

            AttachTimeCombo(combos[0], pattern, TimeComboKind.StartHour);
            AttachTimeCombo(combos[1], pattern, TimeComboKind.StartMinute);
            AttachTimeCombo(combos[2], pattern, TimeComboKind.EndHour);
            AttachTimeCombo(combos[3], pattern, TimeComboKind.EndMinute);
        }

        private static List<Slider> FindDescendantSliders(DependencyObject root)
        {
            var sliders = new List<Slider>();
            CollectDescendants(root, sliders);
            return sliders;
        }

        private static List<ComboBox> FindDescendantComboBoxes(DependencyObject root)
        {
            var combos = new List<ComboBox>();
            CollectDescendants(root, combos);
            return combos;
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

        private void AttachTimeCombo(ComboBox combo, Pattern pattern, TimeComboKind kind)
        {
            DetachTimeCombo(combo);

            bool isEnd = kind is TimeComboKind.EndHour or TimeComboKind.EndMinute;
            bool isHour = kind is TimeComboKind.StartHour or TimeComboKind.EndHour;
            string[] items = (isEnd, isHour) switch
            {
                (true, true) => EndHourItems,
                (true, false) => EndMinuteItems,
                (false, true) => HourItems,
                _ => MinuteItems
            };

            _syncingTimeCombos = true;
            try
            {
                bool needsRebuild = combo.Tag is not TimeComboTag existing
                    || existing.Kind != kind
                    || combo.Items.Count != items.Length;
                if (needsRebuild)
                {
                    combo.Items.Clear();
                    foreach (string item in items)
                        combo.Items.Add(item);
                }

                combo.Tag = new TimeComboTag { PatternId = pattern.Id, Kind = kind };
                combo.SelectedItem = ResolveSelectedItem(pattern, kind);
            }
            finally
            {
                _syncingTimeCombos = false;
            }

            CompactComboBoxHelper.HideDropDownGlyph(combo);
            combo.PointerWheelChanged += PatternTimeCombo_PointerWheelChanged;
            combo.SelectionChanged += PatternTimeCombo_SelectionChanged;
        }

        private void DetachTimeCombo(ComboBox combo)
        {
            combo.PointerWheelChanged -= PatternTimeCombo_PointerWheelChanged;
            combo.SelectionChanged -= PatternTimeCombo_SelectionChanged;
        }

        private void PatternTimeCombo_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not ComboBox combo || !combo.IsEnabled || combo.Items.Count == 0)
                return;

            int delta = e.GetCurrentPoint(combo).Properties.MouseWheelDelta;
            if (delta == 0)
                return;

            // 上スクロール（正）でインデックス増加＝時刻が進む
            int steps = delta > 0 ? 1 : -1;
            int current = combo.SelectedIndex;
            if (current < 0)
                current = 0;

            int next = Math.Clamp(current + steps, 0, combo.Items.Count - 1);
            if (next == current)
            {
                e.Handled = true;
                return;
            }

            combo.SelectedIndex = next;
            e.Handled = true;
        }

        private static string ResolveSelectedItem(Pattern pattern, TimeComboKind kind)
        {
            if (kind is TimeComboKind.EndHour or TimeComboKind.EndMinute && !pattern.HasEndTime)
                return DashItem;

            string source = kind is TimeComboKind.StartHour or TimeComboKind.StartMinute
                ? pattern.Time
                : pattern.EndTime;

            if (!TrySplitTime(source, out int hour, out int minute))
                return kind is TimeComboKind.StartHour or TimeComboKind.EndHour ? "00" : "00";

            return kind is TimeComboKind.StartHour or TimeComboKind.EndHour
                ? hour.ToString("D2", CultureInfo.InvariantCulture)
                : minute.ToString("D2", CultureInfo.InvariantCulture);
        }

        private void PatternTimeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingTimeCombos || _isInitializing || _state == null)
                return;

            if (sender is not ComboBox combo || combo.Tag is not TimeComboTag tag)
                return;

            var pattern = _state.Patterns.FirstOrDefault(p => p.Id == tag.PatternId);
            if (pattern == null)
                return;

            string selected = combo.SelectedItem as string ?? DashItem;

            if (tag.Kind is TimeComboKind.StartHour or TimeComboKind.StartMinute)
            {
                ApplyStartTimeChange(pattern, tag.Kind, selected);
                return;
            }

            ApplyEndTimeChange(pattern, tag.Kind, selected, combo);
        }

        private void ApplyStartTimeChange(Pattern pattern, TimeComboKind kind, string selected)
        {
            if (selected == DashItem)
                return;

            if (!TrySplitTime(pattern.Time, out int hour, out int minute))
            {
                hour = 0;
                minute = 0;
            }

            if (kind == TimeComboKind.StartHour)
            {
                if (!int.TryParse(selected, NumberStyles.None, CultureInfo.InvariantCulture, out hour))
                    return;
            }
            else
            {
                if (!int.TryParse(selected, NumberStyles.None, CultureInfo.InvariantCulture, out minute))
                    return;
            }

            string timeStr = $"{hour:D2}:{minute:D2}";
            if (pattern.Time == timeStr)
                return;

            pattern.Time = timeStr;
            SortPatternsByStartTime();
            _state?.PersistPatterns();
            _state?.NotifyPatternsChanged();
        }

        private void ApplyEndTimeChange(Pattern pattern, TimeComboKind kind, string selected, ComboBox source)
        {
            if (!FindSiblingEndCombos(source, out ComboBox? endHourCombo, out ComboBox? endMinuteCombo)
                || endHourCombo == null
                || endMinuteCombo == null)
            {
                return;
            }

            string hourValue = endHourCombo.SelectedItem as string ?? DashItem;
            string minuteValue = endMinuteCombo.SelectedItem as string ?? DashItem;

            if (kind == TimeComboKind.EndHour)
                hourValue = selected;
            else
                minuteValue = selected;

            // 時が - なら分も - に揃える
            if (hourValue == DashItem && minuteValue != DashItem)
            {
                _syncingTimeCombos = true;
                try
                {
                    endMinuteCombo.SelectedItem = DashItem;
                }
                finally
                {
                    _syncingTimeCombos = false;
                }

                minuteValue = DashItem;
            }

            if (hourValue == DashItem || minuteValue == DashItem)
            {
                if (!pattern.HasEndTime)
                    return;

                pattern.HasEndTime = false;
                _state?.PersistPatterns();
                _state?.NotifyPatternsChanged();
                return;
            }

            if (!int.TryParse(hourValue, NumberStyles.None, CultureInfo.InvariantCulture, out int hour)
                || !int.TryParse(minuteValue, NumberStyles.None, CultureInfo.InvariantCulture, out int minute))
            {
                return;
            }

            string endTimeStr = $"{hour:D2}:{minute:D2}";
            if (pattern.HasEndTime && pattern.EndTime == endTimeStr)
                return;

            pattern.HasEndTime = true;
            pattern.EndTime = endTimeStr;
            _state?.PersistPatterns();
            _state?.NotifyPatternsChanged();
        }

        private static bool FindSiblingEndCombos(ComboBox source, out ComboBox? endHour, out ComboBox? endMinute)
        {
            endHour = null;
            endMinute = null;

            DependencyObject? current = source;
            while (current != null)
            {
                if (current is ListViewItem item)
                {
                    var combos = FindDescendantComboBoxes(item);
                    if (combos.Count < 4)
                        return false;

                    endHour = combos[2];
                    endMinute = combos[3];
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
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

        private void HasEndTimeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            NewEndTimePicker.IsEnabled = HasEndTimeCheckBox.IsChecked == true;
        }

        private void AddPattern_Click(object sender, RoutedEventArgs e)
        {
            if (_state == null) return;

            var newTime = NewTimePicker.Time;
            string timeStr = FormatTime(newTime);

            bool hasEndTime = HasEndTimeCheckBox.IsChecked == true;
            string endTimeStr = "00:00";
            if (hasEndTime)
                endTimeStr = FormatTime(NewEndTimePicker.Time);

            _state.Patterns.Add(new Pattern
            {
                Time = timeStr,
                HasEndTime = hasEndTime,
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

        private static bool TrySplitTime(string value, out int hour, out int minute)
        {
            hour = 0;
            minute = 0;
            var parts = value.Split(':');
            if (parts.Length != 2)
                return false;

            return int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minute);
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
