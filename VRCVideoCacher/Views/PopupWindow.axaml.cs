using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VRCVideoCacher.Views;

public partial class PopupWindow : Window
{
    public PopupWindow() : this(string.Empty)
    {
    }

    public PopupWindow(string error)
    {
        InitializeComponent();
        ErrorTextBlock.Text = error;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
