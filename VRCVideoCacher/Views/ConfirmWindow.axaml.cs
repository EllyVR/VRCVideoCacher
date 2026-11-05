using Avalonia.Controls;
using Avalonia.Interactivity;
using Jeek.Avalonia.Localization;

namespace VRCVideoCacher.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow() : this(string.Empty, string.Empty)
    {
    }

    public ConfirmWindow(string title, string message, string? confirmText = null)
    {
        InitializeComponent();
        this.FindControl<TextBlock>("TitleTextBlock")!.Text = title;
        this.FindControl<TextBlock>("MessageTextBlock")!.Text = message;
        this.FindControl<Button>("CancelButton")!.Content = Localizer.Get("Cancel");
        this.FindControl<Button>("ConfirmButton")!.Content = confirmText ?? Localizer.Get("Delete");
    }

    /// <summary>Shows a modal yes/no dialog; resolves to true only if the danger button was clicked
    /// (closing via the window chrome counts as cancel).</summary>
    public static Task<bool> ShowAsync(Window owner, string title, string message, string? confirmText = null)
    {
        var window = new ConfirmWindow(title, message, confirmText);
        return window.ShowDialog<bool>(owner);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);
}
