using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class AsanaSyncPage : UserControl
{
    private bool _isInitialized;

    public AsanaSyncPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(AsanaSyncViewModel));
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_isInitialized) return;
        _isInitialized = true;

        var vm = DataContext as AsanaSyncViewModel;
        if (vm != null)
            _ = vm.InitAsync();
    }
}
