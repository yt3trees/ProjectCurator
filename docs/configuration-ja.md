# 設定リファレンス

[< READMEに戻る](../README-ja.md)

## 設定ファイル

`ConfigService` は次のフォルダを利用します。

```text
%USERPROFILE%\.projectcurator\          <- デフォルト (新規インストール)
  (PROJECTCURATOR_CONFIG_DIR 環境変数で任意のパスに上書き可)
├── settings.json
├── hidden_projects.json
├── asana_global.json
├── pinned_folders.json
├── agent_hub_state.json    <- Agent Hub の配備状態 (自動生成)
└── curator_state.json      <- 自動生成; Dashboard 更新のたびに書き出される

{Cloud Sync Root}\_config\agent_hub\
├── agents\                 <- マスターAgent定義 (JSON + Markdown)
└── rules\                  <- マスターContext Rule定義 (JSON + Markdown)
```

`settings.json` / `asana_global.json` は `.gitignore` 対象です。

## キーボードショートカット

| Shortcut | Action |
|---|---|
| `Ctrl+K` | Command Paletteを開く |
| `Ctrl+1` - `Ctrl+8` | 各ページへ移動 |
| `Ctrl+S` | Editorで保存 |
| `Ctrl+F` | Editor検索 |
| `Ctrl+Shift+P` | アプリ表示/非表示(既定) |
| `Ctrl+Shift+C` | Quick Capture ウィンドウを開く |
