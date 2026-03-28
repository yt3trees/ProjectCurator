using Avalonia.Controls;
using Avalonia.Interactivity;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class GitReposPage : UserControl
{
    private bool _isInitialized;

    public GitReposPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(GitReposViewModel));
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_isInitialized) return;
        _isInitialized = true;

        var vm = DataContext as GitReposViewModel;
        if (vm != null)
            _ = vm.InitAsync();
    }
}
