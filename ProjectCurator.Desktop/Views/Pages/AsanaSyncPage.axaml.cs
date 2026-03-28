using Avalonia.Controls;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class AsanaSyncPage : UserControl
{
    public AsanaSyncPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(AsanaSyncViewModel));
    }
}
