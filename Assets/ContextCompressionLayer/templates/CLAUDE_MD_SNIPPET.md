# CLAUDE.md Snippet

Append the relevant section below to each project's CLAUDE.md.
For workspace-level instructions, see the "Workspace" section at the bottom.

---

## Per-project

```markdown
## Context Compression Layer

### Session Start

Before responding to the first message, read in order:

1. `_ai-context/context/current_focus.md`
   (shared_work mode: prefer `workstreams/<workstreamId>/current_focus.md` when the
   workstream is known; fall back to the root `current_focus.md`)
2. `_ai-context/context/project_summary.md`
3. `_ai-context/context/open_issues.md` (if it exists)
4. `_ai-context/obsidian_notes/asana-tasks.md` (if it exists)
5. Other files on demand only.

After reading, present a 1-2 line summary of open items (factor in any tensions).
If focus is 3+ days old, ask about progress once.
If any context file is oversized, suggest archiving to `focus_history/`.

### Active Behaviors

Decision logging, session-end focus updates, Obsidian knowledge integration,
and Asana focus sync are handled by the `/project-curator` skill.
Invoke `/project-curator` at the start of a session to activate these behaviors.

### Work Folder Rule

If the current directory is `Local Projects Root/<project>` or directly under
`shared/`, create a dated work folder and work there.

- General: `shared/_work/yyyy/yyyyMM/yyyyMMdd_{work-summary}`
- Workstream: `shared/_work/<workstreamId>/yyyyMM/yyyyMMdd_{work-summary}`
```

---

## Workspace

```markdown
## Context Compression Layer

For cross-project status, today's task priorities, or project-spanning queries,
invoke `/project-curator`. It reads `curator_state.json` maintained by the
ProjectCurator app and provides up-to-date paths and metadata for all projects.
```
