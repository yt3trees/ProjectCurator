# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ProjectCurator is a cross-platform desktop app (.NET 9, Windows/macOS) for managing multiple parallel projects from a system tray. Users toggle visibility with a global hotkey (Ctrl+Shift+P on Windows, Cmd+Shift+P on macOS); normal window-close hides the app.

The codebase is in active migration from WPF (Windows-only) to Avalonia UI (cross-platform). The primary target is `ProjectCurator.Desktop` (Avalonia). The legacy `ProjectCurator.csproj` (WPF) is kept for reference.

## Solution Structure

```
ProjectCurator.sln
  +-- ProjectCurator.Core/         (net9.0, UI-agnostic shared library)
  +-- ProjectCurator.Desktop/      (net9.0, Avalonia UI host)
  +-- ProjectCurator.csproj        (net9.0-windows, WPF legacy - reference only)
```

## Build & Publish

```bash
# Build solution
dotnet build

# Build Avalonia Desktop only
dotnet build ProjectCurator.Desktop/ProjectCurator.Desktop.csproj

# Publish Windows (single-file)
publish.cmd

# Publish macOS
bash publish-macos.sh
```

For build verification, use `dotnet build ProjectCurator.Desktop/ProjectCurator.Desktop.csproj`.

There are no automated tests in this repository.

Requirements: .NET 9 SDK, Git CLI, PowerShell 7+ (Windows). Python 3.10+ is optional (Asana sync).

## Architecture

MVVM + Dependency Injection (`Microsoft.Extensions.DependencyInjection`). All services, ViewModels, and Pages are registered as singletons in `ProjectCurator.Desktop/App.axaml.cs`.

### Key Layers

- `ProjectCurator.Core/Models/` - Data structures (ProjectInfo, WorkstreamInfo, AppConfig, etc.)
- `ProjectCurator.Core/Services/` - Business logic (singleton services injected into ViewModels)
- `ProjectCurator.Core/ViewModels/` - CommunityToolkit.Mvvm with `[ObservableProperty]` / `[RelayCommand]`
- `ProjectCurator.Core/Interfaces/` - Platform abstraction interfaces (IShellService, IDispatcherService, etc.)
- `ProjectCurator.Desktop/Views/Pages/` - AXAML pages; navigation controlled by `MainWindow.axaml.cs`
- `ProjectCurator.Desktop/Services/` - Platform-specific service implementations

### Platform Abstraction Interfaces

All platform-specific operations go through these interfaces (defined in `ProjectCurator.Core/Interfaces/`):

| Interface | Responsibility |
|---|---|
| `IDispatcherService` | UI thread dispatch (Post, Invoke, InvokeAsync) |
| `IShellService` | OS operations: OpenFolder, OpenFile, OpenTerminal, CreateSymlink, IsSymlink, RunShellScriptAsync, SetStartupEnabled |
| `IDialogService` | Modal dialogs: ShowMessageAsync, ShowConfirmAsync |
| `ITrayService` | System tray: Initialize, UpdateHotkeyDisplay, ShowNotification |
| `IHotkeyService` | Global hotkey registration and callbacks |

### Platform Implementations

Windows (`ProjectCurator.Desktop/Services/`):
- `AvaloniaDispatcherService` - `Avalonia.Threading.Dispatcher.UIThread`
- `AvaloniaDialogService` - FluentAvalonia ContentDialog
- `WindowsHotkeyService` - Win32 P/Invoke `RegisterHotKey`
- `WindowsShellService` - explorer.exe, wt.exe, PowerShell Junction creation
- `WindowsTrayService` - Avalonia TrayIcon

macOS (`ProjectCurator.Desktop/Services/`):
- `MacOSHotkeyService` - SharpHook `TaskPoolGlobalHook` (requires Accessibility permission)
- `MacOSShellService` - `open` command, `Directory.CreateSymbolicLink()`, LaunchAgent plist
- `MacOSTrayService` - Avalonia TrayIcon (maps to NSStatusItem)

### Core Services

| Service | Responsibility |
|---|---|
| ConfigService | JSON config at `%USERPROFILE%\Documents\Projects\_config\` (settings.json, asana_global.json, hidden_projects.json, pinned_folders.json) |
| ProjectDiscoveryService | Recursively scans Local and Box project roots; 5-minute TTL cache; detects symlink status, focus age, decision log count, uncommitted git changes; uses `IShellService` for symlink ops |
| TodayQueueService | Reads/prioritizes tasks from `asana-tasks.md` into overdue/today/soon/normal buckets |
| AsanaSyncService | Syncs Asana API tasks to Markdown files; maps workstreams via asana_config.json per project |
| ContextCompressionLayerService | Manages AI context files; creates symlinks via `IShellService.CreateSymlink` |
| StandupGeneratorService | Generates daily standup Markdown on startup and hourly (6am+) |
| FileEncodingService | Async file I/O with BOM-based encoding detection (UTF-8/UTF-8BOM/SJIS/UTF-16); preserves encoding on write |
| ScriptRunnerService | Runs PowerShell/Python scripts async; dispatches output via `IDispatcherService`; cancellable |
| LlmClientService | Sends chat completion requests to OpenAI or Azure OpenAI |
| FocusUpdateService | Orchestrates the "Update Focus from Asana" flow |

### Avalonia-Specific Patterns

- Window visibility: `window.Show()` / `window.Hide()` (no DWM flash issue unlike WPF).
- Page navigation: `MainWindow.axaml.cs` handles `NavigationView.SelectionChanged`; stores `_currentPage` reference. Cross-page navigation uses ViewModel callbacks (`OnOpenInEditor`, `OnOpenInTimeline`).
- Keyboard shortcuts: handled in `MainWindow.OnKeyDown` — Ctrl+1-7 for pages, Ctrl+K for command palette, Escape to hide, Ctrl+S to save in editor.
- Markdown editing: AvaloniaEdit (`Avalonia.AvaloniaEdit`) with `Assets/Markdown.xshd` for syntax highlighting. `DiffLineBackgroundRenderer` implements `IBackgroundRenderer` for diff highlight.
- Cross-ViewModel communication: CommunityToolkit.Mvvm `WeakReferenceMessenger`. `StatusUpdateMessage` broadcasts editor state; `AiEnabledChangedMessage` toggles AI features.

### Configuration

Runtime config is loaded from `%USERPROFILE%\Documents\Projects\_config\`. See `_config/` directory for `.example` templates:
- `settings.json` - workspace roots, hotkey, auto-refresh, Asana sync settings, and LLM/AI settings (`LlmProvider`, `LlmApiKey`, `LlmModel`, `LlmEndpoint`, `LlmApiVersion`, `AiEnabled`)
- `asana_global.json` - Asana token, workspace/user GIDs, personal project GIDs

LLM settings are configured in `Settings > LLM API`. Supported providers are `openai` and `azure_openai`. "Enable AI Features" can only be turned on after a successful Test Connection.

### Managed Folder Layout

```
Local Projects Root/
  MyProject/
    development/source/     # local-only repos
    shared/  (symlink)      # -> Box Projects Root/MyProject/
    _ai-context/
      context/ (symlink)    # -> Obsidian Vault/Projects/MyProject/ai-context/
```

Projects are classified by tier (`full`/`mini`) and category (`project`/`domain`), detected from path conventions.

## AI Features

New AI-powered features must follow these patterns.

### Gating

- Never call `LlmClientService` unless `settings.AiEnabled` is `true`.
- In each ViewModel that exposes an AI action, declare an `[ObservableProperty] bool isAiEnabled` and initialize it from settings. Bind UI button/control visibility to this property.
- React to real-time toggle changes by registering for `AiEnabledChangedMessage` in the ViewModel constructor:

```csharp
IsAiEnabled = _configService.LoadSettings().AiEnabled;
WeakReferenceMessenger.Default.Register<AiEnabledChangedMessage>(this,
    (_, msg) => IsAiEnabled = msg.Enabled);
```

### LLM Calls

- All LLM API calls go through `LlmClientService` — never make direct HTTP calls to LLM providers.
- Use `ChatCompletionAsync` for single-turn (system + user); use `ChatWithHistoryAsync` for multi-turn with conversation history.
- `LlmClientService` throws `InvalidOperationException` if the API key is not configured — callers do not need to re-check the key themselves.
- Services orchestrating LLM workflows (e.g., `FocusUpdateService`) must accept a `CancellationToken` and pass it through to `LlmClientService`.

### Settings

- `AiEnabled` can only be set to `true` after a successful `TestLlmConnectionAsync`. Enforce this by binding the toggle's `IsEnabled` to `AiToggleCanEnable` (see `SettingsViewModel` pattern).
- When `AiEnabled` changes, persist immediately to settings and broadcast `AiEnabledChangedMessage` via `WeakReferenceMessenger` (see `SettingsViewModel.OnAiEnabledChanged`).

## Popup / Dialog Windows (Avalonia)

Use `IDialogService.ShowMessageAsync` / `ShowConfirmAsync` for simple dialogs. For complex dialogs, create a new `Window` with AXAML and show it with `dialog.ShowDialog(parentWindow)`.

Use English-only text in UI elements.

## NuGet Dependencies

### ProjectCurator.Core
- `CommunityToolkit.Mvvm` 8.x - MVVM source generators
- `Microsoft.Extensions.DependencyInjection.Abstractions` 9.x - DI abstractions
- `DiffPlex` 1.x - Line-level diff computation

### ProjectCurator.Desktop
- `Avalonia` 11.x - Cross-platform UI framework
- `Avalonia.Desktop` 11.x - Desktop integration
- `FluentAvaloniaUI` 2.x - Fluent Design controls (NavigationView, ContentDialog, etc.)
- `Avalonia.AvaloniaEdit` 11.x - Syntax-highlighted code/Markdown editor
- `SharpHook` 5.x - Cross-platform global hotkey (macOS uses Accessibility API)
- `CommunityToolkit.Mvvm` 8.x - MVVM source generators
- `Microsoft.Extensions.DependencyInjection` 9.x - DI container
