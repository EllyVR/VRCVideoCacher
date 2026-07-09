using Avalonia.Controls;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Views;

public partial class AboutView : UserControl
{
    private const string GithubUrl = "https://github.com/EllyVR/VRCVideoCacher";
    private const string DiscordUrl = "https://discord.gg/z5kVNkmQuS";
    private const string SteamUrl = "https://store.steampowered.com/app/4296960/";

    public AboutView()
    {
        InitializeComponent();
        DataContext = new VRCVideoCacher.ViewModels.AboutViewModel();
    }

    private void OnDiscordClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenUrl.Open(DiscordUrl);
    }

    private void OnGitHubClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenUrl.Open(GithubUrl);
    }

    private void OnSteamClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenUrl.Open(SteamUrl);
    }

    private void OnGitHubIssueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenUrl.Open($"{GithubUrl}/issues");
    }

    private void OnDiscordIssueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OpenUrl.Open(DiscordUrl);
    }
}
