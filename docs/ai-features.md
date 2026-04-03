# AI Features

[< Back to README](../README.md)

All AI features require `Enable AI Features` to be on (Settings > LLM API). Supported providers: OpenAI and Azure OpenAI.

## Setup

1. Open `Settings > LLM API`
2. Choose provider, enter API Key and Model (and Endpoint / API Version for Azure)
3. Click `Test Connection`
4. Once the test passes, toggle `Enable AI Features` on and save

## User Profile

Enter a free-text description of your role, priorities, and working style in `Settings > LLM API > User Profile`. This text is prepended as a `## User Profile` section to the system prompt of every LLM call, so the model has your context without repeating it in each prompt.

Example:

```
Role: Engineering manager. I work across 3-4 parallel projects.
Prefer concise bullet points. Flag overloaded days rather than packing in tasks.
Language: respond in Japanese unless the document is already in English.
```

## What's Next (Dashboard)

Click the lightbulb icon in the Dashboard toolbar to get 3-5 AI-prioritized action suggestions across all projects. The model analyzes overdue tasks, stale focus files, uncommitted changes, and unrecorded decisions, then ranks actions by urgency. Each suggestion has an Open button to navigate directly to the relevant file.

<img src="../_assets/ai-feature/WhatsNext.png" width="70%" alt="What's Next dialog" />

## Context Briefing (Dashboard Card)

Click the lightbulb icon on a project card to generate a project-specific resume briefing. The model reads `current_focus.md`, recent `decision_log` entries, `tensions.md`, active/completed Asana tasks, and uncommitted repo signals, then outputs:

- `Where you left off` (short narrative summary)
- `Suggested next steps` (prioritized action list)
- `Key context` (compact facts to re-enter work quickly)

The dialog supports `Copy`, `Open in Editor`, and `View Debug` (prompt/response inspection).

<img src="../_assets/ai-feature/ContextBriefing.png" width="70%" alt="Context Briefing dialog" />

## Today's Plan (Dashboard)

Today's Plan dialog provides a time-blocked day plan (for example, Morning / Afternoon), with `Open`, `Copy`, `Save`, and `View Debug` actions.

<img src="../_assets/ai-feature/TodaysPlan.png" width="70%" alt="Today's Plan dialog" />

## Update Focus from Asana (Editor)

Click the `Update Focus from Asana` button in the Editor toolbar to generate a diff-based update proposal for the open `current_focus.md`. The model reads Asana task data and the existing file, then proposes changes while preserving your heading structure and writing style. A backup is saved to `focus_history/` automatically. Supports workstream filtering, natural-language refinement, and a debug view.

<img src="../_assets/ai-feature/UpdateFocusFromAsana.png" width="70%" alt="Update Focus from Asana dialog" />

## AI Decision Log (Editor)

Click `Dec Log` in the Editor toolbar (AI mode) to open the decision log assistant. Describe what was decided; the model generates a structured draft with Options / Why / Risk / Revisit Trigger sections. Supports natural-language refinement and optionally removes the resolved item from `tensions.md`. Saves as `decision_log/YYYY-MM-DD_{topic}.md`.

<img src="../_assets/ai-feature/AI-DecisionLog_1.png" width="70%" alt="AI Decision Log dialog step 1" />
<img src="../_assets/ai-feature/AI-DecisionLog_2.png" width="70%" alt="AI Decision Log dialog step 2" />

## Import Meeting Notes (Editor)

Click the `Import Meeting Notes` button in the Editor toolbar (or press `Ctrl+Enter` in the notes input dialog) to paste raw meeting notes and have the LLM analyze them in a single pass. The model produces four types of output, each shown in a separate tab of the preview dialog:

- Decisions tab: one checkbox per decision detected; click "Show draft" to preview the structured `decision_log` draft; uncheck any decision to skip it
- Focus tab: diff-based view of the AI-generated update to `current_focus.md`; the LLM rewrites the full file integrating new items while preserving the existing structure and writing style
- Tensions tab: preview of items to be appended to `tensions.md` (technical questions, tradeoffs, concerns)
- Asana Tasks tab: list of action items proposed for Asana. Each task has its own controls:
  - Project ComboBox: defaults to the first entry in `personal_project_gids` (`asana_global.json`), or the workstream's mapped project if configured
  - Section ComboBox: auto-selected by matching `anken_aliases` (from `asana_config.json`) against the section name; falls back to `(none)`
  - Due Date picker (optional)
  - Set time checkbox: when checked, reveals Hour / Minute selectors (15-minute increments); produces an ISO 8601 `due_at` value with local timezone offset
  - Check each task to include; uncheck to skip

Select which items to apply and click `Apply Selected`. A `View Debug` button in the dialog shows the full LLM prompt and response. Decision logs are saved as `YYYY-MM-DD_{topic}.md`; `current_focus.md` is backed up to `focus_history/` before updating. Created Asana tasks are appended to `asana-tasks.md` with their GID and due date.

<img src="../_assets/ai-feature/ImportMeetingNotes_1.png" width="70%" alt="Import Meeting Notes dialog step 1" />
<img src="../_assets/ai-feature/ImportMeetingNotes_2.png" width="70%" alt="Import Meeting Notes dialog step 2" />

## Quick Capture (Global Hotkey)

Press `Ctrl+Shift+C` from anywhere on your desktop to open a lightweight capture window. Type a free-text note and press Enter. If AI Features is enabled, an LLM classifies the input and routes it automatically:

| Category | Destination |
|---|---|
| `task` | Creates a task in Asana via API (requires confirmation before submitting) |
| `tension` | Appends to the project's `tensions.md` |
| `focus_update` | Opens Editor and triggers the Update Focus from Asana flow with your input as additional context |
| `decision` | Opens Editor and launches the AI Decision Log flow |
| `memo` | Appends a timestamped entry to `_config/capture_log.md` |

When AI Features is disabled, you can still use Quick Capture by selecting the category and project manually.
