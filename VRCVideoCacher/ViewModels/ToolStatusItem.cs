using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace VRCVideoCacher.ViewModels;

public enum ToolState
{
    Checking,
    Ok,
    Warning,
    Failed,
    NotApplicable,
}

/// <summary>One row in the dashboard's Required Tools panel: a tool's name, verified state, and a detail
/// line (version when healthy, a short reason otherwise). Icon and colour follow the state.</summary>
public partial class ToolStatusItem : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconKind))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private ToolState _state;

    [ObservableProperty]
    private string _detail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    private string _hint = string.Empty;

    public bool HasHint => !string.IsNullOrEmpty(Hint);

    public ToolStatusItem(string name, ToolState state = ToolState.Checking)
    {
        Name = name;
        _state = state;
    }

    public MaterialIconKind IconKind => State switch
    {
        ToolState.Ok => MaterialIconKind.CheckCircle,
        ToolState.Warning => MaterialIconKind.AlertCircle,
        ToolState.Failed => MaterialIconKind.CloseCircle,
        ToolState.NotApplicable => MaterialIconKind.MinusCircle,
        _ => MaterialIconKind.ProgressClock,
    };

    public IBrush StatusBrush => new SolidColorBrush(Color.Parse(State switch
    {
        ToolState.Ok => "#81C784",
        ToolState.Warning => "#FFB74D",
        ToolState.Failed => "#E57373",
        _ => "#888888",
    }));
}
