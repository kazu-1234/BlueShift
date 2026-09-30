using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace App1.Views;

/// <summary>
/// TimePicker フライアウトの時・分選択ハイライトが中央で重ならないよう列間に隙間を入れる。
/// </summary>
internal static class TimePickerFlyoutFixHelper
{
    private static readonly HashSet<TimePicker> Attached = [];
    private static readonly Thickness FirstHostMargin = new(0, 0, 6, 0);
    private static readonly Thickness SecondHostMargin = new(6, 0, 0, 0);

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

        // フライアウト生成・アニメーション後に複数回適用
        queue.TryEnqueue(DispatcherQueuePriority.Low, () => ApplyFix(root));
        var timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(80);
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            ApplyFix(root);
            ticks++;
            if (ticks >= 5)
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

            if (FindDescendantByName(popup.Child, "FirstPickerHost") is FrameworkElement first)
                first.Margin = FirstHostMargin;

            if (FindDescendantByName(popup.Child, "SecondPickerHost") is FrameworkElement second)
                second.Margin = SecondHostMargin;

            // 名前が取れない場合は隣接する選択列らしき要素に余白
            if (FindDescendantByName(popup.Child, "FirstPickerHost") == null)
                ApplyFallbackSpacing(popup.Child);
        }
    }

    private static void ApplyFallbackSpacing(DependencyObject root)
    {
        var columns = new List<FrameworkElement>();
        CollectLikelyPickerColumns(root, columns);
        if (columns.Count < 2)
            return;

        columns[0].Margin = FirstHostMargin;
        columns[1].Margin = SecondHostMargin;
    }

    private static void CollectLikelyPickerColumns(DependencyObject parent, List<FrameworkElement> results)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe
                && fe.ActualWidth is > 40 and < 120
                && fe.ActualHeight > 120
                && fe.GetType().Name.Contains("Looping", StringComparison.Ordinal))
            {
                results.Add(fe);
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
