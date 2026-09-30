using App1;
using CommunityToolkit.WinUI.Controls;
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
using System.IO;
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
            TimePickerFlyoutFixHelper.Attach(NewTimePicker, NewEndTimePicker);
            PatternsList.ContainerContentChanging += PatternsList_ContainerContentChanging;
            ScheduleLeftPanel.Loaded += (_, _) => ConfigureSchedulePanelChrome(ScheduleLeftPanel);
            ScheduleRightPanel.Loaded += (_, _) =>
            {
                ConfigureSchedulePanelChrome(ScheduleRightPanel);
                ConfigurePatternsListScrollViewer();
                MeasureAndLogCardGaps("ScheduleRightPanel_Loaded");
            };
        }

        private void PatternsList_Loaded(object sender, RoutedEventArgs e)
        {
            ConfigureSchedulePanelChrome(ScheduleLeftPanel);
            ConfigureSchedulePanelChrome(ScheduleRightPanel);
            ConfigurePatternsListScrollViewer();
            MeasureAndLogCardGaps("PatternsList_Loaded");
        }

        private void PatternsList_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            MeasureAndLogCardGaps("PatternsList_SizeChanged");
        }

        /// <summary>
        /// SettingsCard の HeaderPanel は Margin 右 24 固定。Description 経由だと一覧右だけ大きく空く。
        /// ContentAlignment=Vertical は Content を Row1 に送るため、空 Header 行に高さを取られる。
        /// ContentPresenter を RootGrid 全域に広げ、左右余白を Padding のみにする。
        /// </summary>
        private void ConfigureSchedulePanelChrome(SettingsCard? card)
        {
            if (card == null)
                return;

            if (FindDescendantByName(card, "HeaderPanel") is FrameworkElement headerPanel)
            {
                headerPanel.Margin = new Thickness(0);
                headerPanel.Visibility = Visibility.Collapsed;
            }

            if (FindDescendantByName(card, "PART_HeaderIconPresenterHolder") is FrameworkElement iconHolder)
                iconHolder.Visibility = Visibility.Collapsed;

            if (FindDescendantByName(card, "PART_ActionIconPresenterHolder") is FrameworkElement actionHolder)
                actionHolder.Visibility = Visibility.Collapsed;

            if (FindDescendantByName(card, "PART_ContentPresenter") is FrameworkElement content)
            {
                Grid.SetRow(content, 0);
                Grid.SetRowSpan(content, 2);
                Grid.SetColumn(content, 0);
                Grid.SetColumnSpan(content, 4);
                content.Margin = new Thickness(0);
                content.HorizontalAlignment = HorizontalAlignment.Stretch;
                content.VerticalAlignment = VerticalAlignment.Stretch;
                if (content is ContentPresenter presenter)
                {
                    presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    presenter.VerticalContentAlignment = VerticalAlignment.Stretch;
                }
            }
        }

        /// <summary>
        /// 縦スクロールバーが内容幅を予約してカード右に余白が開くのを防ぐ。
        /// </summary>
        private void ConfigurePatternsListScrollViewer()
        {
            if (FindDescendantScrollViewer(PatternsList) is not ScrollViewer sv)
                return;

            sv.Padding = new Thickness(0);
            sv.Margin = new Thickness(0);

            // ScrollViewer 既定テンプレは右列にバー幅を予約する → 0 にしてオーバーレイ化
            if (FindDescendantByName(sv, "Root") is Grid root && root.ColumnDefinitions.Count >= 2)
            {
                root.ColumnDefinitions[1].Width = new GridLength(0);
            }
            else if (VisualTreeHelper.GetChildrenCount(sv) > 0
                     && VisualTreeHelper.GetChild(sv, 0) is Grid layoutRoot
                     && layoutRoot.ColumnDefinitions.Count >= 2)
            {
                layoutRoot.ColumnDefinitions[1].Width = new GridLength(0);
            }

            if (FindDescendantByName(sv, "ScrollContentPresenter") is FrameworkElement presenter)
            {
                presenter.Margin = new Thickness(0);
                presenter.HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetColumnSpan(presenter, 2);
            }

            // ItemsPresenter 既定の左右余白があるとカードが内側に縮む
            if (FindDescendantByName(PatternsList, "ItemsPresenter") is FrameworkElement itemsPresenter)
            {
                itemsPresenter.Margin = new Thickness(0);
                itemsPresenter.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            if (FindDescendantByName(sv, "VerticalScrollBar") is FrameworkElement bar)
            {
                bar.HorizontalAlignment = HorizontalAlignment.Right;
                bar.Margin = new Thickness(0);
                Grid.SetColumn(bar, 0);
                Grid.SetColumnSpan(bar, 2);
                Canvas.SetZIndex(bar, 1);
            }

            // 実測で右だけ空く場合は、予約幅ぶんだけ ListView を右へ伸ばす
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => EqualizeCardSideGaps());
        }

        private void EqualizeCardSideGaps()
        {
            if (PatternsListHost == null || PatternsList == null || PatternsList.Visibility != Visibility.Visible)
                return;

            ListViewItem? firstItem = null;
            for (int i = 0; i < PatternsList.Items.Count; i++)
            {
                if (PatternsList.ContainerFromIndex(i) is ListViewItem item)
                {
                    firstItem = item;
                    break;
                }
            }

            if (firstItem == null)
                return;

            Border? cardBorder = FindFirstDescendantBorder(firstItem);
            FrameworkElement measureTarget = cardBorder ?? (FrameworkElement)firstItem;

            var hostTransform = PatternsListHost.TransformToVisual(null);
            var cardTransform = measureTarget.TransformToVisual(null);
            var hostBounds = hostTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, PatternsListHost.ActualWidth, PatternsListHost.ActualHeight));
            var cardBounds = cardTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, measureTarget.ActualWidth, measureTarget.ActualHeight));

            double leftGap = cardBounds.X - hostBounds.X;
            double rightGap = (hostBounds.X + hostBounds.Width) - (cardBounds.X + cardBounds.Width);
            double delta = rightGap - leftGap;
            if (delta > 0.5)
            {
                // 右余白過多分だけ負の右 Padding で伸ばす（左は触らない）
                PatternsList.Padding = new Thickness(0, 0, -delta, 0);
            }
            else if (delta < -0.5)
            {
                PatternsList.Padding = new Thickness(0);
            }
        }

        /// <summary>
        /// 大枠右端とカード右端の隙間を実測し、左右差をログに残す（検証用）。
        /// </summary>
        private void MeasureAndLogCardGaps(string reason)
        {
            if (PatternsListHost == null || PatternsList == null || PatternsList.Visibility != Visibility.Visible)
                return;

            if (VisualTreeHelper.GetChildrenCount(PatternsList) == 0)
                return;

            ListViewItem? firstItem = null;
            for (int i = 0; i < PatternsList.Items.Count; i++)
            {
                if (PatternsList.ContainerFromIndex(i) is ListViewItem item)
                {
                    firstItem = item;
                    break;
                }
            }

            if (firstItem == null)
                return;

            FrameworkElement cardBorder = firstItem;
            if (VisualTreeHelper.GetChildrenCount(firstItem) > 0
                && VisualTreeHelper.GetChild(firstItem, 0) is FrameworkElement inner)
            {
                // ListViewItem の ContentPresenter → Border
                var border = FindFirstDescendantBorder(firstItem);
                if (border != null)
                    cardBorder = border;
            }

            var hostTransform = PatternsListHost.TransformToVisual(null);
            var cardTransform = cardBorder.TransformToVisual(null);
            var hostBounds = hostTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, PatternsListHost.ActualWidth, PatternsListHost.ActualHeight));
            var cardBounds = cardTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, cardBorder.ActualWidth, cardBorder.ActualHeight));

            double leftGap = cardBounds.X - hostBounds.X;
            double rightGap = (hostBounds.X + hostBounds.Width) - (cardBounds.X + cardBounds.Width);

            double comboHeight = -1;
            double comboFont = -1;
            var cardCombos = FindDescendantComboBoxes(firstItem);
            if (cardCombos.Count > 0)
            {
                comboHeight = cardCombos[0].ActualHeight;
                comboFont = cardCombos[0].FontSize;
            }

            string line =
                $"{DateTime.Now:HH:mm:ss.fff} {reason} leftGap={leftGap:F1} rightGap={rightGap:F1} delta={Math.Abs(leftGap - rightGap):F1} " +
                $"hostW={PatternsListHost.ActualWidth:F1} cardW={cardBorder.ActualWidth:F1} comboH={comboHeight:F1} comboFont={comboFont:F1}";

            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BlueShift",
                    "layout-measure.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // 検証ログ失敗は無視
            }

            System.Diagnostics.Debug.WriteLine(line);
        }

        private static Border? FindFirstDescendantBorder(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Border border)
                    return border;
                var nested = FindFirstDescendantBorder(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv)
                    return sv;
                var nested = FindDescendantScrollViewer(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private static FrameworkElement? FindDescendantByName(DependencyObject parent, string name)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement { Name: { } childName } fe && childName == name)
                    return fe;
                var nested = FindDescendantByName(child, name);
                if (nested != null)
                    return nested;
            }

            return null;
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
                ConfigureSchedulePanelChrome(ScheduleLeftPanel);
                ConfigureSchedulePanelChrome(ScheduleRightPanel);
                SyncPatternsListHostHeight();
                ConfigurePatternsListScrollViewer();
                MeasureAndLogCardGaps("OnNavigatedTo");
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

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => MeasureAndLogCardGaps("AfterAttachCombos"));
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

            string timeStr = FormatTime(NewTimePicker.Time);

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
            ConfigurePatternsListScrollViewer();
            MeasureAndLogCardGaps("AddPattern");
        }

        private static string FormatTime(TimeSpan time) =>
            $"{time.Hours:D2}:{time.Minutes:D2}";

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
