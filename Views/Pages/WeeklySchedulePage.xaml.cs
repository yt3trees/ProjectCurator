using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Curia.Services;
using Curia.ViewModels;
using Curia.Views.Controls;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Curia.Views.Pages;

public partial class WeeklySchedulePage : WpfUserControl, INavigableView<WeeklyScheduleViewModel>
{
    public WeeklyScheduleViewModel ViewModel { get; }

    private Point _taskDragStart;
    private bool _taskDragInitiated;

    public WeeklySchedulePage(WeeklyScheduleViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // WeekGridControl に ViewModel を接続
        WeekGrid.ViewModel = ViewModel;

        // ナビゲーションコールバック
        ViewModel.OnOpenInEditor = (project, filePath) =>
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.NavigateToEditorAndOpenFile(project, filePath);
        };

        _ = ViewModel.LoadWeekAsync();
    }

    // ─── 左ペインのタスク項目のドラッグ開始 ─────────────────────────

    private void OnTaskItemMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement fe) return;
        // DataContext は TaskViewItem (左パネル) または TodayQueueTask (直接バインド) の両方を許容
        TodayQueueTask? task = fe.DataContext switch
        {
            TaskViewItem item => item.Task,
            TodayQueueTask t  => t,
            _                 => null,
        };
        if (task == null) return;

        var current = e.GetPosition(this);
        if (!_taskDragInitiated)
        {
            // 最低限のドラッグ距離を確認
            double dx = Math.Abs(current.X - _taskDragStart.X);
            double dy = Math.Abs(current.Y - _taskDragStart.Y);
            if (dx < SystemParameters.MinimumHorizontalDragDistance &&
                dy < SystemParameters.MinimumVerticalDragDistance)
                return;
            _taskDragInitiated = true;
        }

        var payload = new TaskDragPayload(task);
        var data = new System.Windows.DataObject("Curia.TaskDragPayload", payload);
        System.Windows.DragDrop.DoDragDrop(fe, data, System.Windows.DragDropEffects.Copy);
        _taskDragInitiated = false;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        _taskDragStart = e.GetPosition(this);
        _taskDragInitiated = false;
    }
}
