using Avalonia.Controls;
using Avalonia.Interactivity;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Views;

public partial class CacheBrowserView : UserControl
{
    public CacheBrowserView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as CacheBrowserViewModel)?.RefreshCacheCommand.Execute(null);
    }
}
