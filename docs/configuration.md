# Configuration

[< Back to README](../README.md)

## Configuration Files

`ConfigService` reads and writes:

```text
%USERPROFILE%\.curia\          <- default (new installs)
  (or override via CURIA_CONFIG_DIR environment variable)
├── settings.json
├── hidden_projects.json
├── asana_global.json
├── pinned_folders.json
├── agent_hub_state.json    <- auto-generated deployment state for Agent Hub
└── curator_state.json      <- auto-generated; updated on every Dashboard refresh

{Cloud Sync Root}\_config\agent_hub\
├── agents\                 <- master agent definitions (JSON + Markdown)
└── rules\                  <- master context rule definitions (JSON + Markdown)
```

`settings.json` and `asana_global.json` are gitignored.

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+K` | Open Command Palette |
| `Ctrl+1` - `Ctrl+8` | Navigate pages |
| `Ctrl+S` | Save in Editor |
| `Ctrl+F` | Search in Editor |
| `Ctrl+Shift+P` | Toggle app visibility (default) |
| `Ctrl+Shift+C` | Open Quick Capture window |
