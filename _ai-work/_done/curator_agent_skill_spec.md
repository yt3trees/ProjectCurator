# ProjectCurator Agent Skill - 仕様書

## 概要

ProjectCurator WPFアプリが各プロジェクトに展開する統合 Agent Skill `/project-curator`。
旧4 Skillの機能をすべて包含し、State Snapshot によるプロジェクト横断アクセスを新規追加する。

## 背景と動機

- 旧4 Skill (`context-decision-log`, `context-session-end`, `obsidian-knowledge`, `update-focus-from-asana`) を1つに統合
- Claude Code は作業対象プロジェクトのディレクトリにいることが多く、他プロジェクトのデータにアクセスできない
- MCP サーバを Claude Code に直接アタッチすると、全会話のコンテキストにツール定義が常駐する
- 細かい Skill を多数作るのではなく、汎用的な1 Skill に統合したい

## 設計方針

```
/project-curator (Agent Skill - 必要な時だけロード)
    |
    +--> State Snapshot 読み取り (横断アクセス)
    |      curator_state.json → PowerShell ConvertFrom-Json → 個別ファイル Read/Write
    |
    +--> Behaviors (ロード後セッション中有効)
    |      - Decision Detection: 意思決定の自動検出・記録
    |      - Session End: セッション終了時のfocus更新
    |      - Obsidian Knowledge: ナレッジ読み書き
    |
    +--> Actions (明示的な指示で実行)
           - Update Focus from Asana
           - Cross-project query (タスク横断、鮮度チェック等)
```

---

## 1. アプリ側: StateSnapshotService

### 責務

`DashboardViewModel.RefreshAsync()` 完了後に `curator_state.json` をバックグラウンドで書き出す。

### 出力先

```
%USERPROFILE%\Documents\Projects\_config\curator_state.json
```

(ConfigService.ConfigDir と同じディレクトリ)

### 出力タイミング

- `DashboardPage` のリフレッシュ (手動・自動) 完了時
- ← `DashboardViewModel.RefreshAsync()` 末尾で `_ = _stateSnapshotService.ExportAsync(list)` を呼び出す

### JSON スキーマ

```json
{
  "exportedAt": "2026-03-28T09:15:00+09:00",
  "appVersion": "1.x.x",
  "configDir": "C:/Users/xxx/Documents/Projects/_config",
  "localProjectsRoot": "C:/Users/xxx/Documents/Projects",
  "boxProjectsRoot": "C:/Users/xxx/Box/Projects",
  "obsidianVaultRoot": "C:/Users/xxx/Box/Obsidian-Vault",
  "standupDir": "C:/Users/xxx/Box/Obsidian-Vault/standup",
  "projects": [
    {
      "name": "ProjectA",
      "displayName": "ProjectA",
      "tier": "full",
      "category": "project",
      "paths": {
        "root": "C:/Users/xxx/Documents/Projects/ProjectA",
        "aiContext": "C:/Users/xxx/Documents/Projects/ProjectA/_ai-context",
        "focus": "C:/Users/xxx/Documents/Projects/ProjectA/_ai-context/context/current_focus.md",
        "summary": "C:/Users/xxx/Documents/Projects/ProjectA/_ai-context/context/project_summary.md",
        "decisions": "C:/Users/xxx/Documents/Projects/ProjectA/_ai-context/context/decision_log",
        "focusHistory": "C:/Users/xxx/Documents/Projects/ProjectA/_ai-context/context/focus_history",
        "tasks": "C:/Users/xxx/Documents/Projects/ProjectA/_ai-context/obsidian_notes/asana-tasks.md",
        "obsidianNotes": "C:/Users/xxx/Documents/Projects/ProjectA/_ai-context/obsidian_notes",
        "agents": "C:/Users/xxx/Documents/Projects/ProjectA/AGENTS.md",
        "claude": "C:/Users/xxx/Documents/Projects/ProjectA/CLAUDE.md"
      },
      "status": {
        "focusAge": 2,
        "summaryAge": 14,
        "focusLines": 45,
        "summaryLines": 80,
        "decisionLogCount": 7,
        "hasUncommittedChanges": true,
        "uncommittedRepos": ["my-app"],
        "hasWorkstreams": true,
        "workstreams": [
          {
            "id": "shared_infra",
            "label": "shared infra",
            "isClosed": false,
            "focusAge": 1,
            "focusPath": "C:/Users/xxx/.../workstreams/shared_infra/current_focus.md",
            "tasksPath": "C:/Users/xxx/.../workstreams/shared_infra/asana-tasks.md",
            "decisionsPath": "C:/Users/xxx/.../workstreams/shared_infra/decision_log",
            "focusHistoryPath": "C:/Users/xxx/.../workstreams/shared_infra/focus_history"
          }
        ]
      },
      "junctions": {
        "shared": "OK",
        "obsidian": "OK",
        "context": "OK"
      }
    }
  ],
  "todayTasks": [
    {
      "projectName": "ProjectA",
      "workstreamId": null,
      "title": "API design review",
      "parentTitle": null,
      "dueDate": "2026-03-28",
      "dueLabel": "Today",
      "bucket": "today",
      "asanaUrl": "https://app.asana.com/0/0/12345"
    }
  ]
}
```

### フィールド仕様

- `paths` 内の各パスは、ファイル/ディレクトリが存在する場合のみ含める。存在しない場合はキーごと省略
- `todayTasks` は `TodayQueueService.GetAllTasksSorted(projects, 10000)` の結果。`bucket` の値: `"overdue"` / `"today"` / `"soon"` / `"thisweek"` / `"later"` / `"nodue"`
- パス区切りはスラッシュ (`/`) で統一
- `exportedAt` は ISO 8601 (タイムゾーン付き)

### 実装

- `Models/CuratorStateSnapshot.cs`: `CuratorStateSnapshot`, `CuratorProjectEntry`, `CuratorProjectPaths`, `CuratorProjectStatus`, `CuratorWorkstreamEntry`, `CuratorTodayTask`
- `Services/StateSnapshotService.cs`: `ExportAsync(List<ProjectInfo> projects, CancellationToken ct)`
  - `Task.Run` でバックグラウンド実行
  - 一時ファイル書き出し → `File.Move(overwrite: true)` で原子的差し替え
  - 失敗はログのみ、UI通知・リトライなし
  - UTF-8 NoBOM, camelCase JSON
- `App.xaml.cs`: `services.AddSingleton<StateSnapshotService>()`
- `DashboardViewModel`: コンストラクタに注入、`RefreshAsync` 末尾で fire-and-forget 呼び出し

---

## 2. Skill 側: /project-curator

### ファイル構成

```
Assets/ContextCompressionLayer/skills/project-curator/
├── MANIFEST                    ← 全展開ファイルの一覧 (embedded resource 展開に使用)
├── SKILL.md                    ← コア: データアクセス + Behavior/Action リファレンスリンク
└── reference/
    ├── decision-log.md         ← Part 2: Decision Detection and Logging
    ├── session-end.md          ← Part 3: Session End Detection
    ├── obsidian.md             ← Part 4: Obsidian Knowledge Integration
    └── update-focus.md         ← Part 5: Update Focus from Asana
```

### SKILL.md の設計方針

- コア (SKILL.md) はデータアクセスロジックと Behavior/Action の概要のみ (~90行)
- 詳細ルールは `reference/` 配下に分割し、必要な時だけ Claude が読み込む
- State Snapshot の読み取りは `Read` 全文ロードではなく PowerShell `ConvertFrom-Json` でパース

### Shell 実行ルール (SKILL.md に明記)

Claude Code の Bash ツールは bash シェル。PowerShell コードブロックは直接渡さず、必ず明示的に呼び出す:

```bash
powershell.exe -Command "
\$s = Get-Content \"\$env:USERPROFILE/Documents/Projects/_config/curator_state.json\" -Raw | ConvertFrom-Json
# ...
"
```

- 外側は二重引用符の `-Command "..."` を使う
- bash が展開しないよう `$` を `\$` にエスケープする

### State Snapshot のパースパターン (PowerShell)

```powershell
# 一度ロード
$s = Get-Content "$env:USERPROFILE/Documents/Projects/_config/curator_state.json" -Raw | ConvertFrom-Json

# 今日のタスク
$s.todayTasks | Group-Object bucket | Select-Object Name, Count, @{n='items'; e={...}}

# プロジェクト一覧
$s.projects | Select-Object name, displayName, @{n='focusAge'; e={$_.status.focusAge}}, ...

# 名前でプロジェクトを特定
$proj = $s.projects | Where-Object { $_.name -eq 'ProjectA' }

# CWD からプロジェクトを逆引き
$cwd = (Get-Location).Path.Replace('\', '/')
$proj = $s.projects | Where-Object { $cwd.StartsWith($_.paths.root) }
```

---

## 3. 展開メカニズム

### ContextCompressionLayerService の現状

```csharp
private static readonly string[] EmbeddedSkillNames = ["project-curator"];
```

旧4 Skill のアセットディレクトリは削除済み。

### embedded resource 展開: MANIFEST 方式

embedded resource 名の正規化問題 (ハイフンがアンダースコアに変換される場合がある) を回避するため、`MANIFEST` ファイルを使う:

1. `ReadCclAssetText("skills/project-curator/MANIFEST")` で相対パス一覧を取得
2. 各パスを `ReadCclAssetText("skills/project-curator/{relativePath}")` で読み込む
3. 宛先に書き出す

`ReadCclAssetText` は既存の `NormalizeResourceKey` フォールバックを持つため、リソース名の表記ゆれに対応済み。

### physical file path モード (開発時)

`SkillRoot = {AppContext.BaseDirectory}/Assets/ContextCompressionLayer/skills/` が存在する場合、ディレクトリごと `CopyDirectory` (再帰コピー) で展開する。`MANIFEST` は不要。

---

## Phase 2 (将来): 双方向通信

Phase 1 の運用で「アプリへの指示」が必要と判明した場合に拡張する。

### 候補A: Named Pipe

```
Skill -> Bash (PowerShell) -> \\.\pipe\ProjectCurator -> WPF app
```

### 候補B: HTTP API (localhost)

```
Skill -> curl localhost:19840 -> WPF app HttpListener
```

### Phase 2 で追加するアクション候補

| Action | Purpose |
|---|---|
| sync_asana | Trigger Asana sync immediately |
| refresh | Force project discovery cache refresh |
| navigate | Navigate app UI to a specific page |
| open_editor | Open a file in the app's Editor |

---

## 実装タスク (Phase 1)

- [x] 1. `Models/CuratorStateSnapshot.cs` - State Snapshot モデル群を追加
- [x] 2. `Services/StateSnapshotService.cs` - ExportAsync 実装 (一時ファイル→Move)
- [x] 3. DI 登録 (`App.xaml.cs`)
- [x] 4. `DashboardViewModel.RefreshAsync` 末尾で `ExportAsync` を fire-and-forget 呼び出し
- [x] 5. `Assets/.../skills/project-curator/` に SKILL.md + MANIFEST + reference/ 4ファイルを作成
- [x] 6. `EmbeddedSkillNames = ["project-curator"]` (旧4 Skill アセット削除済み)
- [x] 7. `ReadEmbeddedSkillFiles` を MANIFEST 方式に変更 (embedded resource 正規化問題の対処)

### 検証項目

- [ ] アプリ起動・Dashboard リフレッシュ → `curator_state.json` が生成/更新されることを確認
- [ ] 任意のプロジェクトで Setup 実行 (force=true) → `project-curator/SKILL.md` と `reference/` 4ファイルが展開されることを確認
- [ ] 別ディレクトリで `/project-curator` → 横断データが PowerShell パースで取得できることを確認
- [ ] 作業中の意思決定検出 → Decision Log 提案が出ることを確認
- [ ] セッション終了 → focus 更新提案が出ることを確認

---

## 設計判断の記録

| 判断 | 選択 | 理由 |
|---|---|---|
| データ連携方式 | State Snapshot File | ネットワーク不要、ファイアウォール不要、アプリ停止後も動作 |
| MCP直アタッチ | 不採用 | 全会話にツール定義が常駐しコンテキストを消費する |
| Skill 粒度 | 1つの統合Skill | 旧4 Skillの機能を包含し、横断アクセスも追加 |
| Skill名 | `project-curator` | Claude Code 標準のケバブケース命名に統一 |
| Skill記述言語 | 英語 | LLMの解釈精度とClaude Code標準に合わせる |
| SKILL.md 分割 | コア + `reference/` サブディレクトリ | コンテキスト効率化: 詳細は必要な時だけロード |
| JSON パース | PowerShell `ConvertFrom-Json` | `Read` 全文ロード不要、必要な断片だけ抽出 |
| Shell 実行 | `powershell.exe -Command "..."` + `\$` エスケープ | bash ランタイムから PowerShell を安全に呼び出す |
| embedded resource 展開 | MANIFEST ファイル方式 | リソース名の `-` → `_` 正規化問題を `ReadCclAssetText` に委譲して回避 |
| 展開方式 | 既存の ContextCompressionLayerService 経由 | 各プロジェクトへの配布メカニズムを再利用 |
| パス区切り | スラッシュ統一 | Claude Code の Read ツールとの互換性確保 |
| todayTasks | State Snapshot に含める | アプリ側で計算済みの優先度を使うほうが正確 |
| ExportAsync 呼び出し元 | DashboardViewModel.RefreshAsync | プロジェクト一覧とタスクが揃う最も自然なタイミング |
