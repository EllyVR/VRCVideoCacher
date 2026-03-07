using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
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
            if (AdminCheck.ShouldShowAdminWarning())
            {
                var adminWindow = new PopupWindow(AdminCheck.AdminWarningMessage);
                desktop.MainWindow = adminWindow;
                adminWindow.Closed += (_, _) => desktop.Shutdown();
                adminWindow.Show();
                return;
            }

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
    private NativeMenuItem? _showItem;
    private NativeMenuItem? _openCacheItem;
    private NativeMenuItem? _exitItem;

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
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
