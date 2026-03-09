using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CodingSeb.Localization;
using CodingSeb.Localization.Loaders;
using Newtonsoft.Json.Linq;
using VRCVideoCacher.Utils;
using VRCVideoCacher.ViewModels;
using VRCVideoCacher.Views;

namespace VRCVideoCacher;

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
        InitializeLocalization();

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

            // Handle window closing - minimize to tray instead, but allow OS/programmatic closes
            _mainWindow.Closing += (_, e) =>
            {
                if (!ConfigManager.Config.CloseToTray || _isExiting || e.IsProgrammatic)
                    return;

                e.Cancel = true;
                _mainWindow.Hide();
            };

            // Allow the app to exit cleanly on OS shutdown/logoff
            desktop.ShutdownRequested += (_, _) =>
            {
                _isExiting = true;
                _trayIcon?.Dispose();
                _trayIcon = null;
            };
            
            // Check for --minimized flag
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("--minimized"))
            {
                _mainWindow.Show();
            }

            if (AdminCheck.ShouldShowAdminWarning())
            {
                var adminWindow = new PopupWindow(AdminCheck.AdminWarningMessage);
                _ = adminWindow.ShowDialog(_mainWindow);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeLocalization()
    {
        LoadEmbeddedLanguageFiles();

        var configLang = ConfigManager.Config.Language;
        var lang = string.IsNullOrEmpty(configLang) ? GetSystemLanguage() : configLang;
        Loc.Instance.CurrentLanguage = lang;
    }

    /// <summary>
    /// Loads all embedded *.loc.json language files from the Languages/ folder baked into the assembly.
    /// Each resource is named VRCVideoCacher.Languages.{langId}.loc.json and contains a flat
    /// JSON object {"Key": "Translated value"} for that language.
    /// </summary>
    private static void LoadEmbeddedLanguageFiles()
    {
        const string prefix = "VRCVideoCacher.Languages.";
        const string suffix = ".loc.json";

        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(prefix) && r.EndsWith(suffix));

        foreach (var resourceName in resources)
        {
            var langId = resourceName[prefix.Length..^suffix.Length];

            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName)!;
                using var reader = new StreamReader(stream);
                var json = JObject.Parse(reader.ReadToEnd());
                foreach (var prop in json.Properties())
                {
                    LocalizationLoader.Instance.AddTranslation(prop.Name, langId, prop.Value?.ToString() ?? prop.Name);
                }
            }
            catch
            {
                // Skip malformed resources — non-fatal.
            }
        }
    }

    private static string GetSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Loc.Instance.AvailableLanguages.Contains(culture) ? culture : "en";
    }

    private bool _isExiting;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private NativeMenuItem? _showItem;
    private NativeMenuItem? _openCacheItem;
    private NativeMenuItem? _exitItem;

    // Win32 message constants for close interception
    private const uint WmClose = 0x0010;
    private const uint WmSysCommand = 0x0112;
    private const int ScClose = 0xF060;

    private IntPtr Win32WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // User clicked the title-bar X button (generates SC_CLOSE before WM_CLOSE).
        // Marking as handled suppresses the subsequent WM_CLOSE, so the Closing
        // event never fires for a normal user-initiated close on Windows.
        if (ConfigManager.Config.CloseToTray &&
            msg == WmSysCommand &&
            (wParam.ToInt32() & 0xFFF0) == ScClose)
        {
            _mainWindow?.Hide();
            handled = true;
            return IntPtr.Zero;
        }

        // Raw WM_CLOSE arriving here means it came from an external source
        // (taskkill, Task Manager) — a user close via SC_CLOSE would have been
        // caught above and never reached this point.
        if (msg == WmClose && !_isExiting)
        {
            _isExiting = true;
            _trayIcon?.Dispose();
            _trayIcon = null;
            _desktop?.Shutdown();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;

        // On Windows, hook WndProc to distinguish a user clicking X (SC_CLOSE)
        // from an external kill signal (raw WM_CLOSE from taskkill/Task Manager).
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWndProcHookCallback(_mainWindow!, Win32WndProc);

        // On Linux, SIGTERM bypasses the Closing event entirely.  Intercept it
        // and route through desktop.Shutdown() so the tray icon is disposed and
        // Avalonia state is cleaned up properly before the process exits.
        if (OperatingSystem.IsLinux())
        {
            PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
            {
                ctx.Cancel = true; // suppress default immediate-kill behaviour
                Dispatcher.UIThread.Post(() =>
                {
                    _isExiting = true;
                    _trayIcon?.Dispose();
                    _trayIcon = null;
                    _desktop?.Shutdown();
                });
            });
        }

        _showItem = new NativeMenuItem(Loc.Tr("TrayShow"));
        _showItem.Click += (_, _) => ShowMainWindow();

        _openCacheItem = new NativeMenuItem(Loc.Tr("TrayOpenCacheFolder"));
        _openCacheItem.Click += (_, _) => OpenCacheFolder();

        _exitItem = new NativeMenuItem(Loc.Tr("TrayExit"));
        _exitItem.Click += (_, _) =>
        {
            _isExiting = true;
            desktop.Shutdown();
        };
        
        Loc.Instance.CurrentLanguageChanged += (_, _) =>
        {
            if (_showItem != null) _showItem.Header = Loc.Tr("TrayShow");
            if (_openCacheItem != null) _openCacheItem.Header = Loc.Tr("TrayOpenCacheFolder");
            if (_exitItem != null) _exitItem.Header = Loc.Tr("TrayExit");
        };

        var menu = new NativeMenu
        {
            _showItem,
            new NativeMenuItemSeparator(),
            _openCacheItem,
            new NativeMenuItemSeparator(),
            _exitItem
        };

        _trayIcon = new TrayIcon
        {
            ToolTipText = "VRCVideoCacher",
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://VRCVideoCacher/Assets/icon.ico"))),
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
