# Update Focus from Asana

Explicit action: fetch Asana tasks and propose updates to `current_focus.md`.

## Step 1: Identify the Project

Use the project resolution logic from Part 1 of SKILL.md.

If the project has workstreams:
1. Check if a workstream ID is specified in the request
2. If not, check `workstreams[]` in the snapshot
3. Single candidate → use it; multiple → ask user; none → general mode

Focus file selection:
- General mode: `{paths.focus}`
- Workstream mode: `{workstream.focusPath}`, fallback to `{paths.focus}`

## Step 2: File Existence Check

- `tasks.md` missing → tell user to run Asana sync first, abort
- `current_focus.md` missing → tell user to run CCL setup first, abort

## Step 3: Backup

If `{focusHistoryPath}/YYYY-MM-DD.md` does not exist:
- Copy current `current_focus.md` content as backup
- Notify: `Backup created: focus_history/YYYY-MM-DD.md`

If it already exists: skip and notify. Backup happens before user approval.

## Step 4: Parse Asana Tasks

Read `tasks.md` and extract:
- In-progress: unchecked `- [ ]` items
- Completed: checked `- [x]` items

[Collab] task handling:
- Prioritize [Assigned] tasks for "Now doing" and "Next up"
- Exclude [Collab] tasks by default
- Exception: include if already in `current_focus.md` or due today/tomorrow
- Mark with [Collab] prefix when included

## Step 5: Generate Update Proposal

Read `current_focus.md`, cross-reference with Asana tasks, present diff:

```
current_focus.md update proposal (Project: {name})

[Now doing] changes:
  Current: - xxx
  Proposed: - xxx (Asana: #1234)
  + Add: - yyy (Asana: #5678)

[Next up] changes:
  + Add: - zzz (Asana: #9012)

[Completed tasks]:
  - "aaa" -> Asana #3456 is completed. Mark as [Done]?
```

Then ask:
1. Yes — apply as-is
2. Edit — modify before applying
3. Skip — do not apply (backup is kept)

## Step 6: Apply

- "Yes": update `current_focus.md`, update the "Last Updated" date
- "Edit": incorporate user edits, then write
- "Skip": do nothing (backup is preserved)

## Rules

- Backup before approval
- Preserve human-written lines
- Propose [Done] marks for completed tasks, do not auto-delete
- Never edit `tasks.md` directly (it is auto-generated)
- Never write `current_focus.md` without approval
- Create `focus_history/` directory if it does not exist
