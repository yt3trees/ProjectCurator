using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;
using ProjectCurator.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace ProjectCurator.Views.Pages;

public partial class TimelinePage : WpfUserControl, INavigableView<TimelineViewModel>
{
    private bool _isSyncingVerticalScroll;

    public TimelineViewModel ViewModel { get; }

    public TimelinePage(TimelineViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;

        // エントリクリック時にEditorページへ遷移
        ViewModel.OnOpenFileInEditor = (project, filePath) =>
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.NavigateToEditorAndOpenFile(project, filePath);
        };

        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitAsync();
        PeriodComboBox.SelectedIndex = 0; // 30 days

        // Dashboard からのジャンプ要求があれば対応するプロジェクトを選択
        if (ViewModel.NavigateToProjectKey is { } key)
        {
            ViewModel.NavigateToProjectKey = null;
            var match = ViewModel.Projects.FirstOrDefault(p => p.HiddenKey == key);
            if (match != null)
                ViewModel.SelectedProject = match;
        }
    }

    private void OnPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeriodComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string s && int.TryParse(s, out int val))
        {
            ViewModel.DaysBack = val;
        }
    }

    private void OnEntrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (TimelineListBox.SelectedItem is TimelineEntryItem entry)
        {
            ViewModel.OpenEntry(entry);
            TimelineListBox.SelectedItem = null; // 選択解除
        }
    }

    private void OnHeatmapBodyScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (HeatmapHeaderScrollViewer != null && e.HorizontalChange != 0)
            HeatmapHeaderScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);

        if (HeatmapProjectScrollViewer != null && e.VerticalChange != 0)
        {
            _isSyncingVerticalScroll = true;
            HeatmapProjectScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
            _isSyncingVerticalScroll = false;
        }
    }

    private void OnHeatmapProjectScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingVerticalScroll)
            return;

        if (HeatmapBodyScrollViewer == null || e.VerticalChange == 0)
            return;

        _isSyncingVerticalScroll = true;
        HeatmapBodyScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
        _isSyncingVerticalScroll = false;
    }

    private void OnHeatmapCellClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: TimelineHeatmapCellItem cell })
            ViewModel.OpenHeatmapCell(cell);
    }

    private void OnHeatmapBodyPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            return;

        var delta = e.Delta > 0 ? -48d : 48d;
        var next = scrollViewer.HorizontalOffset + delta;
        scrollViewer.ScrollToHorizontalOffset(next);
        e.Handled = true;
    }
}
