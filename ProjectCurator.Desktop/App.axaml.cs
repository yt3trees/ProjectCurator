using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ProjectCurator.Desktop.Services;
using ProjectCurator.Desktop.Views;
using ProjectCurator.Desktop.Views.Pages;
using ProjectCurator.Interfaces;
using ProjectCurator.Services;
using ProjectCurator.ViewModels;

namespace ProjectCurator.Desktop;

public class App : Application
{
    private static IServiceProvider? _services;
    private static Mutex? _mutex;

    public static IServiceProvider Services => _services ?? throw new InvalidOperationException("Services not initialized");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _mutex = new Mutex(true, "Global\\ProjectCurator_SingleInstance", out var isNew);
        if (!isNew)
        {
            // Another instance is already running - exit
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            return;
        }

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            desktopLifetime.MainWindow = _services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Platform services
        services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();

        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IShellService, WindowsShellService>();
            services.AddSingleton<IHotkeyService, WindowsHotkeyService>();
            services.AddSingleton<ITrayService, WindowsTrayService>();
        }
        else if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<IShellService, MacOSShellService>();
            services.AddSingleton<IHotkeyService, MacOSHotkeyService>();
            services.AddSingleton<ITrayService, MacOSTrayService>();
        }
        else
        {
            // Fallback stubs for Linux / other
            services.AddSingleton<IShellService, MacOSShellService>();
            services.AddSingleton<IHotkeyService, MacOSHotkeyService>();
            services.AddSingleton<ITrayService, MacOSTrayService>();
        }

        // Core services
        services.AddSingleton<ConfigService>();
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
        services.AddSingleton<CaptureService>();
        services.AddSingleton<MeetingNotesService>();
        services.AddSingleton<StateSnapshotService>();

        // ViewModels
        services.AddSingleton<TimelineViewModel>();
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<SetupViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<GitReposViewModel>();
        services.AddSingleton<AsanaSyncViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Views
        services.AddSingleton<MainWindow>();

        // Pages
        services.AddSingleton<DashboardPage>();
        services.AddSingleton<EditorPage>();
        services.AddSingleton<TimelinePage>();
        services.AddSingleton<GitReposPage>();
        services.AddSingleton<AsanaSyncPage>();
        services.AddSingleton<SetupPage>();
        services.AddSingleton<SettingsPage>();
    }
}
