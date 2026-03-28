using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop.Views.Controls;

public partial class CommandPaletteOverlay : UserControl
{
    private CommandPaletteViewModel? _viewModel;

    public CommandPaletteOverlay()
    {
        InitializeComponent();
        _viewModel = App.Services.GetService<CommandPaletteViewModel>();
        DataContext = _viewModel;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_viewModel == null) return;

        if (e.Key == Key.Escape)
        {
            _viewModel.Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _viewModel.ExecuteCommandCommand.Execute(null);
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

    private void MoveSelection(int direction)
    {
        if (_viewModel == null) return;
        var list = ResultsList;
        int count = list.ItemCount;
        if (count == 0) return;

        int index = list.SelectedIndex + direction;
        if (index < 0) index = 0;
        if (index >= count) index = count - 1;

        list.SelectedIndex = index;
        list.ScrollIntoView(index);
    }
}
