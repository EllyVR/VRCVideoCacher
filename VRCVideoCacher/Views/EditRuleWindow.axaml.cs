using Avalonia.Controls;
using VRCVideoCacher.ViewModels;

namespace VRCVideoCacher.Views;

public partial class EditRuleWindow : Window
{
    public EditRuleWindow()
    {
        InitializeComponent();
    }

    public EditRuleWindow(EditRuleViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (success) => Close(success);
    }
}
