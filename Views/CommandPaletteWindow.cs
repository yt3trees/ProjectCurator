using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Curia.Models;
using Curia.ViewModels;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;

namespace Curia.Views;

/// <summary>
/// Command Palette ウィンドウ。Ctrl+K / Ctrl+Shift+K でどこからでも起動する。
/// </summary>
public class CommandPaletteWindow : Window
{
    private readonly CommandPaletteViewModel _viewModel;
    private readonly MainWindow _mainWindow;

    private System.Windows.Controls.TextBox _searchBox = null!;
    private WpfListBox _commandList = null!;
    private bool _canCloseOnDeactivate = false;

    public CommandPaletteWindow(CommandPaletteViewModel viewModel, MainWindow mainWindow)
    {
        _viewModel = viewModel;
        _mainWindow = mainWindow;

        // ── ウィンドウ基本設定 ─────────────────────────────────────────────
        // NoResize + WindowStyle.None では DWM 白枠が出ないため WindowChrome 不要
        // (AGENTS.md: NoResize ダイアログに WindowChrome を適用しない)
        WindowStyle           = WindowStyle.None;
        ResizeMode            = ResizeMode.NoResize;
        SizeToContent         = SizeToContent.Height;
        MinHeight             = 0;
        Width                 = 640;
        MaxHeight             = 520;
        Topmost               = true;
        ShowInTaskbar         = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // ── 位置: 検索ボックス（行高さ約40px）の中心がモニター中央に来るよう配置 ───
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top  = workArea.Top  + (workArea.Height / 2) - 20;

        // ── UI 構築 ──────────────────────────────────────────────────────
        Content = BuildContent();
        DataContext = _viewModel;

        // ContentRendered 後（ウィンドウが完全に描画・活性化されてから）のみ
        // Deactivated でクローズする。Show() 中の一時的な非活性化を誤検知しないため。
        // BeginInvoke で次フレームに遅延させることで、非活性化メッセージ処理中の
        // 再入 (reentrancy) によるフリーズを防ぐ。
        ContentRendered += (_, _) => _canCloseOnDeactivate = true;
        Deactivated += (_, _) =>
        {
            if (_canCloseOnDeactivate)
                Dispatcher.BeginInvoke(new Action(Close), System.Windows.Threading.DispatcherPriority.Normal);
        };

        // Window レベルで Escape を捕捉（グローバルホットキー起動時もフォーカスに依存しない）
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _canCloseOnDeactivate = false;
                Close();
                e.Handled = true;
            }
        };

        // Activated で設定することで、グローバルホットキー経由（他アプリからフォーカス移動）でも
        // 確実にキーボードフォーカスが searchBox に届く
        Activated += (_, _) =>
        {
            _searchBox.Focus();
            Keyboard.Focus(_searchBox);
        };
    }

    private FrameworkElement BuildContent()
    {
        // ウィンドウ背景色を明示設定
        this.SetResourceReference(Window.BackgroundProperty, "AppSurface0");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 検索ボックス
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // セパレータ
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MaxHeight = 400 }); // リスト

        // ── 検索ボックス行 ────────────────────────────────────────────────
        var searchRow = new Border { Padding = new Thickness(8, 8, 8, 8) };
        searchRow.SetResourceReference(Border.BackgroundProperty, "AppBackground");

        _searchBox = new System.Windows.Controls.TextBox
        {
            FontSize        = 15,
            Padding         = new Thickness(4, 4, 4, 4),
            BorderThickness = new Thickness(0),
            AcceptsReturn   = false,
        };
        _searchBox.SetResourceReference(System.Windows.Controls.TextBox.BackgroundProperty, "AppBackground");
        _searchBox.SetResourceReference(System.Windows.Controls.TextBox.ForegroundProperty, "AppText");
        _searchBox.TextChanged += (_, _) => _viewModel.SearchText = _searchBox.Text;
        _searchBox.PreviewKeyDown += OnSearchBoxKeyDown;
        searchRow.Child = _searchBox;
        Grid.SetRow(searchRow, 0);
        root.Children.Add(searchRow);

        // ── セパレータ ────────────────────────────────────────────────────
        var separator = new Border { Height = 1 };
        separator.SetResourceReference(Border.BackgroundProperty, "AppSurface1");
        Grid.SetRow(separator, 1);
        root.Children.Add(separator);

        // ── コマンドリスト ────────────────────────────────────────────────
        _commandList = new WpfListBox
        {
            BorderThickness = new Thickness(0),
            Padding         = new Thickness(0, 4, 0, 4),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_commandList, ScrollBarVisibility.Disabled);
        _commandList.SetResourceReference(WpfListBox.BackgroundProperty, "AppSurface0");

        _commandList.ItemTemplate = BuildItemTemplate();
        _commandList.ItemContainerStyle = BuildItemContainerStyle();
        _commandList.SetBinding(WpfListBox.ItemsSourceProperty,
            new System.Windows.Data.Binding(nameof(_viewModel.FilteredCommands)));
        _commandList.SetBinding(WpfListBox.SelectedItemProperty,
            new System.Windows.Data.Binding(nameof(_viewModel.SelectedCommand)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        _commandList.MouseDoubleClick += (_, _) => ExecuteAndClose();
        Grid.SetRow(_commandList, 2);
        root.Children.Add(_commandList);

        // ── 外枠ボーダーオーバーレイ (CaptureWindow と同パターン) ─────────
        var borderOverlay = new Border
        {
            BorderThickness   = new Thickness(1),
            IsHitTestVisible  = false,
        };
        borderOverlay.SetResourceReference(Border.BorderBrushProperty, "AppSurface2");
        Grid.SetRowSpan(borderOverlay, 3);
        root.Children.Add(borderOverlay);

        return root;
    }

    private DataTemplate BuildItemTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.PaddingProperty, new Thickness(12, 6, 12, 6));
        factory.SetValue(Border.BackgroundProperty, WpfBrushes.Transparent);

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
        textFactory.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding(nameof(CommandItem.Display)));
        textFactory.SetResourceReference(TextBlock.ForegroundProperty, "AppText");

        factory.AppendChild(textFactory);

        return new DataTemplate { VisualTree = factory };
    }

    private Style BuildItemContainerStyle()
    {
        var style = new Style(typeof(WpfListBoxItem));

        // ControlTemplate: Border の Background を ListBoxItem.Background に TemplateBinding する
        var template = new ControlTemplate(typeof(WpfListBoxItem));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
        borderFactory.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = borderFactory;
        style.Setters.Add(new Setter(WpfListBoxItem.TemplateProperty, template));

        // デフォルト背景
        style.Setters.Add(new Setter(WpfListBoxItem.BackgroundProperty, WpfBrushes.Transparent));

        // 選択時: Style トリガーで ListBoxItem.Background を変更 (名前付き要素参照不要)
        var selectedTrigger = new Trigger { Property = WpfListBoxItem.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(WpfListBoxItem.BackgroundProperty,
            new DynamicResourceExtension("AppSurface1")));
        style.Triggers.Add(selectedTrigger);

        // ホバー時
        var hoverTrigger = new Trigger { Property = WpfListBoxItem.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(WpfListBoxItem.BackgroundProperty,
            new DynamicResourceExtension("AppSurface2")));
        style.Triggers.Add(hoverTrigger);

        return style;
    }

    private void OnSearchBoxKeyDown(object sender, WpfKeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter:
                ExecuteAndClose();
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        int count = _commandList.Items.Count;
        if (count == 0) return;
        int index = Math.Clamp(_commandList.SelectedIndex + delta, 0, count - 1);
        _commandList.SelectedIndex = index;
        _commandList.ScrollIntoView(_commandList.SelectedItem);
    }

    private void ExecuteAndClose()
    {
        _canCloseOnDeactivate = false; // Deactivated による二重クローズを防ぐ
        _viewModel.ExecuteSelected(cmd =>
        {
            Close();
            cmd.Action?.Invoke(_mainWindow);
        });
    }
}
