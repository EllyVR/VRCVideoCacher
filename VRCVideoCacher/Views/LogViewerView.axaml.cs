using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Views;

public partial class LogViewerView : UserControl
{
    private ScrollViewer? _scrollViewer;

    public LogViewerView()
    {
        InitializeComponent();

        // Subscribe to collection changes for auto-scroll
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LogViewerViewModel vm)
            {
                vm.FilteredLogEntries.CollectionChanged += OnLogEntriesChanged;
            }
        };

        Loaded += (_, _) =>
        {
            _scrollViewer ??= LogListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

            if (DataContext is LogViewerViewModel { AutoScroll: true })
                ScrollToBottomDeferred();
        };
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is LogViewerViewModel { AutoScroll: true } && e.Action == NotifyCollectionChangedAction.Add)
        {
            ScrollToBottomDeferred();
        }
    }

    private void ScrollToBottomDeferred() => Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Render);

    private void ScrollToBottom()
    {
        if (_scrollViewer == null) return;
        var maxOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        _scrollViewer.Offset = new(_scrollViewer.Offset.X, maxOffset);
    }
}