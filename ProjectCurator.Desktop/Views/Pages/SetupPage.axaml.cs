using Avalonia.Controls;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class SetupPage : UserControl
{
    public SetupPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(SetupViewModel));
    }
}
