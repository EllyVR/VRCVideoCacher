using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jeek.Avalonia.Localization;
using VRCVideoCacher.Models;
using VRCVideoCacher.Views;

namespace VRCVideoCacher.ViewModels;

public partial class RuleEntryViewModel : ObservableObject
{
    public UriRule Rule { get; }
    public Action? OnEnabledChanged;

    public string Name => Rule.Name;
    public string Pattern => Rule.Pattern;
    public RuleAction Action => Rule.Action;
    public string ActionSummary => Rule.GetActionSummary();

    [ObservableProperty]
    private bool _isMatched;

    public string RowBackground => IsMatched ? "#1E4D2B" : "Transparent";

    partial void OnIsMatchedChanged(bool value)
    {
        OnPropertyChanged(nameof(RowBackground));
    }

    public bool Enabled
    {
        get => Rule.Enabled;
        set
        {
            if (Rule.Enabled != value)
            {
                Rule.Enabled = value;
                OnPropertyChanged();
                OnEnabledChanged?.Invoke();
            }
        }
    }

    public RuleEntryViewModel(UriRule rule)
    {
        Rule = rule;
    }

    public void RefreshProperties()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Pattern));
        OnPropertyChanged(nameof(Action));
        OnPropertyChanged(nameof(ActionSummary));
        OnPropertyChanged(nameof(Enabled));
    }
}

public partial class RulesViewModel : ViewModelBase
{
    private bool _isLoading;

    public ObservableCollection<RuleEntryViewModel> Rules { get; } = [];

    [ObservableProperty]
    private string _testUrl = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessageColor = "#81C784";

    [ObservableProperty]
    private bool _hasChanges;

    public RulesViewModel()
    {
        ConfigManager.OnConfigChanged += LoadFromConfig;
        LoadFromConfig();
    }

    private RuleEntryViewModel CreateEntry(UriRule rule)
    {
        var entry = new RuleEntryViewModel(rule);
        entry.OnEnabledChanged += () =>
        {
            if (_isLoading) return;
            EvaluateTestUrl();
            SetHasChanges();
        };
        return entry;
    }

    partial void OnTestUrlChanged(string value)
    {
        EvaluateTestUrl();
    }

    public void EvaluateTestUrl()
    {
        var url = TestUrl?.Trim();
        bool foundMatch = false;

        foreach (var entry in Rules)
        {
            if (!foundMatch && !string.IsNullOrWhiteSpace(url) && entry.Enabled && !string.IsNullOrWhiteSpace(entry.Pattern))
            {
                try
                {
                    var regex = new Regex(entry.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
                    if (regex.IsMatch(url))
                    {
                        entry.IsMatched = true;
                        foundMatch = true;
                        continue;
                    }
                }
                catch
                {
                    // Invalid regex in rule
                }
            }
            entry.IsMatched = false;
        }
    }

    public void LoadFromConfig()
    {
        _isLoading = true;
        Rules.Clear();

        var configRules = ConfigManager.Config.UriRules;
        if (configRules == null || configRules.Count == 0)
        {
            configRules = ConfigModel.GetDefaultRules();
            ConfigManager.Config.UriRules = configRules;
        }

        foreach (var rule in configRules)
        {
            Rules.Add(CreateEntry(rule));
        }

        HasChanges = false;
        StatusMessage = string.Empty;
        _isLoading = false;
        EvaluateTestUrl();
    }

    private void SetHasChanges()
    {
        if (_isLoading) return;
        HasChanges = true;
        StatusMessage = Localizer.Get("SettingsUnsavedChanges");
        StatusMessageColor = "#FFB74D";
    }

    public void SaveToConfig()
    {
        ConfigManager.Config.UriRules = Rules.Select(r => r.Rule).ToList();
        ConfigManager.TrySaveConfig();
        HasChanges = false;
        StatusMessage = Localizer.Get("SettingsSaved");
        StatusMessageColor = "#81C784";
        EvaluateTestUrl();
    }

    public async Task<bool> CheckUnsavedChangesAsync(Window? parentWindow)
    {
        if (!HasChanges) return true; // OK to proceed

        var confirmVm = new ConfirmUnsavedViewModel();
        var dialog = new ConfirmUnsavedWindow { DataContext = confirmVm };

        UnsavedChangesResult result = UnsavedChangesResult.Cancel;
        confirmVm.CloseRequested += (res) =>
        {
            result = res;
            dialog.Close();
        };

        if (parentWindow != null)
        {
            await dialog.ShowDialog(parentWindow);
        }

        switch (result)
        {
            case UnsavedChangesResult.Save:
                SaveToConfig();
                return true;

            case UnsavedChangesResult.Discard:
                LoadFromConfig();
                return true;

            case UnsavedChangesResult.Cancel:
            default:
                return false;
        }
    }

    [RelayCommand]
    private async Task AddRule()
    {
        var editVm = new EditRuleViewModel(null);
        var window = new EditRuleWindow(editVm);

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var parentWindow = lifetime?.MainWindow;

        var result = parentWindow != null
            ? await window.ShowDialog<bool>(parentWindow)
            : false;

        if (result)
        {
            var newEntry = CreateEntry(editVm.RuleResult);
            Rules.Add(newEntry);
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private async Task EditRule(RuleEntryViewModel? entry)
    {
        if (entry == null) return;

        var editVm = new EditRuleViewModel(entry.Rule);
        var window = new EditRuleWindow(editVm);

        var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var parentWindow = lifetime?.MainWindow;

        var result = parentWindow != null
            ? await window.ShowDialog<bool>(parentWindow)
            : false;

        if (result)
        {
            entry.Rule.Name = editVm.RuleResult.Name;
            entry.Rule.Pattern = editVm.RuleResult.Pattern;
            entry.Rule.Action = editVm.RuleResult.Action;
            entry.Rule.Enabled = editVm.RuleResult.Enabled;
            entry.Rule.MaxResolution = editVm.RuleResult.MaxResolution;
            entry.Rule.RedirectTarget = editVm.RuleResult.RedirectTarget;

            entry.RefreshProperties();
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private void MoveRuleUp(RuleEntryViewModel? entry)
    {
        if (entry == null) return;
        var index = Rules.IndexOf(entry);
        if (index > 0)
        {
            Rules.Move(index, index - 1);
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private void MoveRuleDown(RuleEntryViewModel? entry)
    {
        if (entry == null) return;
        var index = Rules.IndexOf(entry);
        if (index >= 0 && index < Rules.Count - 1)
        {
            Rules.Move(index, index + 1);
            EvaluateTestUrl();
            SetHasChanges();
        }
    }

    [RelayCommand]
    private void DeleteRule(RuleEntryViewModel? entry)
    {
        if (entry == null) return;
        Rules.Remove(entry);
        EvaluateTestUrl();
        SetHasChanges();
    }

    [RelayCommand]
    private void ResetDefaultRules()
    {
        Rules.Clear();
        foreach (var rule in ConfigModel.GetDefaultRules())
        {
            Rules.Add(CreateEntry(rule));
        }
        EvaluateTestUrl();
        SetHasChanges();
    }

    [RelayCommand]
    private void SaveRules()
    {
        SaveToConfig();
    }
}
