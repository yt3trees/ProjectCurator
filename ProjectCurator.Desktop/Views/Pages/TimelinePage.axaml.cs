using Avalonia.Controls;
using Avalonia.Input;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views.Pages;

public partial class TimelinePage : UserControl
{
    private bool _isSyncingVerticalScroll;

    public TimelinePage()
    {
        InitializeComponent();
        DataContext = App.Services.GetService(typeof(TimelineViewModel));
    }

    private void OnPeriodChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not TimelineViewModel vm) return;
        if (PeriodComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string s && int.TryParse(s, out int val))
        {
            vm.DaysBack = val;
        }
    }

    private void OnEntrySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not TimelineViewModel vm) return;
        if (TimelineListBox.SelectedItem is TimelineEntryItem entry)
        {
            vm.OpenEntry(entry);
            TimelineListBox.SelectedItem = null;
        }
    }

    private void OnHeatmapBodyScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer bodyScroll) return;

        if (HeatmapHeaderScrollViewer != null && e.OffsetDelta.X != 0)
            HeatmapHeaderScrollViewer.Offset = HeatmapHeaderScrollViewer.Offset.WithX(bodyScroll.Offset.X);

        if (HeatmapProjectScrollViewer != null && e.OffsetDelta.Y != 0)
        {
            _isSyncingVerticalScroll = true;
            HeatmapProjectScrollViewer.Offset = HeatmapProjectScrollViewer.Offset.WithY(bodyScroll.Offset.Y);
            _isSyncingVerticalScroll = false;
        }
    }

    private void OnHeatmapProjectScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingVerticalScroll) return;
        if (sender is not ScrollViewer projScroll) return;
        if (HeatmapBodyScrollViewer == null || e.OffsetDelta.Y == 0) return;

        _isSyncingVerticalScroll = true;
        HeatmapBodyScrollViewer.Offset = HeatmapBodyScrollViewer.Offset.WithY(projScroll.Offset.Y);
        _isSyncingVerticalScroll = false;
    }

    private void OnHeatmapCellPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TimelineViewModel vm) return;
        if (sender is Border { DataContext: TimelineHeatmapCellItem cell })
            vm.OpenHeatmapCell(cell);
    }
}
