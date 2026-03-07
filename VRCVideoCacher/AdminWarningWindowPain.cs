using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace VRCVideoCacher;

public class AdminWarningWindowPain : Window
{
    public AdminWarningWindowPain(string error)
    {
        var okButton =
                new Button
                {
                    Content = "OK",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Width = 80,
                };
        okButton.Click += (_, _) => this.Close();

        Title = "VRCVideoCacher";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = error,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 20)
                },
                okButton
            }
        };
    }
}