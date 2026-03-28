# {{PROJECT_NAME}} - AI Agent Instructions

- Project: {{PROJECT_NAME}} / Created: {{CREATION_DATE}}

## Context (READ FIRST)

- `_ai-context/context/` - AI context files (project_summary, current_focus, decision_log)
- `_ai-context/obsidian_notes/` - Obsidian Knowledge Layer
- `shared/` - Cloud Sync shared folder junction

Key paths:
- Obsidian: `%USERPROFILE%\Box\Obsidian-Vault\`
- BOX: `%USERPROFILE%\Box\Projects\`

## Directory Structure

```
Documents/Projects/{{PROJECT_NAME}}/
├── _ai-context/
│   ├── context/          # → CloudSync/Obsidian-Vault/.../ai-context/
│   └── obsidian_notes/   # → CloudSync/Obsidian-Vault/...
├── shared/               # → Box/Projects/{{PROJECT_NAME}}/
│   ├── docs/ reference/ records/ _work/
└── development/source/   # Git-managed

```
