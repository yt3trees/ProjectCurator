using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Curia.ViewModels;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Curia.Views.Controls;

public partial class CommandPaletteOverlay : WpfUserControl
{
    public CommandPaletteViewModel ViewModel => (CommandPaletteViewModel)DataContext;

    public CommandPaletteOverlay()
    {
        InitializeComponent();
        
        IsVisibleChanged += (s, e) =>
        {
            if ((bool)e.NewValue)
            {
                Dispatcher.BeginInvoke(new Action(() => {
                    SearchBox.Focus();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        };
    }

    private void OnBackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender)
        {
            ViewModel.Hide();
        }
    }

    private void OnSearchBoxKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel.Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ExecuteCurrentCommand();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
        }
    }

    private void ExecuteCurrentCommand()
    {
        var window = Window.GetWindow(this) as MainWindow;
        if (window != null)
        {
            ViewModel.ExecuteCommand(window);
        }
    }

    private void MoveSelection(int direction)
    {
        int count = CommandList.Items.Count;
        if (count == 0) return;

        int index = CommandList.SelectedIndex + direction;
        if (index < 0) index = 0;
        if (index >= count) index = count - 1;

        CommandList.SelectedIndex = index;
        CommandList.ScrollIntoView(CommandList.SelectedItem);
    }
}
