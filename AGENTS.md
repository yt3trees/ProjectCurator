# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ProjectCurator is a Windows desktop app (WPF + .NET 9) for managing multiple parallel projects from a system tray. Users toggle visibility with a global hotkey (Ctrl+Shift+P); normal window-close hides the app, Shift+Close exits it.

## Build & Publish

```bash
# Build
dotnet build

# Publish single-file executable to bin\Publish\
publish.cmd
# Equivalent: dotnet publish -p:PublishProfile=SingleFile
```

There are no automated tests in this repository.

Requirements: Windows, .NET 9 SDK, Git CLI, PowerShell 7+. Python 3.10+ is optional (Asana sync).

## Architecture

MVVM + Dependency Injection (`Microsoft.Extensions.DependencyInjection`). All services, ViewModels, and Pages are registered as singletons in `App.xaml.cs`.

### Key Layers

- `Models/` - Data structures (ProjectInfo, WorkstreamInfo, AppConfig, etc.)
- `Services/` - Business logic (singleton services injected into ViewModels)
- `ViewModels/` - CommunityToolkit.Mvvm with `[ObservableProperty]` / `[RelayCommand]`
- `Views/Pages/` - XAML pages; navigation controlled by `MainWindow.xaml.cs`

### Core Services

| Service | Responsibility |
|---|---|
| ConfigService | JSON config at `%USERPROFILE%\Documents\Projects\_config\` (settings.json, asana_global.json, etc.) |
| ProjectDiscoveryService | Recursively scans Local and Box project roots; 5-minute TTL cache |
| TodayQueueService | Reads/prioritizes tasks from `asana-tasks.md` files |
| AsanaSyncService | Syncs Asana API tasks to Markdown files |
| ContextCompressionLayerService | Manages AI context files (current_focus.md, decision_log, project_summary.md) |
| StandupGeneratorService | Generates daily standup Markdown on startup and hourly |
| HotkeyService | Registers global hotkey (Win32 P/Invoke) |
| TrayService | System tray icon via Windows Forms `NotifyIcon` |

### WPF-Specific Patterns

- Window visibility: the app moves the window to (-32000, -32000) instead of `Hide()` to avoid DWM re-initialization flashing. See `Win32Interop.cs` and `MainWindow.xaml.cs`.
- Page navigation uses wpf-ui 3.x `INavigationService`.
- Markdown editing uses AvalonEdit with `Assets/Markdown.xshd` for syntax highlighting.
- `GlobalUsings.cs` aliases WPF `Application` over WinForms to resolve namespace conflicts.

### Configuration

Runtime config is loaded from `%USERPROFILE%\Documents\Projects\_config\`. See `_config/` directory for `.example` templates:
- `settings.json` - workspace roots, hotkey, auto-refresh, Asana sync settings
- `asana_global.json` - Asana token, workspace/user GIDs, personal project GIDs

### Managed Folder Layout

```
Local Projects Root/
  MyProject/
    development/source/     # local-only repos
    shared/  (junction)     # → Box Projects Root/MyProject/
    _ai-context/
      context/ (junction)   # → Obsidian Vault/Projects/MyProject/ai-context/
```

Projects are classified by tier (`full`/`mini`) and category (`project`/`domain`), detected from path conventions.

## Popup / Dialog Windows

When adding modal dialogs, follow the patterns in `DashboardPage.xaml.cs` (canonical reference). See `.codex/skills/projectcurator-popup-window/SKILL.md` for detailed guidelines: theme resources, dark-mode support, WPF control conventions, and Win32 interop guardrails. Use English-only text in UI elements.

## NuGet Dependencies

- `wpf-ui` 3.x - Fluent Design controls and navigation
- `AvalonEdit` 6.x - Syntax-highlighted Markdown editor
- `CommunityToolkit.Mvvm` 8.x - MVVM source generators
- `Microsoft.Extensions.DependencyInjection` 9.x - DI container
