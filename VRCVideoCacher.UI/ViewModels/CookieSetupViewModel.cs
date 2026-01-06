using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VRCVideoCacher.UI.ViewModels;

public partial class CookieSetupViewModel : ViewModelBase
{
    private const string ChromeExtensionUrl = "https://chromewebstore.google.com/detail/vrcvideocacher-cookies-ex/kfgelknbegappcajiflgfbjbdpbpokge";
    private const string FirefoxExtensionUrl = "https://addons.mozilla.org/en-US/firefox/addon/vrcvideocachercookiesexporter";

    public event Action? RequestClose;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private bool _isChrome;

    [ObservableProperty]
    private bool _cookiesReceived;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;

    public bool CanGoBack => CurrentStep > 1 && CurrentStep < 4;
    public bool CanGoNext => CurrentStep switch
    {
        1 => false, // Must select browser
        2 => true,
        3 => CookiesReceived,
        4 => true,
        _ => false
    };

    public string NextButtonText => CurrentStep == 4 ? "Done" : "Next";

    public string ExtensionStoreButtonText => IsChrome
        ? "Open Chrome Web Store"
        : "Open Firefox Add-ons";

    public string CookieStatusText => CookiesReceived
        ? "Cookies received!"
        : "Waiting for cookies...";

    public string CookieStatusIcon => CookiesReceived
        ? "CheckCircle"
        : "TimerSand";

    public IBrush CookieStatusColor => CookiesReceived
        ? new SolidColorBrush(Color.Parse("#81C784"))
        : new SolidColorBrush(Color.Parse("#FFB74D"));

    public CookieSetupViewModel()
    {
        // Subscribe to cookies updated event
        VRCVideoCacher.Program.OnCookiesUpdated += OnCookiesUpdated;

        // Check if cookies are already valid
        CookiesReceived = VRCVideoCacher.Program.IsCookiesEnabledAndValid();
    }

    private void OnCookiesUpdated()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CookiesReceived = VRCVideoCacher.Program.IsCookiesEnabledAndValid();
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CookieStatusText));
            OnPropertyChanged(nameof(CookieStatusIcon));
            OnPropertyChanged(nameof(CookieStatusColor));

            // Auto-advance to step 4 when cookies are received
            if (CookiesReceived && CurrentStep == 3)
            {
                CurrentStep = 4;
                UpdateStepProperties();
            }
        });
    }

    partial void OnCurrentStepChanged(int value)
    {
        UpdateStepProperties();
    }

    private void UpdateStepProperties()
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(NextButtonText));
    }

    [RelayCommand]
    private void SelectChrome()
    {
        IsChrome = true;
        CurrentStep = 2;
        OnPropertyChanged(nameof(ExtensionStoreButtonText));
    }

    [RelayCommand]
    private void SelectFirefox()
    {
        IsChrome = false;
        CurrentStep = 2;
        OnPropertyChanged(nameof(ExtensionStoreButtonText));
    }

    [RelayCommand]
    private void OpenExtensionStore()
    {
        var url = IsChrome ? ChromeExtensionUrl : FirefoxExtensionUrl;
        OpenUrl(url);
    }

    [RelayCommand]
    private void OpenYouTube()
    {
        OpenUrl("https://www.youtube.com");
    }

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep == 4)
        {
            // Mark setup as completed and save config
            ConfigManager.Config.CookieSetupCompleted = true;
            ConfigManager.TrySaveConfig();

            // Done - close the window
            VRCVideoCacher.Program.OnCookiesUpdated -= OnCookiesUpdated;
            RequestClose?.Invoke();
            return;
        }

        if (CurrentStep < 4)
        {
            CurrentStep++;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        VRCVideoCacher.Program.OnCookiesUpdated -= OnCookiesUpdated;
        RequestClose?.Invoke();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { /* Ignore errors */ }
    }
}
