using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace App1.Views;

/// <summary>
/// TimePicker フライアウトの時・分選択ハイライトが隣列にはみ出さないよう、列を Clip する。
/// </summary>
internal static class TimePickerFlyoutFixHelper
{
    private static readonly HashSet<TimePicker> Attached = [];
    private static readonly HashSet<FrameworkElement> SizeHooked = [];
    private static readonly Thickness FirstHostMargin = new(0, 0, 10, 0);
    private static readonly Thickness SecondHostMargin = new(10, 0, 0, 0);

    public static void Attach(params TimePicker[] pickers)
    {
        foreach (TimePicker picker in pickers)
        {
            if (!Attached.Add(picker))
                continue;

            picker.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPickerPointerPressed), true);
            picker.AddHandler(UIElement.TappedEvent, new TappedEventHandler(OnPickerTapped), true);
        }
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
        timer.Interval = TimeSpan.FromMilliseconds(50);
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            ApplyFix(root);
            ticks++;
            if (ticks >= 8)
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

            var columns = ResolvePickerColumns(popup.Child);
            if (columns.Count < 2)
                continue;

            columns[0].Margin = FirstHostMargin;
            columns[1].Margin = SecondHostMargin;

            for (int i = 0; i < Math.Min(2, columns.Count); i++)
            {
                FrameworkElement column = columns[i];
                ApplyClip(column);
                HookSizeChanged(column);
                ClipHighlightDescendants(column);
            }
        }
    }

    private static void ClipHighlightDescendants(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe)
            {
                string name = fe.Name ?? string.Empty;
                string typeName = fe.GetType().Name;
                if (name.Contains("Highlight", StringComparison.OrdinalIgnoreCase)
                    || typeName.Contains("Highlight", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyClip(fe);
                    HookSizeChanged(fe);
                    if (fe.Parent is FrameworkElement parent && parent.ActualWidth > 0)
                    {
                        fe.MaxWidth = parent.ActualWidth;
                        ApplyClip(parent);
                    }
                }
            }

            ClipHighlightDescendants(child);
        }
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

        element.SizeChanged += (_, _) => ApplyClip(element);
    }

    private static void ApplyClip(FrameworkElement element)
    {
        double w = element.ActualWidth;
        double h = element.ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        // ハイライトが隣列へ描画されないよう、列境界で切り取る
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
