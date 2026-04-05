---
name: projectdesk-popup-window
description: Implement and refine Curia-specific WPF popup windows in this repository. Use when adding or updating modal dialogs from Dashboard/Editor/Timeline pages, especially when users request dark-mode popups, list/detail dialogs, folder-open actions, or visual consistency with existing DashboardPage code-behind popups (for example the Pin Work Folder picker).
---

# Curia Popup Window

Curia (WPF + wpf-ui) has an established popup implementation style in `Views/Pages/DashboardPage.xaml.cs`.

Use this skill to keep popup UI consistent, avoid common compile issues, and keep behavior aligned with existing dialogs.

## Workflow

1. Find and copy a known-good pattern
- Primary reference: `ShowPinFolderPickerDialogAsync` in `Views/Pages/DashboardPage.xaml.cs`.
- Reuse its structure: custom `Window`, title bar grid, content stack, footer buttons, `ShowDialog()`.

2. Keep text and labels in English
- All UI labels/tooltips/buttons/messages must be English.
- Avoid symbols that can render inconsistently; prefer plain text unless the app already uses that symbol pattern.

3. Use app theme resources only
- Background/foreground/border/accent must use dynamic resources (`AppSurface0`, `AppSurface1`, `AppSurface2`, `AppText`, `AppSubtext0`, accent like `AppPeach`).
- Do not hardcode colors for dialog shell/UI chrome.

4. Match dialog behavior conventions
- `WindowStyle = None`, draggable title bar (`DragMove()`), owner-centered dialog, `ShowInTaskbar = false`.
- Use `ShowDialog()` for modal interaction.
- `ResizeMode` should match purpose (usually `NoResize` for picker/detail modals).

5. Wire from XAML deliberately
- For clickable status text/badges, keep visual style lightweight if it sits near status text (for example Decision Log row).
- Add event handlers in XAML (`MouseLeftButtonUp`) and handle in code-behind with the card/data context checks.

6. Keep long work off UI thread
- Build repo/status lists with `await Task.Run(...)` and then bind/populate UI controls.

## Required Guardrails

### Compile-safe type usage in `DashboardPage.xaml.cs`

- `using WpfUserControl = System.Windows.Controls.UserControl;` already exists. Keep it.
- Prefer fully qualified types when ambiguity is possible:
  - `System.Windows.Controls.TextBlock`
  - `System.Windows.Controls.TextBox`
  - `System.Windows.Controls.ListBox`
  - `System.Windows.Media.Brush`
  - `System.Windows.Media.FontFamily`
  - `System.Windows.Media.Brushes`
- Avoid bare `Brush`, `TextBlock`, `TextBox`, `ListBox`, `FontFamily`, `Brushes` in ambiguous files.

### Data shown in list controls

- If list items are custom classes, always set display strategy:
  - `DisplayMemberPath = nameof(MyItem.DisplayLabel)`
  - or explicit `ItemTemplate`.
- Otherwise WPF shows type names like `Namespace.ClassName`.

### Action de-duplication

- If a footer button and inline link trigger the same action, keep one (prefer footer button for modal dialogs).

## Implementation Checklist

1. XAML placement/style
- Place clickable uncommitted indicator to the right of Decision Log count.
- Keep it text-like (status row style), not Tier/Domain badge style.
- Use `AppPeach` color direction and subtle hover affordance.

2. Event handler
- Add `OnUncommittedBadgeClick` in `DashboardPage.xaml.cs`.
- Safely get `ProjectCardViewModel` from sender data context.

3. Modal dialog shell
- Build dialog with `Grid` root + title/content/footer rows.
- Close button and footer close button both call `dialogWindow.Close()`.

4. Data binding/population
- Precompute list items (`Task.Run` if expensive).
- Populate `ListBox`, set `DisplayMemberPath`, select first item if exists.
- Update details panel on `SelectionChanged`.

5. Open-folder action
- Keep single `Open Folder` button.
- `Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });`
- Guard with `Directory.Exists(path)`.

6. Validate quickly
- Build/publish locally and inspect:
  - no garbled text
  - no type-name list display
  - modal sizing and close behavior are correct
  - uncommitted indicator appears only when needed

## Scope
- This skill is repository-specific for `C:\work\GenAI\convert\WpfManager`.
- Prefer updating existing dialog methods in `Views/Pages/DashboardPage.xaml.cs` over introducing new global dialog frameworks.
