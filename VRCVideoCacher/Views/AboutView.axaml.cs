using Avalonia.Controls;

namespace VRCVideoCacher.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        DataContext = new VRCVideoCacher.ViewModels.AboutViewModel();
    }
}
