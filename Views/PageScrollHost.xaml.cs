using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App1.Views
{
    public sealed partial class PageScrollHost : ContentControl
    {
        private ScrollViewer? _scrollViewer;
        private FrameworkElement? _contentRoot;

        public PageScrollHost()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            SizeChanged += (_, __) => UpdateScrollability();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
            if (_scrollViewer != null)
                _scrollViewer.SizeChanged += (_, __) => UpdateScrollability();

            WatchContentRoot(Content as FrameworkElement);
            UpdateScrollability();
        }

        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);

            if (oldContent is FrameworkElement oldRoot)
                UnwatchContentRoot(oldRoot);

            WatchContentRoot(newContent as FrameworkElement);
            UpdateScrollability();
        }

        private void WatchContentRoot(FrameworkElement? root)
        {
            if (root == null)
                return;

            _contentRoot = root;
            root.SizeChanged += ContentRoot_SizeChanged;
            root.Loaded += ContentRoot_Loaded;
        }

        private void UnwatchContentRoot(FrameworkElement root)
        {
            root.SizeChanged -= ContentRoot_SizeChanged;
            root.Loaded -= ContentRoot_Loaded;

            if (ReferenceEquals(_contentRoot, root))
                _contentRoot = null;
        }

        private void ContentRoot_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateScrollability();
        }

        private void ContentRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateScrollability();
        }

        private void UpdateScrollability()
        {
            if (_scrollViewer == null)
                return;

            _scrollViewer.UpdateLayout();

            bool needsScroll = _scrollViewer.ExtentHeight > _scrollViewer.ViewportHeight + 0.5;
            _scrollViewer.VerticalScrollBarVisibility = needsScroll
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;

            if (!needsScroll && _scrollViewer.VerticalOffset > 0)
                _scrollViewer.ChangeView(null, 0, null, disableAnimation: true);
        }
    }
}
