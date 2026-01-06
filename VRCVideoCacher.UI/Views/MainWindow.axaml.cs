using Avalonia.Controls;
using Avalonia.Threading;
using VRCVideoCacher.UI.ViewModels;

namespace VRCVideoCacher.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnWindowOpened;
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        // Only run once
        Opened -= OnWindowOpened;

        // Check if we should show the cookie setup wizard
        // Show if: cookies are enabled, setup not completed, and cookies not already valid
        if (ConfigManager.Config.ytdlUseCookies &&
            !ConfigManager.Config.CookieSetupCompleted &&
            !VRCVideoCacher.Program.IsCookiesEnabledAndValid())
        {
            // Delay slightly to let the main window fully render
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(500);
                await ShowCookieSetupDialog();
            });
        }
    }

    private async System.Threading.Tasks.Task ShowCookieSetupDialog()
    {
        var viewModel = new CookieSetupViewModel();
        var window = new CookieSetupWindow
        {
            DataContext = viewModel
        };

        viewModel.RequestClose += () => window.Close();

        await window.ShowDialog(this);
    }
}
