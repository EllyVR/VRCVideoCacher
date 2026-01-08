using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using VRCVideoCacher.UI.ViewModels;
using VRCVideoCacher.UI.Views;

namespace VRCVideoCacher.UI;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "DataValidators is safe to access at startup")]
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit
            BindingPlugins.DataValidators.RemoveAt(0);

            _mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };

            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Set up tray icon
            SetupTrayIcon(desktop);

            // Handle window closing - minimize to tray instead
            _mainWindow.Closing += (_, e) =>
            {
                if (!_isExiting)
                {
                    e.Cancel = true;
                    _mainWindow.Hide();
                }
            };

            // Check for --minimized flag
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("--minimized"))
            {
                _mainWindow.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private bool _isExiting;

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var showItem = new NativeMenuItem("Show");
        showItem.Click += (_, _) => ShowMainWindow();

        var openCacheItem = new NativeMenuItem("Open Cache Folder");
        openCacheItem.Click += (_, _) => OpenCacheFolder();

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            _isExiting = true;
            desktop.Shutdown();
        };

        var menu = new NativeMenu
        {
            showItem,
            new NativeMenuItemSeparator(),
            openCacheItem,
            new NativeMenuItemSeparator(),
            exitItem
        };

        _trayIcon = new TrayIcon
        {
            ToolTipText = "VRCVideoCacher",
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://VRCVideoCacher.UI/Assets/icon.ico"))),
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    private void OpenCacheFolder()
    {
        var cachePath = CacheManager.CachePath;
        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start("explorer.exe", cachePath);
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Process.Start("xdg-open", cachePath);
        }
    }
}
