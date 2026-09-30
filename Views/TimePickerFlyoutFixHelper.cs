using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI;

namespace App1.Views;

/// <summary>
/// TimePicker フライアウトの時・分選択ハイライトが境目で重なり暗い筋になるのを防ぐ。
/// 列間隔を広げ、横断 HighlightRect を1本にし、列ごとの MonochromaticOverlayPresenter 塗りを消す。
/// </summary>
internal static class TimePickerFlyoutFixHelper
{
    private const double ColumnGap = 14;

    private static readonly HashSet<TimePicker> Attached = [];
    private static readonly HashSet<FrameworkElement> SizeHooked = [];

    public static void Attach(params TimePicker[] pickers)
    {
        foreach (TimePicker picker in pickers)
        {
            if (!Attached.Add(picker))
                continue;

            picker.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPickerPointerPressed), true);
            picker.AddHandler(UIElement.TappedEvent, new TappedEventHandler(OnPickerTapped), true);
            picker.GotFocus += OnPickerGotFocus;
        }
    }

    private static void OnPickerGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TimePicker picker)
            ScheduleFix(picker);
    }

    private static void OnPickerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is TimePicker picker)
            ScheduleFix(picker);
    }

    private static void OnPickerTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is TimePicker picker)
            ScheduleFix(picker);
    }

    private static void ScheduleFix(TimePicker picker)
    {
        if (picker.XamlRoot == null)
            return;

        XamlRoot root = picker.XamlRoot;
        DispatcherQueue queue = picker.DispatcherQueue;

        queue.TryEnqueue(DispatcherQueuePriority.Low, () => ApplyFix(root));
        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(40);
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            ApplyFix(root);
            ticks++;
            if (ticks >= 12)
                timer.Stop();
        };
        timer.Start();
    }

    private static void ApplyFix(XamlRoot? root)
    {
        if (root == null)
            return;

        foreach (Popup popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root))
        {
            if (popup.Child == null)
                continue;

            if (!LooksLikeTimePickerFlyout(popup.Child))
                continue;

            WidenColumnGap(popup.Child);
            UnifyOrSeparateHighlights(popup.Child);
        }
    }

    private static bool LooksLikeTimePickerFlyout(DependencyObject root)
    {
        return FindDescendantByName(root, "FirstPickerHost") != null
            || FindDescendantByName(root, "SecondPickerHost") != null
            || FindDescendantByName(root, "HighlightRect") != null;
    }

    private static void WidenColumnGap(DependencyObject root)
    {
        if (FindDescendantByName(root, "FirstPickerSpacing") is FrameworkElement spacing)
        {
            spacing.Width = Math.Max(spacing.Width, ColumnGap);
            spacing.MinWidth = ColumnGap;
            if (spacing is Rectangle rect)
                rect.Opacity = 0;
        }

        if (FindDescendantByName(root, "FirstPickerHost") is FrameworkElement firstHost
            && VisualTreeHelper.GetParent(firstHost) is Grid hostGrid
            && hostGrid.ColumnDefinitions.Count >= 3)
        {
            ColumnDefinition gapCol = hostGrid.ColumnDefinitions[1];
            gapCol.Width = new GridLength(ColumnGap);
            gapCol.MinWidth = ColumnGap;
        }
    }

    private static void UnifyOrSeparateHighlights(DependencyObject root)
    {
        var columns = ResolvePickerColumns(root);
        FrameworkElement? sharedHighlight = FindDescendantByName(root, "HighlightRect");

        if (sharedHighlight != null)
        {
            // 横断ハイライトを1本残し、列内の別アクセント塗りを消して二重描画を防ぐ
            sharedHighlight.Opacity = 1;
            sharedHighlight.ClearValue(UIElement.ClipProperty);
            sharedHighlight.ClearValue(FrameworkElement.MaxWidthProperty);

            foreach (FrameworkElement column in columns)
                HideNestedAccentHighlights(column, except: sharedHighlight);

            // Host 外に共有ハイライトがある場合も、列内以外の重複アクセントを消す
            HideNestedAccentHighlights(root, except: sharedHighlight);
            return;
        }

        // 共有ハイライトが無い場合: 各列を厳密 Clip し対面側に余白
        for (int i = 0; i < columns.Count; i++)
        {
            FrameworkElement column = columns[i];
            double facingPad = 2;
            if (i == 0)
                column.Margin = new Thickness(0, 0, facingPad, 0);
            else if (i == 1)
                column.Margin = new Thickness(facingPad, 0, 0, 0);

            ApplyExactClip(column);
            HookSizeChanged(column);
        }
    }

    private static void HideNestedAccentHighlights(DependencyObject root, FrameworkElement except)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe)
            {
                if (!ReferenceEquals(fe, except))
                {
                    // HighlightRect と同色の Background が列ごとに重なり左端が濃くなるのを防ぐ。
                    // ReplacementColor による文字差し替えは残すため Opacity は触らない。
                    if (IsMonochromaticOverlay(fe))
                        ClearBackground(fe);
                    else if (IsAccentHighlight(fe))
                        fe.Opacity = 0;
                }

                HideNestedAccentHighlights(fe, except);
            }
            else
            {
                HideNestedAccentHighlights(child, except);
            }
        }
    }

    private static bool IsMonochromaticOverlay(FrameworkElement fe)
    {
        return fe.GetType().Name.Contains("MonochromaticOverlay", StringComparison.Ordinal);
    }

    private static void ClearBackground(FrameworkElement fe)
    {
        Brush transparent = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        if (fe is MonochromaticOverlayPresenter overlay)
        {
            overlay.Background = transparent;
            return;
        }

        switch (fe)
        {
            case Control control:
                control.Background = transparent;
                break;
            case Panel panel:
                panel.Background = transparent;
                break;
            case Border border:
                border.Background = transparent;
                break;
        }
    }

    private static bool IsAccentHighlight(FrameworkElement fe)
    {
        if (IsMonochromaticOverlay(fe))
            return false;

        string name = fe.Name ?? string.Empty;
        string typeName = fe.GetType().Name;
        if (name.Contains("Highlight", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Highlight", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Brush? fill = fe switch
        {
            Shape shape => shape.Fill,
            Border border => border.Background,
            Control control => control.Background,
            Panel panel => panel.Background,
            _ => null
        };

        if (fill is not SolidColorBrush solid)
            return false;

        Color c = solid.Color;
        return c.A > 40 && c.B > c.R + 20 && c.B > c.G;
    }

    private static List<FrameworkElement> ResolvePickerColumns(DependencyObject root)
    {
        var columns = new List<FrameworkElement>();

        if (FindDescendantByName(root, "FirstPickerHost") is FrameworkElement first)
            columns.Add(first);
        if (FindDescendantByName(root, "SecondPickerHost") is FrameworkElement second)
            columns.Add(second);

        if (columns.Count >= 2)
            return columns;

        columns.Clear();
        CollectLikelyPickerColumns(root, columns);
        return columns;
    }

    private static void HookSizeChanged(FrameworkElement element)
    {
        if (!SizeHooked.Add(element))
            return;

        element.SizeChanged += (_, _) => ApplyExactClip(element);
    }

    private static void ApplyExactClip(FrameworkElement element)
    {
        double w = element.ActualWidth;
        double h = element.ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        element.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, w, h)
        };
    }

    private static void CollectLikelyPickerColumns(DependencyObject parent, List<FrameworkElement> results)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe)
            {
                string typeName = fe.GetType().Name;
                if ((typeName.Contains("Looping", StringComparison.Ordinal)
                     || typeName.Contains("Picker", StringComparison.Ordinal))
                    && fe.ActualWidth is > 32 and < 160
                    && fe.ActualHeight > 100)
                {
                    results.Add(fe);
                }
            }

            CollectLikelyPickerColumns(child, results);
        }
    }

    private static FrameworkElement? FindDescendantByName(DependencyObject parent, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: { } childName } fe && childName == name)
                return fe;

            FrameworkElement? nested = FindDescendantByName(child, name);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
