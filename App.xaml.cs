using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui;
using ProjectCurator.Services;
using ProjectCurator.ViewModels;
using ProjectCurator.Views.Pages;
using AppPageService = ProjectCurator.Services.PageService;
using WpfApplication = System.Windows.Application;

namespace ProjectCurator;

public partial class App : WpfApplication
{
    private const string MutexName = "Global\\ProjectCurator_SingleInstance";
    private Mutex? _mutex;
    private IServiceProvider? _serviceProvider;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        // シングルインスタンス制御
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "ProjectCurator is already running.",
                "ProjectCurator",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        // DI コンテナ構築
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Standup タイマー起動
        var standup = _serviceProvider.GetRequiredService<StandupGeneratorService>();
        standup.StartScheduler();

        // Asana Sync スケジュールタイマー起動
        var asanaSync = _serviceProvider.GetRequiredService<AsanaSyncViewModel>();
        asanaSync.StartScheduler();

        // MainWindow 表示
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        // 画面外に配置してから Show() することで、DWM 初期化時のフラッシュを防ぐ。
        // OnLoaded で MoveOnScreen() が呼ばれ、画面中央に移動する。
        mainWindow.Left = -32000;
        mainWindow.Top = -32000;
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<ConfigService>();
        services.AddSingleton<HotkeyService>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<ScriptRunnerService>();
        services.AddSingleton<ProjectDiscoveryService>();
        services.AddSingleton<ContextCompressionLayerService>();
        services.AddSingleton<FileEncodingService>();
        services.AddSingleton<TodayQueueService>();
        services.AddSingleton<StandupGeneratorService>();
        services.AddSingleton<AsanaSyncService>();
        services.AddSingleton<LlmClientService>();
        services.AddSingleton<AsanaTaskParser>();
        services.AddSingleton<FocusUpdateService>();
        services.AddSingleton<DecisionLogGeneratorService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<TimelineViewModel>();
        services.AddSingleton<GitReposViewModel>();
        services.AddSingleton<AsanaSyncViewModel>();
        services.AddSingleton<SetupViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();

        // Pages
        services.AddSingleton<DashboardPage>();
        services.AddSingleton<EditorPage>();
        services.AddSingleton<TimelinePage>();
        services.AddSingleton<GitReposPage>();
        services.AddSingleton<AsanaSyncPage>();
        services.AddSingleton<SetupPage>();
        services.AddSingleton<SettingsPage>();

        // wpf-ui: IPageService for DI-based page resolution
        services.AddSingleton<IPageService, AppPageService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        // MainWindow
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();

        base.OnExit(e);
    }
}
