using Avalonia.Controls;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class GitReposPage : UserControl
{
    public GitReposPage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(GitReposViewModel));
    }
}
