# AI Features

[< Back to README](../README.md)

All AI features require `Enable AI Features` to be on (Settings > LLM API). Supported providers: OpenAI and Azure OpenAI.

## AI Features Overview

The AI features in ProjectCurator are categorized into three main situations: Planning (Dashboard), Updating/Logging (Editor), and Capturing thoughts (Global).

```mermaid
flowchart LR
    %% Base
    Profile>👤 User Profile<br>Sets tone & context everywhere]

    %% Categories
    subgraph Dashboard ["📊 Overviews & Planning (Dashboard)"]
        direction TB
        WN["💡 What's Next<br>Suggests next actions"]
        CB["📋 Context Briefing<br>Resume work seamlessly"]
        TP["📅 Today's Plan<br>Time-blocked day plan"]
    end

    subgraph Editor ["📝 Updating & Logging (Editor)"]
        direction TB
        UF["🎯 Update Focus<br>Sync focus from Asana status"]
        DL["⚖️ AI Decision Log<br>Appends structured logs"]
        IM["👥 Import Meeting Notes<br>Auto-extract tasks & decisions"]
    end

    subgraph Global ["⚡ Fleeting Thoughts (Global)"]
        direction TB
        QC["🪟 Quick Capture<br>Ctrl+Shift+C<br>AI-routed inbox"]
    end

    subgraph Wiki ["📚 Knowledge Base (Wiki)"]
        direction TB
        WI["📥 Ingest<br>Import source, generate summary page"]
        WQ["💬 Query<br>Ask questions against the Wiki"]
        WL["🔍 Lint<br>Detect contradictions & orphan pages"]
    end

    Profile -.-> Dashboard
    Profile -.-> Editor
    Profile -.-> Global
    Profile -.-> Wiki
```

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

Click the lightbulb icon on a project card to generate a project-specific resume briefing. The model reads `current_focus.md`, recent `decision_log` entries, `open_issues.md`, active/completed Asana tasks, and uncommitted repo signals, then outputs:

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

Click `Dec Log` in the Editor toolbar (AI mode) to open the decision log assistant. Describe what was decided; the model generates a structured draft with Options / Why / Risk / Revisit Trigger sections. Supports natural-language refinement and optionally removes the resolved item from `open_issues.md`. Saves as `decision_log/YYYY-MM-DD_{topic}.md`.

<img src="../_assets/ai-feature/AI-DecisionLog_1.png" width="70%" alt="AI Decision Log dialog step 1" />
<img src="../_assets/ai-feature/AI-DecisionLog_2.png" width="70%" alt="AI Decision Log dialog step 2" />

## Import Meeting Notes (Editor)

Click the `Import Meeting Notes` button in the Editor toolbar (or press `Ctrl+Enter` in the notes input dialog) to paste raw meeting notes and have the LLM analyze them in a single pass. The model produces four types of output, each shown in a separate tab of the preview dialog:

- Decisions tab: one checkbox per decision detected; click "Show draft" to preview the structured `decision_log` draft; uncheck any decision to skip it
- Focus tab: diff-based view of the AI-generated update to `current_focus.md`; the LLM rewrites the full file integrating new items while preserving the existing structure and writing style
- Tensions tab: preview of items to be appended to `open_issues.md` (technical questions, tradeoffs, concerns)
- Asana Tasks tab: list of action items proposed for Asana. Each task has its own controls:
  - Project ComboBox: defaults to the first entry in `personal_project_gids` (`asana_global.json`), or the workstream's mapped project if configured
  - Section ComboBox: auto-selected by matching `anken_aliases` (from `asana_config.json`) against the section name; falls back to `(none)`
  - Due Date picker (optional)
  - Set time checkbox: when checked, reveals Hour / Minute selectors (15-minute increments); produces an ISO 8601 `due_at` value with local timezone offset
  - Check each task to include; uncheck to skip

Select which items to apply and click `Apply Selected`. A `View Debug` button in the dialog shows the full LLM prompt and response. Decision logs are saved as `YYYY-MM-DD_{topic}.md`; `current_focus.md` is backed up to `focus_history/` before updating. Created Asana tasks are appended to `tasks.md` with their GID and due date.

<img src="../_assets/ai-feature/ImportMeetingNotes_1.png" width="70%" alt="Import Meeting Notes dialog step 1" />
<img src="../_assets/ai-feature/ImportMeetingNotes_2.png" width="70%" alt="Import Meeting Notes dialog step 2" />

## Quick Capture (Global Hotkey)

Press `Ctrl+Shift+C` from anywhere on your desktop to open a lightweight capture window. Type a free-text note and press Enter. If AI Features is enabled, an LLM classifies the input and routes it automatically:

| Category | Destination |
|---|---|
| `task` | Creates a task in Asana via API (requires confirmation before submitting) |
| `tension` | Appends to the project's `open_issues.md` |
| `focus_update` | Opens Editor and triggers the Update Focus from Asana flow with your input as additional context |
| `decision` | Opens Editor and launches the AI Decision Log flow |
| `memo` | Appends a timestamped entry to `_config/capture_log.md` |

When AI Features is disabled, you can still use Quick Capture by selecting the category and project manually.

## Wiki

The Wiki tab lets the LLM incrementally build and maintain a project-specific knowledge base from your source files.

### Page Categories

Pages generated during Import are organized into four categories. The LLM assigns categories automatically.

| Category | Location | Contents |
|---|---|---|
| Wiki Files | `wiki/` root | `index.md` (page list) and `log.md` (operation log). Management files auto-updated by the LLM |
| sources | `pages/sources/` | One summary page per imported source file |
| entities | `pages/entities/` | Concrete "things" in the project: tables, screens, APIs, reports, user roles, etc. |
| concepts | `pages/concepts/` | Design philosophy and business rules: approval flows, workflows, technical policies, decision criteria, etc. |
| analysis | `pages/analysis/` | Q&A pages and comparative analyses saved from the Query tab |

A helpful rule of thumb: "What is it (noun)?" → entities; "How does it work or why is it so (verb/policy)?" → concepts.

### Import (Ingest a Source)

Click "+ Import Source" or drag and drop a file onto the Wiki tab. The LLM automatically:

- Saves the source to `wiki/raw/` (immutable copy)
- Creates a summary page in `pages/sources/`
- Creates or updates related `pages/entities/` and `pages/concepts/` pages
- Updates `index.md` and `log.md`

Supported formats: `.md` / `.txt` (PDF / Word require text conversion first).

LLM responses are received as JSON; the count of created and updated pages is shown in the status bar.

#### Import Prompt Structure

The LLM call is a single `ChatCompletionAsync` (system + user).

System prompt:
- Full text of `wiki-schema.md` (acts as the LLM's operating instructions for the wiki)
- Output language directive (Japanese if PC locale is Japanese, otherwise English)
- Response format directive: JSON only (no code fences)
- Instruction to include YAML frontmatter (`title` / `created` / `updated` / `sources` / `tags`) in each page
- Instruction to use `[[PageName]]` wikilink format for cross-references

User prompt includes:
- Full text of the current `index.md` (list of existing pages)
- Source file name and full body text
- Instructions: create a sources/ summary page, update existing pages with full content, create new entity/concept pages, generate the full updated index.md, generate a log.md entry

LLM response JSON schema:

```json
{
  "summary": "brief description of what was done",
  "newPages": [{ "path": "pages/category/filename.md", "content": "full Markdown" }],
  "updatedPages": [{ "path": "pages/category/filename.md", "diff": "full updated Markdown" }],
  "indexUpdate": "full updated index.md content",
  "logEntry": "log.md entry to append"
}
```

The `diff` field in `updatedPages` returns the full updated content (not a patch).

### Query (Ask the Wiki)

Answers questions by reading the accumulated Wiki. Unlike RAG, pages are passed directly to the LLM rather than being searched and re-synthesized on every call.

- 100 pages or fewer (Small mode): all page contents (up to 50 pages) are passed directly to the LLM
- More than 100 pages (Medium+ mode): the LLM first selects 5 relevant pages from the index, then generates an answer from those pages (2 LLM calls)

Use "Save as Wiki Page" to save the answer to `pages/analysis/`.

#### Query Prompt Structure

Small mode uses 1 `ChatCompletionAsync` call; Medium+ mode uses 2.

[Small mode] System prompt:
- Declaration that the model is the wiki answer assistant
- Instruction to answer based ONLY on the provided wiki content
- Instruction to list referenced pages in `[[PageName]]` format at the end
- Output language directive (locale-based)
- Full text of `wiki-schema.md` (project context)

[Small mode] User prompt includes:
- The question
- Full text of `index.md`
- Full contents of relevant pages (up to 50)

[Medium+ mode] First call (page selection):
- System: "wiki search assistant — respond with file paths only, one per line"
- User: question + full `index.md` → returns paths of 5 relevant pages

[Medium+ mode] Second call: same structure as Small mode, using the 5 selected pages

### Lint

Combines static checks (C#) and LLM analysis to validate Wiki quality.

| Check | Description | Method |
|---|---|---|
| BrokenLink | `[[wikilink]]` pointing to a non-existent page | Static |
| Orphan | Page with no inbound links (sources and management files excluded) | Static |
| MissingSource | Source reference not found in `raw/` (checks frontmatter of sources/ pages) | Static |
| Stale | Page not updated in 30+ days (sources and management files excluded) | Static |
| Contradiction | Conflicting descriptions of the same fact across pages | LLM |
| Missing | Topic mentioned in 3+ pages but with no dedicated page | LLM |

When AI Features is disabled, only static checks are run.

#### Lint Prompt Structure

The LLM check is a single `ChatCompletionAsync` call.

System prompt (sent in Japanese when locale is Japanese):
- Declaration that the model is the wiki quality auditor
- Scope: Contradiction and Missing checks only
- Strict response format:
  - `CONTRADICTION: [page1] vs [page2] — [description]`
  - `MISSING: [topic] — mentioned in [page1], [page2]...`
  - Use `CONTRADICTION: none` / `MISSING: none` when nothing is found

User prompt includes:
- Full text of `index.md`
- One-line summary of each page (up to 80 pages; summaries rather than full content to reduce token usage)

The LLM response is parsed line by line and dispatched by `CONTRADICTION:` / `MISSING:` prefix.
