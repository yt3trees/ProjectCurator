using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProjectCurator.Models;
using ProjectCurator.Services;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHA = System.Windows.HorizontalAlignment;
using WpfVA = System.Windows.VerticalAlignment;

namespace ProjectCurator.Views;

/// <summary>
/// Global Capture ウィンドウ。どこからでもホットキーで起動する軽量入力 UI。
/// MainWindow から ShowDialog() で呼び出す。
/// </summary>
public class CaptureWindow : Window
{
    // ── 依存サービス ──────────────────────────────────────────────────────
    private readonly CaptureService _captureService;
    private readonly ProjectDiscoveryService _discoveryService;
    private readonly ConfigService _configService;

    // ── 状態 ──────────────────────────────────────────────────────────────
    private enum Screen { Input, Loading, Confirm, TaskApproval, Complete }
    private Screen _currentScreen = Screen.Input;
    private CaptureClassification? _classification;
    private CancellationTokenSource? _cts;
    private List<ProjectInfo> _projects = [];
    private List<AsanaProjectMeta> _asanaProjects = [];
    private List<AsanaSectionMeta> _asanaSections = [];

    // ── テーマブラシ ──────────────────────────────────────────────────────
    private System.Windows.Media.Brush Surface0 => (System.Windows.Media.Brush)FindResource("AppSurface0");
    private System.Windows.Media.Brush Surface1 => (System.Windows.Media.Brush)FindResource("AppSurface1");
    private System.Windows.Media.Brush Surface2 => (System.Windows.Media.Brush)FindResource("AppSurface2");
    private System.Windows.Media.Brush Text => (System.Windows.Media.Brush)FindResource("AppText");
    private System.Windows.Media.Brush Subtext => (System.Windows.Media.Brush)FindResource("AppSubtext0");
    private System.Windows.Media.Brush Accent => Application.Current.Resources.Contains("AppPeach")
        ? (System.Windows.Media.Brush)Application.Current.Resources["AppPeach"]
        : (System.Windows.Media.Brush)FindResource("AppText");
    private System.Windows.Media.Brush AccentGreen => Application.Current.Resources.Contains("AppGreen")
        ? (System.Windows.Media.Brush)Application.Current.Resources["AppGreen"]
        : (System.Windows.Media.Brush)FindResource("AppText");

    // ── ルート Grid ───────────────────────────────────────────────────────
    private readonly Grid _root;
    private Grid _titleBar = null!;

    // ── Input screen ─────────────────────────────────────────────────────
    private Grid _inputPanel = null!;
    private System.Windows.Controls.TextBox _inputBox = null!;
    private System.Windows.Controls.ComboBox _projectCombo = null!;
    private System.Windows.Controls.ComboBox _categoryCombo = null!;
    private System.Windows.Controls.Button _captureBtn = null!;

    // ── Loading screen ────────────────────────────────────────────────────
    private Grid _loadingPanel = null!;

    // ── Confirm screen ────────────────────────────────────────────────────
    private Grid _confirmPanel = null!;
    private System.Windows.Controls.TextBlock _confirmInputPreview = null!;
    private System.Windows.Controls.ComboBox _confirmCategoryCombo = null!;
    private System.Windows.Controls.ComboBox _confirmProjectCombo = null!;
    private System.Windows.Controls.TextBox _confirmSummaryBox = null!;
    private StackPanel _asanaProjectRow = null!;
    private System.Windows.Controls.ComboBox _asanaProjectCombo = null!;
    private StackPanel _asanaSectionRow = null!;
    private System.Windows.Controls.ComboBox _asanaSectionCombo = null!;
    private StackPanel _dueDateRow = null!;
    private StackPanel _timePickerRow = null!;
    private System.Windows.Controls.DatePicker _dueDatePicker = null!;
    private System.Windows.Controls.CheckBox _setTimeCheckBox = null!;
    private System.Windows.Controls.ComboBox _hourCombo = null!;
    private System.Windows.Controls.ComboBox _minuteCombo = null!;
    private System.Windows.Controls.TextBlock _confirmErrorText = null!;

    // ── Task Approval screen ──────────────────────────────────────────────
    private Grid _taskApprovalPanel = null!;
    private System.Windows.Controls.TextBox _requestPreviewBox = null!;
    private System.Windows.Controls.Button _approveBtn = null!;
    private System.Windows.Controls.TextBlock _taskApprovalErrorText = null!;
    private System.Windows.Controls.Button _saveAsMemoBtn = null!;

    // ── Complete screen ───────────────────────────────────────────────────
    private Grid _completePanel = null!;
    private System.Windows.Controls.TextBlock _completeMessageText = null!;
    private System.Windows.Controls.Button _openFileBtn = null!;
    private System.Windows.Controls.Button _openAsanaBtn = null!;

    // ── ナビゲーションコールバック ─────────────────────────────────────────
    public Action<string, string>? OnNavigateToFile { get; set; }         // (projectName, filePath)
    public Action<string, string, string>? OnNavigateToFocusUpdate { get; set; }  // (projectName, filePath, capturedText) → focus_update 専用
    public Action<string, string>? OnNavigateToDecision { get; set; }      // (projectName, capturedText)

    // ─────────────────────────────────────────────────────────────────────
    public CaptureWindow(
        CaptureService captureService,
        ProjectDiscoveryService discoveryService,
        ConfigService configService)
    {
        _captureService = captureService;
        _discoveryService = discoveryService;
        _configService = configService;

        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;

        // WindowChrome: 白枠防止
        System.Windows.Shell.WindowChrome.SetWindowChrome(this,
            new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });

        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // title bar
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // content

        BuildTitleBar();
        BuildInputPanel();
        BuildLoadingPanel();
        BuildConfirmPanel();
        BuildTaskApprovalPanel();
        BuildCompletePanel();

        Content = _root;

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Build UI
    // ─────────────────────────────────────────────────────────────────────

    private void BuildTitleBar()
    {
        _titleBar = new Grid
        {
            Height = 36,
            Background = Surface1,
            Cursor = WpfCursors.SizeAll
        };
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new System.Windows.Controls.TextBlock
        {
            Text = "●",
            Foreground = Accent,
            FontSize = 10,
            VerticalAlignment = WpfVA.Center,
            Margin = new Thickness(12, 0, 6, 0)
        };
        Grid.SetColumn(dot, 0);

        var title = new System.Windows.Controls.TextBlock
        {
            Text = "Quick Capture",
            Foreground = Text,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = WpfVA.Center
        };
        Grid.SetColumn(title, 1);

        var closeBtn = new System.Windows.Controls.Button
        {
            Content = "✕",
            Width = 36,
            Height = 36,
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
            Foreground = Subtext,
            FontSize = 12,
            Cursor = WpfCursors.Hand
        };
        closeBtn.Click += (_, _) => { _cts?.Cancel(); Close(); };
        Grid.SetColumn(closeBtn, 2);

        _titleBar.Children.Add(dot);
        _titleBar.Children.Add(title);
        _titleBar.Children.Add(closeBtn);
        _titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        Grid.SetRow(_titleBar, 0);
        _root.Children.Add(_titleBar);

        // ウィンドウ境界線
        var border = new Border
        {
            BorderBrush = Surface2,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false
        };
        var borderStyle = new Style(typeof(Border));
        var borderTrigger = new DataTrigger
        {
            Binding = new System.Windows.Data.Binding("WindowState")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Window), 1)
            },
            Value = WindowState.Maximized
        };
        borderTrigger.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(0)));
        borderStyle.Triggers.Add(borderTrigger);
        border.Style = borderStyle;

        Grid.SetRow(border, 0);
        Grid.SetRowSpan(border, 2);
        _root.Children.Add(border);
    }

    // ── Input screen ──────────────────────────────────────────────────────
    private void BuildInputPanel()
    {
        _inputPanel = new Grid { Background = Surface0, Margin = new Thickness(12, 10, 12, 12) };
        _inputPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _inputPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 入力 TextBox
        _inputBox = new System.Windows.Controls.TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100,
            MaxHeight = 160,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Surface1,
            Foreground = Text,
            CaretBrush = Text,
            BorderBrush = Surface2,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        _inputBox.TextChanged += (_, _) => _captureBtn.IsEnabled = !string.IsNullOrWhiteSpace(_inputBox.Text);
        Grid.SetRow(_inputBox, 0);
        _inputPanel.Children.Add(_inputBox);

        // フッター行 (Project コンボ + ボタン)
        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _categoryCombo = BuildComboBox(new[] { "task", "tension", "focus_update", "decision", "memo" }, 110);
        _categoryCombo.SelectedIndex = 0;
        _categoryCombo.Visibility = Visibility.Collapsed; // AI 有効時は非表示
        Grid.SetColumn(_categoryCombo, 0);
        // category combo はプロジェクト comboの前に配置するため MarginRight を設定
        _categoryCombo.Margin = new Thickness(0, 0, 6, 0);

        _projectCombo = BuildComboBox([], 0);
        _projectCombo.MinWidth = 160;
        Grid.SetColumn(_projectCombo, 0);

        _captureBtn = BuildButton("Capture  ▶", true, enabled: false);
        _captureBtn.Margin = new Thickness(8, 0, 0, 0);
        _captureBtn.Click += OnCaptureClick;
        Grid.SetColumn(_captureBtn, 2);

        var cancelBtn = BuildButton("Cancel", false);
        cancelBtn.Margin = new Thickness(6, 0, 0, 0);
        cancelBtn.Click += (_, _) => Close();
        Grid.SetColumn(cancelBtn, 3);

        footer.Children.Add(_categoryCombo);
        footer.Children.Add(_projectCombo);
        footer.Children.Add(_captureBtn);
        footer.Children.Add(cancelBtn);
        Grid.SetRow(footer, 1);
        _inputPanel.Children.Add(footer);

        Grid.SetRow(_inputPanel, 1);
        _root.Children.Add(_inputPanel);
    }

    // ── Loading screen ────────────────────────────────────────────────────
    private void BuildLoadingPanel()
    {
        _loadingPanel = new Grid
        {
            Background = Surface0,
            Margin = new Thickness(12, 10, 12, 12),
            MinHeight = 120,
            Visibility = Visibility.Collapsed
        };
        _loadingPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _loadingPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var centerStack = new StackPanel
        {
            VerticalAlignment = WpfVA.Center,
            HorizontalAlignment = WpfHA.Center,
            Margin = new Thickness(0, 16, 0, 16)
        };
        var progress = new System.Windows.Controls.ProgressBar
        {
            IsIndeterminate = true,
            Width = 200,
            Height = 4,
            Margin = new Thickness(0, 0, 0, 10),
            Foreground = Accent
        };
        var msg = new System.Windows.Controls.TextBlock
        {
            Text = "Classifying...",
            Foreground = Subtext,
            FontSize = 12,
            HorizontalAlignment = WpfHA.Center
        };
        centerStack.Children.Add(progress);
        centerStack.Children.Add(msg);
        Grid.SetRow(centerStack, 0);
        _loadingPanel.Children.Add(centerStack);

        var cancelBtn = BuildButton("Cancel", false);
        cancelBtn.HorizontalAlignment = WpfHA.Right;
        cancelBtn.Margin = new Thickness(0, 8, 0, 0);
        cancelBtn.Click += (_, _) => { _cts?.Cancel(); ShowScreen(Screen.Input); };
        Grid.SetRow(cancelBtn, 1);
        _loadingPanel.Children.Add(cancelBtn);

        Grid.SetRow(_loadingPanel, 1);
        _root.Children.Add(_loadingPanel);
    }

    // ── Confirm screen ────────────────────────────────────────────────────
    private void BuildConfirmPanel()
    {
        _confirmPanel = new Grid
        {
            Background = Surface0,
            Margin = new Thickness(12, 10, 12, 12),
            Visibility = Visibility.Collapsed
        };
        _confirmPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // preview
        _confirmPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // separator
        _confirmPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // fields
        _confirmPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // error
        _confirmPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons

        // 入力テキストプレビュー
        _confirmInputPreview = new System.Windows.Controls.TextBlock
        {
            Foreground = Subtext,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 52,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(_confirmInputPreview, 0);
        _confirmPanel.Children.Add(_confirmInputPreview);

        // セパレーター
        var sep = new Border
        {
            Height = 1,
            Background = Surface2,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(sep, 1);
        _confirmPanel.Children.Add(sep);

        // フィールド
        var fields = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };

        // Category
        var catRow = BuildFieldRow("Category");
        _confirmCategoryCombo = BuildComboBox(
            ["task", "tension", "focus_update", "decision", "memo"], 140);
        _confirmCategoryCombo.SelectionChanged += OnConfirmCategoryChanged;
        catRow.Children.Add(_confirmCategoryCombo);
        fields.Children.Add(catRow);

        // Project
        var projRow = BuildFieldRow("Project");
        _confirmProjectCombo = BuildComboBox([], 200);
        _confirmProjectCombo.SelectionChanged += OnConfirmProjectChanged;
        projRow.Children.Add(_confirmProjectCombo);
        fields.Children.Add(projRow);

        // Summary
        var summaryRow = BuildFieldRow("Summary");
        _confirmSummaryBox = new System.Windows.Controls.TextBox
        {
            MinWidth = 280,
            Background = Surface1,
            Foreground = Text,
            CaretBrush = Text,
            BorderBrush = Surface2,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4),
            FontSize = 12
        };
        summaryRow.Children.Add(_confirmSummaryBox);
        fields.Children.Add(summaryRow);

        // Asana Project (task のみ)
        _asanaProjectRow = BuildFieldRow("Asana Project");
        _asanaProjectRow.Visibility = Visibility.Collapsed;
        _asanaProjectCombo = BuildComboBox([], 240);
        _asanaProjectCombo.SelectionChanged += OnAsanaProjectChanged;
        _asanaProjectRow.Children.Add(_asanaProjectCombo);
        fields.Children.Add(_asanaProjectRow);

        // Asana Section (task のみ)
        _asanaSectionRow = BuildFieldRow("Section (optional)");
        _asanaSectionRow.Visibility = Visibility.Collapsed;
        _asanaSectionCombo = BuildComboBox([], 240);
        _asanaSectionRow.Children.Add(_asanaSectionCombo);
        fields.Children.Add(_asanaSectionRow);

        // Due Date (task のみ)
        _dueDateRow = BuildFieldRow("Due Date");
        _dueDateRow.Visibility = Visibility.Collapsed;
        _dueDatePicker = new System.Windows.Controls.DatePicker
        {
            Width = 140,
            Background = Surface1,
            Foreground = Text,
            BorderBrush = Surface2,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 12,
            ToolTip = "Select a due date (optional)"
        };
        _setTimeCheckBox = new System.Windows.Controls.CheckBox
        {
            Content = "Set time",
            Foreground = Subtext,
            FontSize = 11,
            VerticalAlignment = WpfVA.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        _setTimeCheckBox.Checked   += (_, _) => _timePickerRow.Visibility = Visibility.Visible;
        _setTimeCheckBox.Unchecked += (_, _) => _timePickerRow.Visibility = Visibility.Collapsed;
        _dueDateRow.Children.Add(_dueDatePicker);
        _dueDateRow.Children.Add(_setTimeCheckBox);
        fields.Children.Add(_dueDateRow);

        // Time pickers (task のみ、"Set time" チェック時のみ表示)
        _timePickerRow = BuildFieldRow("Time");
        _timePickerRow.Visibility = Visibility.Collapsed;
        _hourCombo = BuildComboBox(
            Enumerable.Range(0, 24).Select(h => h.ToString("00")), 60);
        _hourCombo.SelectedIndex = 9;  // default 09:00
        var colonLabel = new System.Windows.Controls.TextBlock
        {
            Text = ":",
            Foreground = Text,
            FontSize = 13,
            VerticalAlignment = WpfVA.Center,
            Margin = new Thickness(4, 0, 4, 0)
        };
        _minuteCombo = BuildComboBox(["00", "15", "30", "45"], 60);
        _minuteCombo.SelectedIndex = 0;
        _timePickerRow.Children.Add(_hourCombo);
        _timePickerRow.Children.Add(colonLabel);
        _timePickerRow.Children.Add(_minuteCombo);
        fields.Children.Add(_timePickerRow);

        Grid.SetRow(fields, 2);
        _confirmPanel.Children.Add(fields);

        // エラーテキスト
        _confirmErrorText = new System.Windows.Controls.TextBlock
        {
            Foreground = new SolidColorBrush(Colors.OrangeRed),
            FontSize = 11,
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(_confirmErrorText, 3);
        _confirmPanel.Children.Add(_confirmErrorText);

        // ボタン行
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = WpfHA.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var routeBtn = BuildButton("Route  ▶", true);
        routeBtn.Click += OnRouteClick;
        var backBtn = BuildButton("Back", false);
        backBtn.Margin = new Thickness(6, 0, 0, 0);
        backBtn.Click += (_, _) => ShowScreen(Screen.Input);
        var cancelBtn = BuildButton("Cancel", false);
        cancelBtn.Margin = new Thickness(6, 0, 0, 0);
        cancelBtn.Click += (_, _) => Close();
        btnRow.Children.Add(routeBtn);
        btnRow.Children.Add(backBtn);
        btnRow.Children.Add(cancelBtn);
        Grid.SetRow(btnRow, 4);
        _confirmPanel.Children.Add(btnRow);

        Grid.SetRow(_confirmPanel, 1);
        _root.Children.Add(_confirmPanel);
    }

    // ── Task Approval screen ──────────────────────────────────────────────
    private void BuildTaskApprovalPanel()
    {
        _taskApprovalPanel = new Grid
        {
            Background = Surface0,
            Margin = new Thickness(12, 10, 12, 12),
            Visibility = Visibility.Collapsed
        };
        _taskApprovalPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // header
        _taskApprovalPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // preview
        _taskApprovalPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // error text
        _taskApprovalPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons

        var header = new System.Windows.Controls.TextBlock
        {
            Text = "Review the request before sending:",
            Foreground = Subtext,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(header, 0);
        _taskApprovalPanel.Children.Add(header);

        _requestPreviewBox = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 140,
            MaxHeight = 220,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Surface1,
            Foreground = Text,
            BorderBrush = Surface2,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11
        };
        Grid.SetRow(_requestPreviewBox, 1);
        _taskApprovalPanel.Children.Add(_requestPreviewBox);

        _taskApprovalErrorText = new System.Windows.Controls.TextBlock
        {
            Foreground = new SolidColorBrush(Colors.OrangeRed),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(_taskApprovalErrorText, 2);
        _taskApprovalPanel.Children.Add(_taskApprovalErrorText);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = WpfHA.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _approveBtn = BuildButton("Approve & Create", true);
        _approveBtn.Click += OnApproveClick;
        _saveAsMemoBtn = BuildButton("Save as memo", false);
        _saveAsMemoBtn.Margin = new Thickness(6, 0, 0, 0);
        _saveAsMemoBtn.Visibility = Visibility.Collapsed;
        _saveAsMemoBtn.Click += OnSaveAsMemoClick;
        var backBtn = BuildButton("Back to Edit", false);
        backBtn.Margin = new Thickness(6, 0, 0, 0);
        backBtn.Click += (_, _) =>
        {
            _taskApprovalErrorText.Visibility = Visibility.Collapsed;
            _saveAsMemoBtn.Visibility = Visibility.Collapsed;
            _approveBtn.Content = "Approve & Create";
            ShowScreen(Screen.Confirm);
        };
        var cancelBtn = BuildButton("Cancel", false);
        cancelBtn.Margin = new Thickness(6, 0, 0, 0);
        cancelBtn.Click += (_, _) => Close();
        btnRow.Children.Add(_approveBtn);
        btnRow.Children.Add(_saveAsMemoBtn);
        btnRow.Children.Add(backBtn);
        btnRow.Children.Add(cancelBtn);
        Grid.SetRow(btnRow, 3);
        _taskApprovalPanel.Children.Add(btnRow);

        Grid.SetRow(_taskApprovalPanel, 1);
        _root.Children.Add(_taskApprovalPanel);
    }

    // ── Complete screen ───────────────────────────────────────────────────
    private void BuildCompletePanel()
    {
        _completePanel = new Grid
        {
            Background = Surface0,
            Margin = new Thickness(12, 10, 12, 12),
            MinHeight = 80,
            Visibility = Visibility.Collapsed
        };
        _completePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _completePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _completeMessageText = new System.Windows.Controls.TextBlock
        {
            Foreground = AccentGreen,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = WpfVA.Center,
            Margin = new Thickness(0, 12, 0, 12)
        };
        Grid.SetRow(_completeMessageText, 0);
        _completePanel.Children.Add(_completeMessageText);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = WpfHA.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _openFileBtn = BuildButton("Open File", false);
        _openFileBtn.Visibility = Visibility.Collapsed;
        _openFileBtn.Click += OnOpenFileClick;
        _openAsanaBtn = BuildButton("Open Asana", false);
        _openAsanaBtn.Visibility = Visibility.Collapsed;
        _openAsanaBtn.Click += OnOpenAsanaClick;
        var newCaptureBtn = BuildButton("New Capture", true);
        newCaptureBtn.Margin = new Thickness(6, 0, 0, 0);
        newCaptureBtn.Click += (_, _) => { ResetToInputScreen(); ShowScreen(Screen.Input); };
        var closeBtn = BuildButton("Close", false);
        closeBtn.Margin = new Thickness(6, 0, 0, 0);
        closeBtn.Click += (_, _) => Close();
        btnRow.Children.Add(_openFileBtn);
        btnRow.Children.Add(_openAsanaBtn);
        btnRow.Children.Add(newCaptureBtn);
        btnRow.Children.Add(closeBtn);
        Grid.SetRow(btnRow, 1);
        _completePanel.Children.Add(btnRow);

        Grid.SetRow(_completePanel, 1);
        _root.Children.Add(_completePanel);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // ウィンドウ背景
        Background = Surface0;

        // ウィンドウ位置とフォーカスを先に設定する。
        // GetProjectInfoListAsync は初回がディスク全スキャンで数秒かかるため、
        // await より前に完了させておかないとその間キーボード入力が効かない。
        PositionNearCursor();
        Activate();
        _inputBox.Focus();

        // AI 有効かチェック (コンボ表示はプロジェクト読み込み前でも確定できる)
        var settings = _configService.LoadSettings();
        if (!settings.AiEnabled)
            _categoryCombo.Visibility = Visibility.Visible;

        // プロジェクト一覧を非同期で取得
        try
        {
            _projects = await _discoveryService.GetProjectInfoListAsync();
        }
        catch
        {
            _projects = [];
        }

        // Project コンボを初期化 (取得完了後)
        PopulateProjectCombos();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (_currentScreen == Screen.Loading)
                _cts?.Cancel();
            else
                Close();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Enter &&
                 System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            switch (_currentScreen)
            {
                case Screen.Input when _captureBtn.IsEnabled:
                    OnCaptureClick(null, null!);
                    break;
                case Screen.Confirm:
                    OnRouteClick(null, null!);
                    break;
                case Screen.TaskApproval:
                    OnApproveClick(null, null!);
                    break;
            }
            e.Handled = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Event handlers
    // ─────────────────────────────────────────────────────────────────────

    private async void OnCaptureClick(object? sender, RoutedEventArgs e)
    {
        var input = _inputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;

        // 機密文字列チェック (簡易)
        if (ContainsSensitivePattern(input))
        {
            var confirm = MessageBox.Show(
                "The input may contain sensitive data (API key / token / secret).\nProceed?",
                "Quick Capture",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        var settings = _configService.LoadSettings();
        var selectedProject = (_projectCombo.SelectedItem as ProjectInfo)?.Name;

        if (!settings.AiEnabled)
        {
            // AI 無効: 手動分類
            var category = (_categoryCombo.SelectedItem as string) ?? "memo";
            _classification = _captureService.BuildManualClassification(input, category, selectedProject ?? "");
            ShowConfirmScreen(input);
            return;
        }

        // AI 分類
        ShowScreen(Screen.Loading);
        _cts = new CancellationTokenSource();
        try
        {
            _classification = await _captureService.ClassifyAsync(input, selectedProject, _cts.Token);
            ShowConfirmScreen(input);
        }
        catch (OperationCanceledException)
        {
            ShowScreen(Screen.Input);
        }
        catch (Exception ex)
        {
            // LLM エラー: 手動モードにフォールバック
            _classification = _captureService.BuildManualClassification(input, "memo", selectedProject ?? "");
            ShowConfirmScreen(input);
            SetConfirmError($"AI classification failed ({ex.Message}). Manual mode active.");
        }
    }

    private async void OnRouteClick(object? sender, RoutedEventArgs e)
    {
        if (_classification == null) return;

        _classification.Category = (_confirmCategoryCombo.SelectedItem as string) ?? _classification.Category;
        _classification.ProjectName = (_confirmProjectCombo.SelectedItem as ProjectInfo)?.Name ?? _classification.ProjectName;
        _classification.Summary = _confirmSummaryBox.Text.Trim();

        SetConfirmError("");

        if (string.IsNullOrWhiteSpace(_classification.ProjectName) && _classification.Category != "memo")
        {
            SetConfirmError("Please select a project.");
            return;
        }

        if (_classification.Category == "task")
        {
            // task: 承認画面へ
            await ShowTaskApprovalScreenAsync();
            return;
        }

        // tension + AI: 差分確認ダイアログ
        if (_classification.Category == "tension" && _configService.LoadSettings().AiEnabled)
        {
            _cts = new CancellationTokenSource();
            await HandleTensionWithReviewAsync(_classification, _cts.Token);
            return;
        }

        // focus_update / decision / tension(AI無効) / memo
        _cts = new CancellationTokenSource();
        CaptureRouteResult result;
        try
        {
            result = await _captureService.RouteAsync(_classification, _inputBox.Text, _cts.Token);
        }
        catch (Exception ex)
        {
            SetConfirmError($"Routing failed: {ex.Message}");
            return;
        }

        HandleRouteResult(result);
    }

    private async Task HandleTensionWithReviewAsync(CaptureClassification classification, CancellationToken ct)
    {
        var projects = await _discoveryService.GetProjectInfoListAsync(ct: ct);
        var project  = projects.FirstOrDefault(p =>
            string.Equals(p.Name, classification.ProjectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
        {
            SetConfirmError("Project not found.");
            return;
        }

        ShowScreen(Screen.Loading);

        FileUpdateProposal proposal;
        string encoding;
        try
        {
            (proposal, encoding) = await _captureService.GenerateOpenIssuesProposalAsync(classification, project, ct);
        }
        catch (OperationCanceledException)
        {
            ShowScreen(Screen.Confirm);
            return;
        }
        catch (Exception ex)
        {
            ShowScreen(Screen.Confirm);
            SetConfirmError($"Failed to generate proposal: {ex.Message}");
            return;
        }

        // リファイン用の状態
        var refineHistory     = new List<(string instruction, string result)>();
        var initialUserPrompt = proposal.DebugUserPrompt;
        var initialProposed   = proposal.ProposedContent;

        Func<string, string, Task<string>> refineFunc = async (_, instructions) =>
        {
            var refined = await _captureService.RefineTensionsAsync(
                initialUserPrompt, initialProposed, instructions, refineHistory, ct);
            refineHistory.Add((instructions, refined));
            return refined;
        };

        var (apply, content) = await ProposalReviewDialog.ShowAsync(
            this, proposal,
            titleText:  "Review Open Issue",
            titleIcon:  "⚡",
            refineFunc: refineFunc);

        if (!apply || content == null)
        {
            ShowScreen(Screen.Confirm);
            return;
        }

        var openIssuesPath = System.IO.Path.Combine(project.AiContextContentPath, "open_issues.md");
        try
        {
            await _captureService.WriteOpenIssuesAsync(openIssuesPath, content, encoding, ct);
        }
        catch (Exception ex)
        {
            ShowScreen(Screen.Confirm);
            SetConfirmError($"Failed to write: {ex.Message}");
            return;
        }

        // capture_log.md に副次記録
        var proj = $" [{classification.ProjectName}]";
        _ = _captureService.AppendCaptureLogEntryAsync(
            $"[open_issue]{proj} {classification.Summary}\n{_inputBox.Text}", ct);

        HandleRouteResult(new CaptureRouteResult
        {
            Success        = true,
            Message        = $"Added to {project.Name}/open_issues.md",
            TargetFilePath = openIssuesPath
        });
    }

    private async Task ShowTaskApprovalScreenAsync()
    {
        if (_classification == null) return;

        var selectedAsanaProject = _asanaProjectCombo.SelectedItem as AsanaProjectMeta;
        var selectedSection = _asanaSectionCombo.SelectedItem as AsanaSectionMeta;

        if (selectedAsanaProject == null)
        {
            SetConfirmError("Please select an Asana project.");
            return;
        }

        // section 整合性検証
        if (selectedSection != null && !string.IsNullOrWhiteSpace(selectedSection.Gid))
        {
            var valid = _asanaSections.Any(s => s.Gid == selectedSection.Gid);
            if (!valid)
            {
                SetConfirmError("Section does not belong to the selected project. Section cleared.");
                _asanaSectionCombo.SelectedIndex = 0;
                selectedSection = null;
            }
        }

        // payload 組み立て (最終確定値から)
        var notes = _classification.Body;
        var selectedDate = _dueDatePicker.SelectedDate;
        var hasTime = _setTimeCheckBox.IsChecked == true;

        if (hasTime && selectedDate == null)
        {
            SetConfirmError("Please select a due date when setting a time.");
            return;
        }

        var dueOn = "";
        var dueAt = "";
        if (selectedDate.HasValue)
        {
            if (hasTime)
            {
                var h = int.Parse((string)_hourCombo.SelectedItem!);
                var m = int.Parse((string)_minuteCombo.SelectedItem!);
                var dto = new DateTimeOffset(selectedDate.Value.Year, selectedDate.Value.Month, selectedDate.Value.Day,
                    h, m, 0, DateTimeOffset.Now.Offset);
                dueAt = dto.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz");
            }
            else
            {
                dueOn = selectedDate.Value.ToString("yyyy-MM-dd");
            }
        }

        var preview = new AsanaTaskCreatePreview
        {
            ProjectName = selectedAsanaProject.Name,
            ProjectGid = selectedAsanaProject.Gid,
            SectionName = selectedSection?.Name ?? "",
            SectionGid = selectedSection?.Gid ?? "",
            TaskName = _classification.Summary,
            Notes = notes,
            DueOn = dueOn,
            DueAt = dueAt
        };

        // request JSON 組み立て (機密除外)
        var requestData = new System.Collections.Generic.Dictionary<string, object>
        {
            ["name"] = preview.TaskName,
            ["notes"] = preview.Notes,
            ["projects"] = new[] { preview.ProjectGid }
        };
        if (!string.IsNullOrWhiteSpace(preview.DueAt))
            requestData["due_at"] = preview.DueAt;
        else if (!string.IsNullOrWhiteSpace(preview.DueOn))
            requestData["due_on"] = preview.DueOn;
        if (!string.IsNullOrWhiteSpace(preview.SectionGid))
            requestData["memberships"] = new[]
            {
                new System.Collections.Generic.Dictionary<string, string>
                {
                    ["project"] = preview.ProjectGid,
                    ["section"] = preview.SectionGid
                }
            };

        var requestJson = System.Text.Json.JsonSerializer.Serialize(
            new { data = requestData },
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

        var displayText = $"POST https://app.asana.com/api/1.0/tasks\n\n{requestJson}";
        if (!string.IsNullOrWhiteSpace(preview.SectionName))
            displayText += $"\n\n// Section: {preview.SectionName} ({preview.SectionGid})";

        preview.RequestJson = requestJson;

        // 承認画面に表示
        _requestPreviewBox.Text = displayText;

        // approval ボタンに preview を紐付け
        _approveBtn.Tag = preview;

        ShowScreen(Screen.TaskApproval);
    }

    private async void OnApproveClick(object? sender, RoutedEventArgs e)
    {
        if (_approveBtn.Tag is not AsanaTaskCreatePreview preview) return;

        _approveBtn.IsEnabled = false;
        _taskApprovalErrorText.Visibility = Visibility.Collapsed;
        _saveAsMemoBtn.Visibility = Visibility.Collapsed;

        var idempotencyKey = CaptureService.BuildIdempotencyKey(
            preview.ProjectGid, preview.TaskName, preview.Notes);

        _cts = new CancellationTokenSource();
        var result = await _captureService.CreateAsanaTaskAsync(preview, idempotencyKey, _cts.Token);

        _approveBtn.IsEnabled = true;

        if (!result.Success)
        {
            _taskApprovalErrorText.Text = result.Message;
            _taskApprovalErrorText.Visibility = Visibility.Visible;
            _approveBtn.Content = "Retry";
            _saveAsMemoBtn.Visibility = Visibility.Visible;
            return;
        }

        ShowCompleteScreen(result);
    }

    private async void OnSaveAsMemoClick(object? sender, RoutedEventArgs e)
    {
        if (_classification == null) return;

        _saveAsMemoBtn.IsEnabled = false;
        _cts = new CancellationTokenSource();
        try
        {
            var memoClassification = new CaptureClassification
            {
                Category = "memo",
                Summary = _classification.Summary,
                Body = _classification.Body
            };
            var result = await _captureService.RouteAsync(memoClassification, _inputBox.Text, _cts.Token);
            ShowCompleteScreen(result);
        }
        catch (Exception ex)
        {
            _taskApprovalErrorText.Text = $"Save as memo also failed: {ex.Message}";
        }
        finally
        {
            _saveAsMemoBtn.IsEnabled = true;
        }
    }

    private void OnConfirmCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_classification == null) return;
        var cat = _confirmCategoryCombo.SelectedItem as string ?? "memo";
        var isTask = cat == "task";
        _asanaProjectRow.Visibility = isTask ? Visibility.Visible : Visibility.Collapsed;
        _asanaSectionRow.Visibility = isTask ? Visibility.Visible : Visibility.Collapsed;
        _dueDateRow.Visibility = isTask ? Visibility.Visible : Visibility.Collapsed;
        if (!isTask) _timePickerRow.Visibility = Visibility.Collapsed;

        if (isTask)
            _ = LoadAsanaProjectsForCurrentProjectAsync();
    }

    private async void OnConfirmProjectChanged(object sender, SelectionChangedEventArgs e)
    {
        var cat = _confirmCategoryCombo.SelectedItem as string ?? "memo";
        if (cat == "task")
            await LoadAsanaProjectsForCurrentProjectAsync();
    }

    private async void OnAsanaProjectChanged(object sender, SelectionChangedEventArgs e)
    {
        var projectMeta = _asanaProjectCombo.SelectedItem as AsanaProjectMeta;
        if (projectMeta == null) return;

        _asanaSections = await _captureService.FetchSectionsAsync(projectMeta.Gid);
        PopulateAsanaSectionCombo(_classification?.AsanaSectionCandidateGid ?? "");
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e)
    {
        var result = (sender as System.Windows.Controls.Button)?.Tag as CaptureRouteResult;
        if (result?.TargetFilePath == null) return;

        if (_classification?.ProjectName is { } projName &&
            OnNavigateToFile != null)
        {
            var projects = _projects;
            var project = projects.FirstOrDefault(p =>
                string.Equals(p.Name, projName, StringComparison.OrdinalIgnoreCase));
            if (project != null)
                OnNavigateToFile(project.Name, result.TargetFilePath);
        }
        Close();
    }

    private void OnOpenAsanaClick(object sender, RoutedEventArgs e)
    {
        var url = (sender as System.Windows.Controls.Button)?.Tag as string;
        if (!string.IsNullOrWhiteSpace(url))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Screen transitions
    // ─────────────────────────────────────────────────────────────────────

    private void ShowScreen(Screen screen)
    {
        _currentScreen = screen;
        _inputPanel.Visibility = screen == Screen.Input ? Visibility.Visible : Visibility.Collapsed;
        _loadingPanel.Visibility = screen == Screen.Loading ? Visibility.Visible : Visibility.Collapsed;
        _confirmPanel.Visibility = screen == Screen.Confirm ? Visibility.Visible : Visibility.Collapsed;
        _taskApprovalPanel.Visibility = screen == Screen.TaskApproval ? Visibility.Visible : Visibility.Collapsed;
        _completePanel.Visibility = screen == Screen.Complete ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowConfirmScreen(string originalInput)
    {
        if (_classification == null) return;

        // 入力テキストプレビュー (2行分)
        var preview = originalInput.Length > 120 ? originalInput[..120] + "…" : originalInput;
        _confirmInputPreview.Text = preview;

        // 設定中はイベントを一時停止して LoadAsanaProjectsForCurrentProjectAsync の多重呼び出しを防ぐ
        _confirmCategoryCombo.SelectionChanged -= OnConfirmCategoryChanged;
        _confirmProjectCombo.SelectionChanged -= OnConfirmProjectChanged;

        // Category
        var catIdx = Array.IndexOf(["task", "tension", "focus_update", "decision", "memo"], _classification.Category);
        _confirmCategoryCombo.SelectedIndex = catIdx >= 0 ? catIdx : 4;

        // Project
        var projMatch = _projects.FirstOrDefault(p =>
            string.Equals(p.Name, _classification.ProjectName, StringComparison.OrdinalIgnoreCase));
        _confirmProjectCombo.SelectedItem = projMatch;

        // Summary
        _confirmSummaryBox.Text = _classification.Summary;

        // task / asana rows
        var isTask = _classification.Category == "task";
        _asanaProjectRow.Visibility = isTask ? Visibility.Visible : Visibility.Collapsed;
        _asanaSectionRow.Visibility = isTask ? Visibility.Visible : Visibility.Collapsed;
        _dueDateRow.Visibility = isTask ? Visibility.Visible : Visibility.Collapsed;
        _timePickerRow.Visibility = Visibility.Collapsed;

        // AI が提案した期限を初期値にセット
        if (isTask && !string.IsNullOrWhiteSpace(_classification.DueOn) &&
            System.DateTime.TryParse(_classification.DueOn, out var parsedDate))
            _dueDatePicker.SelectedDate = parsedDate;
        else
            _dueDatePicker.SelectedDate = null;
        _setTimeCheckBox.IsChecked = false;

        SetConfirmError("");

        // イベント再登録
        _confirmCategoryCombo.SelectionChanged += OnConfirmCategoryChanged;
        _confirmProjectCombo.SelectionChanged += OnConfirmProjectChanged;

        ShowScreen(Screen.Confirm);

        if (isTask)
            _ = LoadAsanaProjectsForCurrentProjectAsync();
    }

    private void ShowCompleteScreen(CaptureRouteResult result)
    {
        _completeMessageText.Foreground = result.Success ? AccentGreen : new SolidColorBrush(Colors.OrangeRed);
        _completeMessageText.Text = result.Success
            ? $"✓  {result.Message}"
            : $"✗  {result.Message}";

        _openFileBtn.Visibility = result.TargetFilePath != null ? Visibility.Visible : Visibility.Collapsed;
        _openFileBtn.Tag = result;

        _openAsanaBtn.Visibility = result.AsanaTaskUrl != null ? Visibility.Visible : Visibility.Collapsed;
        _openAsanaBtn.Tag = result.AsanaTaskUrl;

        ShowScreen(Screen.Complete);

        // focus_update / decision は Editor に遷移してウィンドウを閉じる
        if (result.RequiresNavigation)
        {
            if (result.NavigationFilePath != null && result.NavigationProjectName != null)
            {
                if (_classification?.Category == "focus_update" && OnNavigateToFocusUpdate != null)
                {
                    var capturedText = _classification.Body ?? _inputBox.Text;
                    OnNavigateToFocusUpdate(result.NavigationProjectName, result.NavigationFilePath, capturedText);
                }
                else
                    OnNavigateToFile?.Invoke(result.NavigationProjectName, result.NavigationFilePath);
            }
            else if (result.NavigationProjectName != null)
            {
                var capturedText = _classification?.Body ?? _inputBox.Text;
                OnNavigateToDecision?.Invoke(result.NavigationProjectName, capturedText);
            }
            Close();
        }
    }

    private void HandleRouteResult(CaptureRouteResult result)
    {
        if (result.RequiresNavigation)
        {
            ShowCompleteScreen(result);
            return;
        }
        ShowCompleteScreen(result);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private void PopulateProjectCombos()
    {
        // Input screen
        _projectCombo.Items.Clear();
        _projectCombo.Items.Add("Auto-detect");
        foreach (var p in _projects) _projectCombo.Items.Add(p);
        _projectCombo.SelectedIndex = 0;

        // Confirm screen
        _confirmProjectCombo.Items.Clear();
        _confirmProjectCombo.Items.Add("(not specified)");
        foreach (var p in _projects) _confirmProjectCombo.Items.Add(p);
        _confirmProjectCombo.SelectedIndex = 0;
    }

    private async Task LoadAsanaProjectsForCurrentProjectAsync()
    {
        var selectedProject = _confirmProjectCombo.SelectedItem as ProjectInfo;
        if (selectedProject == null)
        {
            _asanaProjectCombo.Items.Clear();
            _asanaProjectCombo.Items.Add(new AsanaProjectMeta { Gid = "", Name = "(select project first)" });
            _asanaProjectCombo.SelectedIndex = 0;
            return;
        }

        var (gids, wsMap) = _captureService.LoadAsanaProjectGids(selectedProject);

        if (gids.Count == 0)
        {
            _asanaProjectCombo.Items.Clear();
            _asanaProjectCombo.Items.Add(new AsanaProjectMeta { Gid = "", Name = "No Asana config" });
            _asanaProjectCombo.SelectedIndex = 0;
            _confirmErrorText.Text = "Asana project not configured. Check Asana Sync settings.";
            _confirmErrorText.Visibility = Visibility.Visible;
            return;
        }
        _confirmErrorText.Visibility = Visibility.Collapsed;

        // workstream 逆引き
        string? resolvedGid = null;
        if (!string.IsNullOrWhiteSpace(_classification?.WorkstreamHint) &&
            wsMap.TryGetValue(_classification.WorkstreamHint, out var wsGid))
            resolvedGid = wsGid;

        // AI 候補
        if (resolvedGid == null && !string.IsNullOrWhiteSpace(_classification?.AsanaProjectCandidateGid) &&
            gids.Contains(_classification.AsanaProjectCandidateGid))
            resolvedGid = _classification.AsanaProjectCandidateGid;

        // メタデータ取得
        _asanaProjects = [];
        _asanaProjectCombo.Items.Clear();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var gid in gids)
        {
            var meta = await _captureService.FetchProjectMetaAsync(gid, cts.Token) ??
                       new AsanaProjectMeta { Gid = gid, Name = "" };
            _asanaProjects.Add(meta);
            _asanaProjectCombo.Items.Add(meta);
        }

        _asanaProjectCombo.DisplayMemberPath = nameof(AsanaProjectMeta.DisplayLabel);

        // 自動選択
        if (resolvedGid != null)
        {
            var target = _asanaProjects.FirstOrDefault(p => p.Gid == resolvedGid);
            if (target != null) _asanaProjectCombo.SelectedItem = target;
        }
        else if (_asanaProjects.Count == 1)
        {
            _asanaProjectCombo.SelectedIndex = 0;
        }
    }

    private void PopulateAsanaSectionCombo(string candidateGid)
    {
        _asanaSectionCombo.Items.Clear();
        _asanaSectionCombo.Items.Add(new AsanaSectionMeta { Gid = "", Name = "(none)" });
        foreach (var s in _asanaSections)
            _asanaSectionCombo.Items.Add(s);
        _asanaSectionCombo.DisplayMemberPath = nameof(AsanaSectionMeta.DisplayLabel);
        _asanaSectionCombo.SelectedIndex = 0;

        if (!string.IsNullOrWhiteSpace(candidateGid))
        {
            var match = _asanaSections.FirstOrDefault(s => s.Gid == candidateGid);
            if (match != null) _asanaSectionCombo.SelectedItem = match;
        }
    }

    private void ResetToInputScreen()
    {
        _inputBox.Text = "";
        _classification = null;
        _captureBtn.IsEnabled = false;
        ShowScreen(Screen.Input);
        _inputBox.Focus();
    }

    private void SetConfirmError(string msg)
    {
        _confirmErrorText.Text = msg;
        _confirmErrorText.Visibility = string.IsNullOrWhiteSpace(msg) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PositionNearCursor()
    {
        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var workArea = screen.WorkingArea;

        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY;

        // カーソルのあるモニターの作業領域中央に表示
        Left = workArea.Left / scaleX + (workArea.Width / scaleX - Width) / 2;
        Top = workArea.Top / scaleY + (workArea.Height / scaleY - ActualHeight) / 2;
    }

    private static bool ContainsSensitivePattern(string text)
    {
        return Regex.IsMatch(text,
            @"(api[_-]?key|secret|token|bearer|password|passwd|pwd)\s*[:=]\s*\S+",
            RegexOptions.IgnoreCase);
    }

    private static bool IsValidDate(string s) =>
        System.DateTime.TryParseExact(s, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    // ── UI ファクトリメソッド ─────────────────────────────────────────────

    private System.Windows.Controls.Button BuildButton(string text, bool isPrimary, bool enabled = true)
    {
        var normalBg = isPrimary ? Accent : Surface1;
        var hoverBg  = isPrimary ? MakeLighter(Accent, 25) : Surface2;
        var pressBg  = isPrimary ? MakeDarker(Accent, 20)  : Surface2;
        var fg       = isPrimary ? WpfBrushes.Black : Text;
        var border   = isPrimary ? WpfBrushes.Transparent : Surface2;

        var btn = new System.Windows.Controls.Button
        {
            Content = text,
            MinWidth = 72,
            Height = 28,
            Padding = new Thickness(10, 0, 10, 0),
            FontSize = 12,
            IsEnabled = enabled,
            Foreground = fg,
            Background = normalBg,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            Cursor = WpfCursors.Hand,
            Template = BuildButtonTemplate(normalBg, hoverBg, pressBg, fg, border)
        };
        return btn;
    }

    /// <summary>WPF デフォルトのグレーホバーを上書きする最小 ControlTemplate。</summary>
    private static ControlTemplate BuildButtonTemplate(
        System.Windows.Media.Brush normalBg,
        System.Windows.Media.Brush hoverBg,
        System.Windows.Media.Brush pressBg,
        System.Windows.Media.Brush fg,
        System.Windows.Media.Brush borderBrush)
    {
        var template = new ControlTemplate(typeof(System.Windows.Controls.Button));

        var bdFactory = new FrameworkElementFactory(typeof(Border));
        bdFactory.Name = "bd";
        bdFactory.SetValue(Border.BackgroundProperty, normalBg);
        bdFactory.SetValue(Border.BorderBrushProperty, borderBrush);
        bdFactory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bdFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, WpfHA.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, WpfVA.Center);
        cp.SetValue(ContentPresenter.MarginProperty, new Thickness(10, 0, 10, 0));
        bdFactory.AppendChild(cp);

        template.VisualTree = bdFactory;

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "bd"));
        template.Triggers.Add(hoverTrigger);

        var pressTrigger = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true };
        pressTrigger.Setters.Add(new Setter(Border.BackgroundProperty, pressBg, "bd"));
        template.Triggers.Add(pressTrigger);

        var disabledTrigger = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabledTrigger.Setters.Add(new Setter(Border.OpacityProperty, 0.4, "bd"));
        template.Triggers.Add(disabledTrigger);

        return template;
    }

    private static System.Windows.Media.Brush MakeLighter(System.Windows.Media.Brush brush, byte delta)
    {
        if (brush is not SolidColorBrush scb) return brush;
        var c = scb.Color;
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A,
            (byte)Math.Min(255, c.R + delta),
            (byte)Math.Min(255, c.G + delta),
            (byte)Math.Min(255, c.B + delta)));
    }

    private static System.Windows.Media.Brush MakeDarker(System.Windows.Media.Brush brush, byte delta)
    {
        if (brush is not SolidColorBrush scb) return brush;
        var c = scb.Color;
        return new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A,
            (byte)Math.Max(0, c.R - delta),
            (byte)Math.Max(0, c.G - delta),
            (byte)Math.Max(0, c.B - delta)));
    }

    private System.Windows.Controls.ComboBox BuildComboBox(IEnumerable<string> items, int width)
    {
        var cb = new System.Windows.Controls.ComboBox
        {
            Background = Surface1,
            Foreground = Text,
            BorderBrush = Surface2,
            Padding = new Thickness(6, 4, 4, 4),
            FontSize = 12
        };
        if (width > 0) cb.Width = width;
        foreach (var item in items) cb.Items.Add(item);
        return cb;
    }

    private StackPanel BuildFieldRow(string label)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 5)
        };
        var lbl = new System.Windows.Controls.TextBlock
        {
            Text = label + ":",
            Foreground = Subtext,
            FontSize = 12,
            Width = 110,
            VerticalAlignment = WpfVA.Center
        };
        row.Children.Add(lbl);
        return row;
    }
}
