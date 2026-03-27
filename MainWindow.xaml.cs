using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;
using Wpf.Ui.Controls;
using ProjectCurator.Models;
using ProjectCurator.Services;
using ProjectCurator.ViewModels;
using ProjectCurator.Views;
using ProjectCurator.Views.Pages;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using WpfKey = System.Windows.Input.Key;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfApplication = System.Windows.Application;

namespace ProjectCurator;

public partial class MainWindow : FluentWindow
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HotkeyService _hotkeyService;
    private readonly TrayService _trayService;
    private readonly MainWindowViewModel _viewModel;
    private readonly IPageService _pageService;
    private readonly IContentDialogService _contentDialogService;
    private readonly ConfigService _configService;
    private readonly CaptureService _captureService;
    private IntPtr _hwnd;
    private CaptureWindow? _activeCaptureWindow;

    // ウィンドウが画面上に表示されているかのフラグ (Hide() を使わない方式)
    private bool _isShownOnScreen = false;

    public MainWindow(
        IServiceProvider serviceProvider,
        HotkeyService hotkeyService,
        TrayService trayService,
        MainWindowViewModel viewModel,
        IPageService pageService,
        IContentDialogService contentDialogService,
        ConfigService configService,
        CaptureService captureService)
    {
        _serviceProvider = serviceProvider;
        _hotkeyService = hotkeyService;
        _trayService = trayService;
        _viewModel = viewModel;
        _pageService = pageService;
        _contentDialogService = contentDialogService;
        _configService = configService;
        _captureService = captureService;

        DataContext = _viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
    }

    // ── Win32: DWM 補助設定 ───────────────────────────────────────────────
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLongW")]
    private static extern uint SetClassLong32(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private const int GCLP_HBRBACKGROUND = -10;
    private const int WM_ERASEBKGND = 0x0014;
    private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(_hwnd);
        if (source?.CompositionTarget != null)
            source.CompositionTarget.BackgroundColor = System.Windows.Media.Color.FromRgb(0x0d, 0x11, 0x17);
        source?.AddHook(WndProc);
        if (IntPtr.Size == 8)
            SetClassLongPtr64(_hwnd, GCLP_HBRBACKGROUND, IntPtr.Zero);
        else
            SetClassLong32(_hwnd, GCLP_HBRBACKGROUND, 0);
        var disabled = 1;
        DwmSetWindowAttribute(_hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref disabled, sizeof(int));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ERASEBKGND)
        {
            handled = true;
            return new IntPtr(1);
        }
        return IntPtr.Zero;
    }

    // ── フラッシュ防止: 画面外移動方式 ───────────────────────────────────
    //
    // 方針:
    //   - Hide()/Show() を一切使わない。Hide() は DWM のサーフェスを再初期化するため。
    //   - 代わりにウィンドウを画面外 (-32000, -32000) に移動して「隠す」。
    //   - サーフェスは常に生き続けるので Show() 時の DWM 再初期化フラッシュが起きない。
    //   - FlashBlocker (WPF Grid) でコンテンツを覆い、ウィンドウ移動中の描画を隠す。
    //   - 起動時は App.xaml.cs で画面外位置を設定し、OnLoaded 後に画面中央へ移動する。

    private const double OffScreenX = -32000;
    private const double OffScreenY = -32000;

    /// <summary>
    /// 1フレーム待って FlashBlocker を描画してからウィンドウを画面外に移動する。
    /// </summary>
    private void MoveOffScreen()
    {
        // 画面外に移動する前にウィンドウ位置・サイズを保存する
        if (_isShownOnScreen)
        {
            _configService.SaveWindowPlacement(new Models.WindowPlacement
            {
                Left = Left,
                Top = Top,
                Width = 0,   // サイズは保存しない (再起動後は固定値に戻す)
                Height = 0,
            });
        }

        FlashBlocker.Visibility = Visibility.Visible;
        System.Windows.Media.CompositionTarget.Rendering -= OnFlashBlockerRenderedThenMoveOff;
        System.Windows.Media.CompositionTarget.Rendering += OnFlashBlockerRenderedThenMoveOff;
    }

    private void OnFlashBlockerRenderedThenMoveOff(object? sender, EventArgs e)
    {
        System.Windows.Media.CompositionTarget.Rendering -= OnFlashBlockerRenderedThenMoveOff;
        _isShownOnScreen = false;
        Left = OffScreenX;
        Top = OffScreenY;
    }

    /// <summary>
    /// ウィンドウを画面中央へ移動し、次フレームで FlashBlocker を非表示にする。
    /// </summary>
    private void MoveOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        var saved = _configService.LoadWindowPlacement();

        // サイズは再起動後も固定値に戻す (XAML の Width/Height をそのまま使う)
        // ウィンドウが作業領域より大きい場合はサイズを縮小する (MinWidth/MinHeight は守る)
        if (Width > workArea.Width)
            Width = Math.Max(MinWidth, workArea.Width);
        if (Height > workArea.Height)
            Height = Math.Max(MinHeight, workArea.Height);

        if (saved != null)
        {
            // 保存された位置が作業領域内に収まるよう調整する
            Left = Math.Max(workArea.Left, Math.Min(saved.Left, workArea.Right - Width));
            Top = Math.Max(workArea.Top, Math.Min(saved.Top, workArea.Bottom - Height));
        }
        else
        {
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + (workArea.Height - Height) / 2;
        }
        _isShownOnScreen = true;
        WindowState = WindowState.Normal;
        Activate();
        Focus();
        System.Windows.Media.CompositionTarget.Rendering -= OnFlashBlockerCollapse;
        System.Windows.Media.CompositionTarget.Rendering += OnFlashBlockerCollapse;
    }

    private void OnFlashBlockerCollapse(object? sender, EventArgs e)
    {
        System.Windows.Media.CompositionTarget.Rendering -= OnFlashBlockerCollapse;
        FlashBlocker.Visibility = Visibility.Collapsed;
    }

    // ──────────────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // wpf-ui ContentDialogService に親要素を設定
        _contentDialogService.SetDialogHost(RootContentDialogPresenter);

        // wpf-ui NavigationView に PageService を設定 (DI対応ページ生成)
        RootNavigation.SetPageService(_pageService);

        // ページ遷移時にステータスバーの表示/非表示を切り替える
        RootNavigation.Navigated += (_, args) =>
        {
            _viewModel.IsEditorActive = args.Page is EditorPage;
        };

        // ホットキー登録
        _hotkeyService.OnActivated = ToggleVisibility;
        _hotkeyService.OnCaptureActivated = ShowCaptureWindow;
        _hotkeyService.Register(this);

        // トレイアイコン初期化
        _trayService.OnActivated = ToggleVisibility;
        _trayService.OnCaptureActivated = ShowCaptureWindow;
        _trayService.Initialize(this);

        // ウィンドウアイコンをトレイアイコン (ダイヤモンド) と統一
        if (_trayService.DiamondBitmapSource != null)
        {
            this.Icon = _trayService.DiamondBitmapSource;
            RootTitleBar.Icon = new Wpf.Ui.Controls.ImageIcon
            {
                Source = _trayService.DiamondBitmapSource,
                Width = 14,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            };
        }
        _trayService.UpdateHotkeyDisplay(_hotkeyService.HotkeyDisplayText);

        // デフォルトページを DashboardPage に設定
        RootNavigation.Navigate(typeof(DashboardPage));

        // 起動時: App.xaml.cs で画面外位置に設定済み。
        // OnLoaded で WPF が描画完了後、ウィンドウを画面中央に移動して表示する。
        MoveOnScreen();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (WpfKeyboard.Modifiers.HasFlag(WpfModifierKeys.Shift))
        {
            _hotkeyService.Unregister();
            _trayService.Dispose();
            WpfApplication.Current.Shutdown();
        }
        else
        {
            e.Cancel = true;
            MoveOffScreen();
        }
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfKey.Escape)
        {
            MoveOffScreen();
            e.Handled = true;
            return;
        }

        if (WpfKeyboard.Modifiers == WpfModifierKeys.Control)
        {
            switch (e.Key)
            {
                case WpfKey.D1:
                    RootNavigation.Navigate(typeof(DashboardPage));
                    e.Handled = true;
                    break;
                case WpfKey.D2:
                    RootNavigation.Navigate(typeof(EditorPage));
                    e.Handled = true;
                    break;
                case WpfKey.D3:
                    RootNavigation.Navigate(typeof(TimelinePage));
                    e.Handled = true;
                    break;
                case WpfKey.D4:
                    RootNavigation.Navigate(typeof(GitReposPage));
                    e.Handled = true;
                    break;
                case WpfKey.D5:
                    RootNavigation.Navigate(typeof(AsanaSyncPage));
                    e.Handled = true;
                    break;
                case WpfKey.D6:
                    RootNavigation.Navigate(typeof(SetupPage));
                    e.Handled = true;
                    break;
                case WpfKey.D7:
                    RootNavigation.Navigate(typeof(SettingsPage));
                    e.Handled = true;
                    break;
                case WpfKey.K:
                    _viewModel.CommandPaletteViewModel.Show();
                    e.Handled = true;
                    break;
            }
        }
    }

    public void ToggleVisibility()
    {
        if (_isShownOnScreen && IsActive)
        {
            // FlashBlocker を1フレーム描画してからウィンドウを画面外に移動
            MoveOffScreen();
        }
        else
        {
            // ウィンドウを画面中央に移動し、次フレームで FlashBlocker を非表示
            MoveOnScreen();
        }
    }

    /// <summary>
    /// DashboardPage からEditorPageへ遷移し、指定プロジェクトを選択する。
    /// </summary>
    public void NavigateToEditor(ProjectInfo project)
    {
        var editorVm = _serviceProvider.GetRequiredService<EditorViewModel>();
        var match = editorVm.Projects.FirstOrDefault(p => p.HiddenKey == project.HiddenKey);
        editorVm.SelectedProject = match ?? project;
        RootNavigation.Navigate(typeof(EditorPage));
    }

    public async Task NavigateToDashboardAndShowBriefingAsync(ProjectInfo project)
    {
        RootNavigation.Navigate(typeof(DashboardPage));
        await Dispatcher.InvokeAsync(async () =>
        {
            var dashboard = _serviceProvider.GetRequiredService<DashboardPage>();
            await dashboard.ShowBriefingForProjectAsync(project);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    public void NavigateToSettings()
    {
        RootNavigation.Navigate(typeof(SettingsPage));
    }

    /// <summary>
    /// DashboardPage から TimelinePage へ遷移し、指定プロジェクトを選択する。
    /// </summary>
    public void NavigateToTimeline(ProjectInfo project)
    {
        var timelineVm = _serviceProvider.GetRequiredService<TimelineViewModel>();
        timelineVm.NavigateToProjectKey = project.HiddenKey;
        RootNavigation.Navigate(typeof(TimelinePage));
    }

    /// <summary>
    /// TimelinePage からEditorPageへ遷移し、指定プロジェクト + ファイルを開く。
    /// </summary>
    public void NavigateToEditorAndOpenFile(ProjectInfo project, string filePath)
    {
        var editorVm = _serviceProvider.GetRequiredService<EditorViewModel>();
        var match = editorVm.Projects.FirstOrDefault(p => p.HiddenKey == project.HiddenKey);
        editorVm.NavigateToProjectAndOpenFile(match ?? project, filePath);
        RootNavigation.Navigate(typeof(EditorPage));
    }

    /// <summary>
    /// Quick Capture の decision ルーティング専用。
    /// Editor に遷移後、AI Decision Log フローを自動発火する。
    /// </summary>
    private void NavigateToEditorAndTriggerDecision(ProjectInfo project, string capturedText)
    {
        var editorVm = _serviceProvider.GetRequiredService<EditorViewModel>();
        var match = editorVm.Projects.FirstOrDefault(p => p.HiddenKey == project.HiddenKey);
        editorVm.SelectedProject = match ?? project;
        editorVm.RequestDecisionLogOnOpen(capturedText);
        RootNavigation.Navigate(typeof(EditorPage));
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new System.Action(async () => await editorVm.TriggerDecisionLogIfPendingAsync()));
    }

    /// <summary>
    /// Quick Capture の focus_update ルーティング専用。
    /// エディタを指定ファイルで開いた後、Update Focus LLM フローを自動発火する。
    /// </summary>
    private void NavigateToEditorAndTriggerFocusUpdate(ProjectInfo project, string filePath, string capturedText)
    {
        var editorVm = _serviceProvider.GetRequiredService<EditorViewModel>();
        var match = editorVm.Projects.FirstOrDefault(p => p.HiddenKey == project.HiddenKey);
        editorVm.RequestFocusUpdateOnOpen(capturedText);
        editorVm.NavigateToProjectAndOpenFile(match ?? project, filePath);
        RootNavigation.Navigate(typeof(EditorPage));
        // ファイル読み込み・LoadProjectsAsync が落ち着いてからトリガー (OpenFileAndSelectNodeAsync 側の
        // トリガーが何らかの理由でミスした場合のフォールバック)
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new System.Action(() => editorVm.TriggerFocusUpdateIfPending()));
    }

    /// <summary>
    /// SettingsPage からホットキー変更時にトレイアイコンの表示を更新する。
    /// </summary>
    public void UpdateTrayHotkeyDisplay(string displayText)
    {
        _trayService.UpdateHotkeyDisplay(displayText);
    }

    /// <summary>
    /// キャプチャホットキーが押されたときに CaptureWindow を表示する。
    /// </summary>
    private void ShowCaptureWindow()
    {
        if (_activeCaptureWindow != null)
        {
            _activeCaptureWindow.Activate();
            return;
        }

        var discoveryService = _serviceProvider.GetRequiredService<ProjectDiscoveryService>();
        var captureWindow = new CaptureWindow(_captureService, discoveryService, _configService);
        captureWindow.Owner = this;
        captureWindow.Closed += (_, _) => _activeCaptureWindow = null;
        _activeCaptureWindow = captureWindow;

        captureWindow.OnNavigateToFile = (projectName, filePath) =>
        {
            var project = ResolveProject(projectName);
            if (project != null)
                NavigateToEditorAndOpenFile(project, filePath);
            MoveOnScreen();
        };

        captureWindow.OnNavigateToFocusUpdate = (projectName, filePath, capturedText) =>
        {
            var project = ResolveProject(projectName);
            if (project != null)
                NavigateToEditorAndTriggerFocusUpdate(project, filePath, capturedText);
            MoveOnScreen();
        };

        captureWindow.OnNavigateToDecision = (projectName, capturedText) =>
        {
            var project = ResolveProject(projectName);
            if (project != null)
                NavigateToEditorAndTriggerDecision(project, capturedText);
            MoveOnScreen();
        };

        captureWindow.ShowDialog();
    }

    /// <summary>
    /// プロジェクト名でプロジェクトを解決する。
    /// EditorViewModel が未ロードの場合は ProjectDiscoveryService のキャッシュから取得する。
    /// </summary>
    private ProjectInfo? ResolveProject(string projectName)
    {
        var editorVm = _serviceProvider.GetRequiredService<EditorViewModel>();
        var project = editorVm.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));

        if (project == null)
        {
            var discoveryService = _serviceProvider.GetRequiredService<ProjectDiscoveryService>();
            var allProjects = discoveryService.GetProjectInfoList();
            project = allProjects.FirstOrDefault(p =>
                string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        }

        return project;
    }
}
