using Avalonia.Controls;

namespace VRCVideoCacher.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        DataContext = new VRCVideoCacher.ViewModels.AboutViewModel();
    }
    
    private void OnDiscordClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenUrl("https://discord.gg/t6x6p6Tzs");
    }
    
    private void OnGitHubClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenUrl("https://github.com/EllyVR/VRCVideoCacher");
    }
    
    private void OnSteamClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenUrl("https://store.steampowered.com/app/4296960/VRCVideoCacher/");
    }
    
    private void OpenUrl(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch { /* Optionally handle errors */ }
    }
}
