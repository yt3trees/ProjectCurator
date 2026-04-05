using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Curia.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Curia.Views.Pages;

public partial class AsanaSyncPage : WpfUserControl, INavigableView<AsanaSyncViewModel>
{
    public AsanaSyncViewModel ViewModel { get; }

    public AsanaSyncPage(AsanaSyncViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitAsync();
    }

    private void OnOutputTextChanged(object sender, TextChangedEventArgs e)
    {
        OutputTextBox.ScrollToEnd();
    }
}
