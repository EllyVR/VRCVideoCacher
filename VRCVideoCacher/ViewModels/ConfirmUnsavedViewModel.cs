using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VRCVideoCacher.ViewModels;

public enum UnsavedChangesResult
{
    Save,
    Discard,
    Cancel
}

public partial class ConfirmUnsavedViewModel : ObservableObject
{
    public event Action<UnsavedChangesResult>? CloseRequested;

    [RelayCommand]
    private void Save() => CloseRequested?.Invoke(UnsavedChangesResult.Save);

    [RelayCommand]
    private void Discard() => CloseRequested?.Invoke(UnsavedChangesResult.Discard);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(UnsavedChangesResult.Cancel);
}
