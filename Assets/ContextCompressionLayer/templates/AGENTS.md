# {{PROJECT_NAME}} - AI Agent Instructions

- Project: {{PROJECT_NAME}} / Created: {{CREATION_DATE}}

## Context (READ FIRST)

- `_ai-context/context/` - AI context files (project_summary, current_focus, decision_log)
- `_ai-context/obsidian_notes/` - Obsidian Knowledge Layer
- `shared/` - Cloud Sync shared folder junction

## Directory Structure

```
Documents/Projects/{{PROJECT_NAME}}/
├── _ai-context/
│   ├── context/          # → [obsidianVaultRoot]/.../ai-context/
│   └── obsidian_notes/   # → [obsidianVaultRoot]/...
├── shared/               # → [cloudSyncRoot]/Projects/{{PROJECT_NAME}}/
│   ├── docs/ reference/ records/ _work/
└── development/source/   # Git-managed

```

> Actual paths: `%USERPROFILE%\Documents\Projects\_config\curator_state.json`
