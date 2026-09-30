using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using Windows.Foundation;
using Windows.UI;

namespace App1.Views;

/// <summary>
/// TimePicker フライアウトの時・分選択ハイライトが境目で重なり暗い筋になるのを防ぐ。
/// 列間隔を広げ、各列とハイライトを Clip して隣列へはみ出さないようにする。
/// </summary>
internal static class TimePickerFlyoutFixHelper
{
    private const double ColumnGap = 14;
    private const double ClipInset = 1;

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
            SeparateAndClipColumns(popup.Child);
            ClipHighlightElements(popup.Child);
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
                rect.Opacity = 0; // 区切り線はギャップで足りる。重なりの視覚ノイズを減らす
        }

        // PickerHost を含む Grid の中間 Auto 列を広げる
        if (FindDescendantByName(root, "FirstPickerHost") is FrameworkElement firstHost
            && VisualTreeHelper.GetParent(firstHost) is Grid hostGrid
            && hostGrid.ColumnDefinitions.Count >= 3)
        {
            // 典型: Col0=時*, Col1=間隔 Auto, Col2=分*
            ColumnDefinition gapCol = hostGrid.ColumnDefinitions[1];
            gapCol.Width = new GridLength(ColumnGap);
            gapCol.MinWidth = ColumnGap;
        }
    }

    private static void SeparateAndClipColumns(DependencyObject root)
    {
        var columns = ResolvePickerColumns(root);
        if (columns.Count < 2)
            return;

        double halfGap = ColumnGap / 2.0;
            columns[0].Margin = new Thickness(0, 0, halfGap, 0);
            columns[1].Margin = new Thickness(halfGap, 0, 0, 0);

            for (int i = 0; i < Math.Min(2, columns.Count); i++)
            {
                FrameworkElement column = columns[i];
                ApplyInsetClip(column);
                HookSizeChanged(column);
                ClipHighlightDescendants(column);
            }

            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BlueShift",
                    "timepicker-fix.txt");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.AppendAllText(
                    path,
                    $"{DateTime.Now:HH:mm:ss.fff} columns={columns.Count} gap={ColumnGap} w0={columns[0].ActualWidth:F1} w1={columns[1].ActualWidth:F1}{Environment.NewLine}");
            }
            catch
            {
                // ignore
            }
        }

    private static void ClipHighlightElements(DependencyObject root)
    {
        if (FindDescendantByName(root, "HighlightRect") is FrameworkElement sharedHighlight)
        {
            // 旧テンプレの横断ハイライトは列またぎで良いが、幅は親に合わせる
            ApplyInsetClip(sharedHighlight);
            HookSizeChanged(sharedHighlight);
        }

        ClipHighlightDescendants(root);
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
                bool isHighlight = name.Contains("Highlight", StringComparison.OrdinalIgnoreCase)
                    || typeName.Contains("Highlight", StringComparison.OrdinalIgnoreCase)
                    || IsAccentFilledShape(fe);

                if (isHighlight)
                {
                    // 端で隣列と重ならないよう少し内側に縮める
                    if (fe.ActualWidth > ColumnGap)
                        fe.Margin = new Thickness(ClipInset, fe.Margin.Top, ClipInset, fe.Margin.Bottom);

                    ApplyInsetClip(fe);
                    HookSizeChanged(fe);

                    if (fe.Parent is FrameworkElement parent && parent.ActualWidth > 0)
                    {
                        fe.MaxWidth = Math.Max(0, parent.ActualWidth - ClipInset * 2);
                        ApplyInsetClip(parent);
                    }
                }
            }

            ClipHighlightDescendants(child);
        }
    }

    private static bool IsAccentFilledShape(FrameworkElement fe)
    {
        Brush? fill = fe switch
        {
            Shape shape => shape.Fill,
            Border border => border.Background,
            _ => null
        };

        if (fill is not SolidColorBrush solid)
            return false;

        Color c = solid.Color;
        // アクセント青系の半透明ハイライトを想定
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

        element.SizeChanged += (_, _) => ApplyInsetClip(element);
    }

    private static void ApplyInsetClip(FrameworkElement element)
    {
        double w = element.ActualWidth;
        double h = element.ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        double inset = Math.Min(ClipInset, w / 4);
        element.Clip = new RectangleGeometry
        {
            Rect = new Rect(inset, 0, Math.Max(0, w - inset * 2), h)
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
