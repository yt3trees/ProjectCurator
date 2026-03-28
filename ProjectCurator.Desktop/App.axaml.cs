using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ProjectCurator.Desktop.Services;
using ProjectCurator.Desktop.Views;
using ProjectCurator.Desktop.Views.Pages;
using ProjectCurator.Interfaces;
using ProjectCurator.Services;
using ProjectCurator.ViewModels;
using System.Windows.Input;

namespace ProjectCurator.Desktop;

public partial class App : Application
{
    private static IServiceProvider? _services;
    private static Mutex? _mutex;
    public static bool ExitRequested { get; private set; }

    public static IServiceProvider Services => _services ?? throw new InvalidOperationException("Services not initialized");

    // Tray icon commands (bound via TrayIcon.Icons in App.axaml)
    public ICommand ShowWindowCommand { get; }
    public ICommand QuickCaptureCommand { get; }
    public ICommand ExitCommand { get; }
    public static readonly StyledProperty<string> HotkeyMenuHeaderProperty =
        AvaloniaProperty.Register<App, string>(nameof(HotkeyMenuHeader), "Hotkey: (none)");

    public string HotkeyMenuHeader
    {
        get => GetValue(HotkeyMenuHeaderProperty);
        private set => SetValue(HotkeyMenuHeaderProperty, value);
    }

    public App()
    {
        ShowWindowCommand = new RelayCommand(ShowWindow);
        QuickCaptureCommand = new RelayCommand(QuickCapture);
        ExitCommand = new RelayCommand(Exit);
        DataContext = this;
    }

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

            var trayService = _services.GetRequiredService<ITrayService>();
            var hotkeyService = _services.GetRequiredService<IHotkeyService>();
            trayService.UpdateHotkeyDisplay(hotkeyService.HotkeyDisplayText);

            // Start background schedulers on app startup (same behavior as WPF)
            _services.GetRequiredService<StandupGeneratorService>().StartScheduler();
            _services.GetRequiredService<AsanaSyncViewModel>().StartScheduler();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.MainWindow;
            if (window != null) { window.Show(); window.Activate(); }
        }
    }

    private void OnTrayIconClicked(object? sender, EventArgs e)
        => ShowWindow();

    private void QuickCapture()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        if (desktop.MainWindow is MainWindow mainWindow)
            mainWindow.ShowCaptureWindow();
    }

    private void Exit()
    {
        ExitRequested = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is MainWindow mw)
                mw.RequestExit();
            else
                desktop.Shutdown();
        }
    }

    public void UpdateTrayHotkeyDisplay(string hotkeyText)
    {
        HotkeyMenuHeader = $"Hotkey: {hotkeyText}";
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

    // Minimal ICommand implementation (no CommunityToolkit.Mvvm dependency on App class)
    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
#pragma warning disable CS0067 // CanExecuteChanged is never raised (commands are always executable)
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
