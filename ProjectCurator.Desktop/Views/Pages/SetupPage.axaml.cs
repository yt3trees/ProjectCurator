using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class SetupPage : UserControl
{
    private bool _isInitialized;

    public SetupPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(SetupViewModel));
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_isInitialized) return;
        _isInitialized = true;

        var vm = DataContext as SetupViewModel;
        if (vm != null)
            _ = vm.LoadProjectNamesAsync();
    }
}
