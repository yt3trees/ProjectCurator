using Avalonia.Controls;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class DashboardPage : UserControl
{
    public DashboardPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(DashboardViewModel));
    }
}
