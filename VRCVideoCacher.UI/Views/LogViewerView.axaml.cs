using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using VRCVideoCacher.UI.ViewModels;

namespace VRCVideoCacher.UI.Views;

public partial class LogViewerView : UserControl
{
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

        // Scroll to bottom when view becomes visible
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.NewValue is true)
        {
            ScrollToBottom();
        }
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is LogViewerViewModel { AutoScroll: true } && e.Action == NotifyCollectionChangedAction.Add)
        {
            ScrollToBottom();
        }
    }

    private void ScrollToBottom()
    {
        if (LogListBox.ItemCount > 0)
        {
            LogListBox.ScrollIntoView(LogListBox.ItemCount - 1);
        }
    }
}
