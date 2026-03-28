using Avalonia.Controls;
using Avalonia.Input;
using ProjectCurator.Interfaces;

namespace ProjectCurator.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly IHotkeyService? _hotkeyService;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(IHotkeyService hotkeyService) : this()
    {
        _hotkeyService = hotkeyService;
        if (_hotkeyService != null)
        {
            _hotkeyService.OnActivated = ToggleVisibility;
        }
    }

    private void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Escape hides window
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }
}
