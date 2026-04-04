# UI Guide

[< Back to README](../README.md)

## Dashboard

Overview of all projects with health indicators, update freshness, and Today Queue at the bottom.

![](../_assets/Dashboard.png)

![](../_assets/Dashboard-Card.png)

This is the standard project card view with health signals, repository status, and quick actions.

- Use the top bar to refresh the view, set auto refresh (Off / 10 / 15 / 30 / 60 min), and show hidden projects.
- Each project card gives a quick health check: project name, tier (FULL/MINI), optional DOMAIN tag, link status dots, decision log count, and uncommitted repo count.
- Click the uncommitted badge to see repository-by-repository change details.
- `Focus` and `Summary` show how old each file is (in days), and the background color changes as files get older.
- The 30-day mini activity bar is clickable and opens Timeline.
- Card buttons help you move straight into work: open folder, open terminal (or launch Claude/Gemini/Codex), open Editor, and pin work folders.
- Workstreams can be expanded per project. From each row, you can open `current_focus.md`, open the workstream `_work` folder, create today's work folder, or pin a recent folder.
- `Pinned Folders` appears when you pin at least one folder. You can open, unpin, drag to reorder, or clear all pins.
- `Today Queue` reads unchecked tasks from `asana-tasks.md` files and shows them by urgency (Overdue, Today, In Nd, No due).
- In each Today Queue row, you can open the task in Asana, snooze it until tomorrow, or mark it done in Asana.
- Today Queue also has project/workstream filters, Show All (Top 10 vs up to 100), unsnooze all, manual refresh, and fixed/resizable height mode.

### AI Features on Dashboard

<img src="../_assets/ai-feature/WhatsNext.png" width="60%" alt="What's Next dialog" />

When AI Features is enabled, the What's Next button in the top bar shows 3-5 prioritized actions across all projects, with an `Open` button for direct navigation and `Copy` for plain-text export.

<img src="../_assets/ai-feature/ContextBriefing.png" width="60%" alt="Context Briefing dialog" />

When AI Features is enabled, each project card also shows a Briefing button that generates a project-specific context-switch summary (Where you left off / Suggested next steps / Key context) with `Copy`, `Open in Editor`, and `View Debug`.

<img src="../_assets/ai-feature/TodaysPlan.png" width="60%" alt="Today's Plan dialog" />

Today's Plan dialog (AI) provides a time-blocked day plan (for example, Morning / Afternoon), with `Open`, `Copy`, `Save`, and `View Debug` actions.

## Editor

Tree-based file browser for AI context files (`current_focus.md`, `decision_log`, etc.) with syntax-highlighted Markdown editing.

![](../_assets/Editor.png)

- Project selector dropdown at the top left to switch between projects
- Tree view on the left lists AI context files: `current_focus.md`, `file_map.md`, `project_summary.md`, `open_issues.md`, `decision_log/`, `focus_history/`, `obsidian_notes/`, `workstreams/`, `CLAUDE.md`, `AGENTS.md`
- Syntax-highlighted Markdown editor on the right with section-based coloring
- Toolbar buttons: Refresh, Dec Log (quick decision log entry), P (pin folder), Save
- Full file path displayed in the header bar
- Status bar at the bottom shows the current project and file name

### AI Features in Editor

<img src="../_assets/ai-feature/UpdateFocusFromAsana.png" width="60%" alt="Update Focus from Asana dialog" />

Update Focus from Asana (AI) reads `asana-tasks.md`, sends context to the configured LLM, and shows a diff-based proposal dialog; it supports workstream filtering, natural-language refinement, and `View Debug`, and saves a backup to `focus_history/`.

<img src="../_assets/ai-feature/AI-DecisionLog_1.png" width="60%" alt="AI Decision Log dialog step 1" />
<img src="../_assets/ai-feature/AI-DecisionLog_2.png" width="60%" alt="AI Decision Log dialog step 2" />

AI Decision Log (Dec Log in AI mode) detects implicit decisions from recent `focus_history`, accepts decision metadata (Status/Trigger/attachments), generates a structured draft (Options / Why / Risk / Revisit Trigger), supports refinement and debug view, and saves to `decision_log/YYYY-MM-DD_{topic}.md`.

<img src="../_assets/ai-feature/ImportMeetingNotes_1.png" width="60%" alt="Import Meeting Notes dialog step 1" />
<img src="../_assets/ai-feature/ImportMeetingNotes_2.png" width="60%" alt="Import Meeting Notes dialog step 2" />

Import Meeting Notes (AI) analyzes raw notes in a single pass and previews Decisions / Focus / Tensions / Asana Tasks tabs; you can choose what to apply, inspect prompt/response via `View Debug`, and `current_focus.md` is backed up before overwrite.

## Timeline

Chronological view of project activity filtered by project and time period.

![](../_assets/Timeline.png)

- Project dropdown to filter by a specific project (e.g. GenAi [Domain])
- Period dropdown to set the time range (e.g. 30 days)
- Graph scope selector to choose between single project and all projects
- Entries tab shows a list of timeline entries ([Focus], [Decision], [Work]) with dates (including day of week); [Work] entries come from `shared/_work/` date folders (e.g. `20260321_fix-login-bug`) and clicking them opens the folder in Explorer
- Graph tab visualizes activity trends over the selected period; Work folder events are counted alongside Focus and Decision entries

## Git Repos

Scans workspace roots and lists repositories with remote URLs, branches, and last commit dates.

![](../_assets/GitRepos.png)

- Project dropdown to filter repositories by project
- Scan button to trigger a recursive repository search under workspace roots
- Save to Cloud / Load from Cloud buttons to back up or restore clone metadata
- Copy Clone Script button generates a shell script to re-clone all listed repositories
- Table columns: Project, Repository, Remote URL, Branch, Last Commit date

## Asana Sync

Configure per-project Asana sync with scheduling, workstream mapping, and section filters.

![](../_assets/AsanaSync.png)

Use this only if your workflow includes Asana.

Left panel (sync controls):

- Auto Sync checkbox and interval setting (in hours)
- Save Schedule to persist the schedule
- Run Sync Now to execute a one-time sync immediately
- Clear button to reset sync state
- Last sync timestamp displayed for reference

Right panel (per-project config):

- Project selector dropdown (e.g. GenAi [Domain]) with Load button
- Asana Project GIDs: one GID per line to specify which Asana projects to sync
- Workstream Map: maps `gid` to `workstream-id` for routing tasks to the correct workstream folder
- Workstream Field: the custom field name in Asana used to identify the workstream
- Project Aliases: aliases used to match Asana custom field to this project (one per line)
- Save button to persist the per-project `asana_config.json`

Setup steps:

1. Enable Asana integration in `Settings` and save the required fields
2. Open `Asana Sync` and choose the target project
3. Run `Run Sync` once first
   - On success, these files are updated:
   - `_ai-context/obsidian_notes/asana-tasks.md`
   - optionally `_ai-context/obsidian_notes/workstreams/<id>/asana-tasks.md`
4. Go back to `Dashboard` and check Today Queue
   - Today Queue reads tasks from the `asana-tasks.md` files above
5. Only if you want automatic sync, turn on `Enable Schedule`
6. Choose interval and click `Save Schedule`

If tasks do not appear:
- Confirm `asana-tasks.md` was updated after `Run Sync`
- Refresh `Dashboard` to reload Today Queue

Reference (you usually do not edit these directly):
- Global Asana values are stored in the config directory (`%USERPROFILE%\.projectcurator\asana_global.json` by default)
- Per-project advanced settings are stored in `{CloudSyncProject}\asana_config.json`

## Agent Hub

Manage reusable agent/rule definitions and deploy them per project and per CLI from one page.

![](../_assets/AgentHub.png)

- Left panel: master library (Agents / Context Rules) with preview and create/edit/delete actions; double-click an item to open the Edit dialog directly
- Right panel: per-project deployment matrix with per-CLI toggles (Cl, Cx, Cp, Gm); each row has an All checkbox to toggle all CLI targets at once
- Library import is ZIP-only (Markdown-only import is not supported); export also produces a ZIP
- Supports target subfolder selection, status sync, and batch deploy
- AI Builder (AI Features must be enabled) generates an Agent or Context Rule from a free-text prompt; select the type with the radio buttons, press Generate or Ctrl+Enter, and Name/Description are auto-filled by the AI

<img src="../_assets/AgentHub-EditAgent.png" width="60%" alt="Agent Hub Edit Agent dialog" />

Edit Agent dialog lets you update name, description, and content for each reusable sub-agent definition. Ctrl+Enter saves.

<img src="../_assets/AgentHub-EditContextRule.png" width="60%" alt="Agent Hub Edit Context Rule dialog" />

Edit Context Rule dialog provides the same workflow for reusable context rules. Ctrl+Enter saves.

## Setup - New Project

Create new projects, check existing structures, archive, and convert tiers from a single page.

![](../_assets/Setup-NewProject.png)

New Project tab:

- Project Name: select an existing project to auto-fill Tier/Category, add ExternalSharePath, or run AI Context Setup on it
- Tier: `full (standard)` or `mini`
- Category: `project (time-bound)` or `domain`
- ExternalSharePath (optional): custom path per files for shared data
- Also run AI Context Setup: when checked, junctions for `_ai-context/context/` and `_ai-context/obsidian_notes/` are created automatically
- Overwrite existing skills (-Force): re-deploys `.claude/skills/`, `.codex/skills/`, `.gemini/skills/`, and `.github/skills/` even if they already exist
- Setup Project button creates the folder structure, junctions, and skill files
- Output area shows the log of operations performed

Check tab:

- Validates an existing project's folder structure, junctions, and skill files
- Reports missing or broken items so you can fix them

Archive tab:

- Moves a project to an archive location and cleans up junctions

Convert Tier tab:

- Converts a project between `full` and `mini` tiers, adjusting folder structure accordingly

## Setup - Workstreams

Manage workstreams within a project: create, rename labels, and close/reopen.

![](../_assets/Setup-Workstreams.png)

- Project selector dropdown with Reload button
- Add Workstream section: enter a Workstream ID (kebab-case), an optional label, and an optional display label, then click Create Workstream
- Existing Workstreams list shows each workstream's ID, label, and status (Active / Closed)
- Close button marks a workstream as Closed; Reopen restores it to Active
- Save Labels persists any label changes
- Output area shows the log of operations performed
