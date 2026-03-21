using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using ProjectCurator.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace ProjectCurator.Views.Pages;

public partial class SettingsPage : WpfUserControl, INavigableView<SettingsViewModel>
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        // ホットキー表示更新
        ViewModel.OnHotkeyDisplayChanged = display =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Window.GetWindow(this) is MainWindow mw)
                    mw.UpdateTrayHotkeyDisplay(display);
            });
        };

        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Load();
        InitAutoRefreshCombo();
    }

    private void InitAutoRefreshCombo()
    {
        foreach (ComboBoxItem item in SettingsAutoRefreshComboBox.Items)
        {
            if (item.Tag is string s && s == ViewModel.AutoRefreshMinutes.ToString())
            {
                SettingsAutoRefreshComboBox.SelectedItem = item;
                return;
            }
        }
        SettingsAutoRefreshComboBox.SelectedIndex = 0; // Off
    }

    private void OnAutoRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsAutoRefreshComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string s && int.TryParse(s, out int val))
        {
            ViewModel.AutoRefreshMinutes = val;
        }
    }

    private void OnApplyHotkey(object sender, RoutedEventArgs e)
        => ViewModel.ApplyHotkeyCommand.Execute(null);
}
