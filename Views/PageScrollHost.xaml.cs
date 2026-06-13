using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App1.Views
{
    public sealed partial class PageScrollHost : ContentControl
    {
        private const double ContentHorizontalMargin = 48;
        private const double ContentMaxWidth = 1000;

        private Grid? _layoutRoot;
        private Grid? _contentHost;
        private bool _widthUpdateScheduled;

        public PageScrollHost()
        {
            InitializeComponent();
            Loaded += (_, _) => ScheduleContentWidthUpdate();
        }

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (_layoutRoot != null)
                _layoutRoot.SizeChanged -= LayoutRoot_SizeChanged;

            _layoutRoot = GetTemplateChild("LayoutRoot") as Grid;
            _contentHost = GetTemplateChild("ContentHost") as Grid;

            if (_layoutRoot != null)
                _layoutRoot.SizeChanged += LayoutRoot_SizeChanged;

            ScheduleContentWidthUpdate();
        }

        private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleContentWidthUpdate(e.NewSize.Width);
        }

        private void ScheduleContentWidthUpdate(double? layoutWidth = null)
        {
            if (_widthUpdateScheduled)
                return;

            _widthUpdateScheduled = true;
            double? capturedWidth = layoutWidth;

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _widthUpdateScheduled = false;
                UpdateContentWidth(capturedWidth);
            });
        }

        private void UpdateContentWidth(double? layoutWidth = null)
        {
            if (_contentHost == null)
                return;

            double availableWidth = layoutWidth ?? _layoutRoot?.ActualWidth ?? 0;
            if (availableWidth <= 0)
                return;

            // 測定中に Width を直接変更すると MeasureOverride が失敗するため MaxWidth を使う。
            _contentHost.MaxWidth = System.Math.Max(0, System.Math.Min(availableWidth - ContentHorizontalMargin, ContentMaxWidth));
        }
    }
}
