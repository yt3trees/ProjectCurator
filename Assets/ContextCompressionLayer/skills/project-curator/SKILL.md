---
name: project-curator
description: Unified Curia skill. Use for cross-project status, today's tasks, decision logging, session-end focus updates, and Obsidian knowledge. Invoke explicitly for "update focus from Asana".
allowed-tools: Read, Write, Glob, Grep, Bash
---

# /Curia

## Shell Execution Rules (IMPORTANT)

The runtime shell is **bash** (even on Windows). PowerShell code blocks in this skill must NEVER be passed directly to the Bash tool — bash will expand `$` variables and corrupt the command.

Always invoke PowerShell explicitly:

```bash
powershell.exe -Command "
\$s = Get-Content \"\$env:USERPROFILE/Documents/Projects/_config/curator_state.json\" -Raw | ConvertFrom-Json
# ... rest of script
"
```

Rules:
- Use `powershell.exe -Command "..."` (double-quoted outer string)
- Escape every `$` as `\$` inside the bash string so bash does not expand them
- Do NOT use single-quoted heredocs for multi-line PowerShell — escaping `\$` is the reliable approach

---

## Part 1: Cross-Project Data Access

### State Snapshot

File: `$env:USERPROFILE/Documents/Projects/_config/curator_state.json`

**Parse once per task — do not use Read on the raw JSON:**

```powershell
$s = Get-Content "$env:USERPROFILE/Documents/Projects/_config/curator_state.json" -Raw | ConvertFrom-Json
```

When executing via Bash tool, escape `$` signs:

```bash
powershell.exe -Command "
\$s = Get-Content \"\$env:USERPROFILE/Documents/Projects/_config/curator_state.json\" -Raw | ConvertFrom-Json
"
```

- File missing → ask user to launch Curia
- `$s.exportedAt` older than 1 hour → warn of stale data

---

### Targeted Extractions

**Today's tasks (grouped by priority bucket):**

```powershell
$s.todayTasks | Group-Object bucket | Select-Object Name, Count,
  @{n='items'; e={$_.Group | Select-Object projectName, title, dueLabel}}
```

Bucket order: overdue > today > soon > thisweek > later > nodue

---

**Project list / overview:**

```powershell
$s.projects | Select-Object name, displayName,
  @{n='focusAge';     e={$_.status.focusAge}},
  @{n='uncommitted';  e={$_.status.hasUncommittedChanges}},
  @{n='workstreams';  e={$_.status.hasWorkstreams}}
```

---

**Find a project then read its files:**

```powershell
# By name
$proj = $s.projects | Where-Object { $_.name -eq 'ProjectA' }

# By current working directory
$cwd = (Get-Location).Path.Replace('\', '/')
$proj = $s.projects | Where-Object { $cwd.StartsWith($_.paths.root) }
```

If neither matches → list `$s.projects | Select-Object name` and ask user to choose.

After finding `$proj`, use Read/Glob on individual files only as needed:

| Need | Path expression |
|---|---|
| Focus | `$proj.paths.focus` |
| Summary | `$proj.paths.summary` |
| Tasks | `$proj.paths.tasks` |
| Decisions | Glob `"$($proj.paths.decisions)/*.md"` then Read |
| Latest standup | Glob `"$($s.standupDir)/*_standup.md"` then Read newest |

---

**Stale / neglected projects:**

```powershell
$s.projects | Where-Object { $_.status.focusAge -gt 7 } |
  Select-Object name, @{n='focusAge'; e={$_.status.focusAge}} |
  Sort-Object focusAge -Descending
```

---

**Projects with uncommitted changes:**

```powershell
$s.projects | Where-Object { $_.status.hasUncommittedChanges } |
  Select-Object name, @{n='repos'; e={$_.status.uncommittedRepos}}
```

---

**Workstream paths (for multi-workstream projects):**

```powershell
$proj.status.workstreams | Select-Object id, label, isClosed, focusAge,
  focusPath, tasksPath, decisionsPath, focusHistoryPath
```

---

## Active Behaviors

These behaviors are active throughout the session once the skill is loaded.

**Decision Detection** — Detect decisions in conversation, propose logging.
→ Full rules: [reference/decision-log.md](reference/decision-log.md)

**Session End** — Detect wrap-up, propose `current_focus.md` update.
→ Full rules: [reference/session-end.md](reference/session-end.md)

**Obsidian Knowledge** — Read vault for context; propose writing back.
→ Full rules: [reference/obsidian.md](reference/obsidian.md)

---

## Action: Update Focus from Asana

Explicit trigger: "update focus from Asana", "sync Asana to focus", or `/Curia update focus`.
→ Full procedure: [reference/update-focus.md](reference/update-focus.md)

---

## Constraints

- Only access paths listed in the State Snapshot
- Do not call external APIs (Asana sync is app-side only)
- Do not trigger app-side actions (sync, refresh, navigation)
