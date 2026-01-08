using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VRCVideoCacher.UI.Models;
using VRCVideoCacher.UI.Services;

namespace VRCVideoCacher.UI.ViewModels;

public partial class LogViewerViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _showDebug = true;

    [ObservableProperty]
    private bool _showInfo = true;

    [ObservableProperty]
    private bool _showWarning = true;

    [ObservableProperty]
    private bool _showError = true;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private int _maxLogEntries = 1000;

    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ObservableCollection<LogEntry> FilteredLogEntries { get; } = [];

    public LogViewerViewModel()
    {
        // Load buffered logs that occurred before UI was ready
        foreach (var entry in LogService.GetBufferedLogs())
        {
            LogEntries.Add(entry);
            if (ShouldShowEntry(entry))
            {
                FilteredLogEntries.Add(entry);
            }
        }

        // Subscribe to new log entries
        LogService.OnLogEntry += OnLogEntry;
    }

    private void OnLogEntry(LogEntry entry)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            LogEntries.Add(entry);

            // Trim old entries
            while (LogEntries.Count > MaxLogEntries)
            {
                LogEntries.RemoveAt(0);
            }

            // Apply filter
            if (ShouldShowEntry(entry))
            {
                FilteredLogEntries.Add(entry);
                while (FilteredLogEntries.Count > MaxLogEntries)
                {
                    FilteredLogEntries.RemoveAt(0);
                }
            }
        });
    }

    private bool ShouldShowEntry(LogEntry entry)
    {
        // Filter by level
        var show = entry.Level switch
        {
            "DBG" => ShowDebug,
            "INF" => ShowInfo,
            "WRN" => ShowWarning,
            "ERR" or "FTL" => ShowError,
            _ => true
        };

        if (!show) return false;

        // Filter by text
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var filter = FilterText.ToLowerInvariant();
            if (!entry.Message.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !entry.Source.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnShowDebugChanged(bool value) => ApplyFilter();
    partial void OnShowInfoChanged(bool value) => ApplyFilter();
    partial void OnShowWarningChanged(bool value) => ApplyFilter();
    partial void OnShowErrorChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredLogEntries.Clear();
        foreach (var entry in LogEntries)
        {
            if (ShouldShowEntry(entry))
            {
                FilteredLogEntries.Add(entry);
            }
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
        FilteredLogEntries.Clear();
    }

    [RelayCommand]
    private async Task ExportLogs()
    {
        // Export logs to file
        var logPath = Path.Combine(VRCVideoCacher.Program.DataPath, $"export_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        var lines = LogEntries.Select(e => $"[{e.Timestamp:HH:mm:ss} {e.Level} {e.Source}] {e.Message}");
        await File.WriteAllLinesAsync(logPath, lines);

        // Open file location
        if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{logPath}\"");
        }
    }
}
