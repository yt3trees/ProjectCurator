using System.Windows;
using Wpf.Ui.Controls;
using ProjectCurator.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace ProjectCurator.Views.Pages;

public partial class GitReposPage : WpfUserControl, INavigableView<GitReposViewModel>
{
    public GitReposViewModel ViewModel { get; }

    public GitReposPage(GitReposViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitAsync();
    }
}
