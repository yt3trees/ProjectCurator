using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;
using ProjectCurator.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace ProjectCurator.Views.Pages;

public partial class SetupPage : WpfUserControl, INavigableView<SetupViewModel>
{
    public SetupViewModel ViewModel { get; }

    public SetupPage(SetupViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        ViewModel.OnOpenWorkstreamFocusInEditor = (project, filePath) =>
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.NavigateToEditorAndOpenFile(project, filePath);
        };

        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadProjectNamesAsync();

        // OutputText 変化時に自動スクロール
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SetupViewModel.OutputText))
        {
            OutputScrollViewer.ScrollToEnd();
        }
    }
}
