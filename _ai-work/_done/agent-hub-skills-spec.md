# Agent Hub - Skills Management 仕様書

- 作成日: 2026-03-31
- ステータス: Draft

## 概要

Agent HubにAgentSkillsの管理機能を追加する。Skillはプロジェクトの初期Setup時に作成されるフォルダ単位のリソースであり、Agent/Context Ruleと同列にAgent Hubで一元管理できるようにする。

デフォルトで組み込まれる `project-curator` Skillはシステム提供のためグレーアウト(読み取り専用)として保護する。

## 背景

### 現状のSkill管理

- Skillは `ContextCompressionLayerService.SetupCliSkills()` でプロジェクトSetup時にデプロイされる
- 組み込みSkillは `EmbeddedSkillNames = ["project-curator"]` のみ
- Skill構成: `SKILL.md` (YAMLフロントマター + 本文) + `reference/` サブフォルダ + `MANIFEST`
- デプロイ先: `.claude/skills/`, `.codex/skills/`, `.gemini/skills/`, `.github/skills/`
- Agent HubではAgentsとContext Rulesの2種類を管理しているが、Skillsは未対応

### 現状のAgent Hub構成

- 左パネル: Master Library (Agents / Context Rules の2セクション)
- 右パネル: Deployment (プロジェクト選択 → CliTarget別チェックボックス)
- ライブラリ保存先: `~/.config/agent_hub/agents/` と `~/.config/agent_hub/rules/`
- デプロイ状態: `_config/agent_hub_state.json`

## 設計方針

### Skill vs Agent/Rule の違い

| 項目 | Agent | Context Rule | Skill |
|---|---|---|---|
| 単位 | 単一ファイル | 単一ファイル | フォルダ (SKILL.md + reference/) |
| デプロイ先 | `.{cli}/agents/` | CLAUDE.md等に追記 | `.{cli}/skills/{name}/` |
| 構成 | フロントマター + 本文 | 本文のみ | SKILL.md + 複数参照ファイル |
| Junction | なし | なし | Box同期でjunction作成あり |

### Skill定義の管理スコープ

- Agent Hub Master Libraryに "Skills" セクションを追加
- ライブラリ保存先: `~/.config/agent_hub/skills/`
- 各Skill: `{skillId}/` フォルダに `meta.json` + `SKILL.md` + `reference/` 等を格納
- `project-curator` はEmbedded Skillとして特別扱い (BuiltIn フラグ)

## データモデル

### SkillDefinition (新規Model)

```csharp
public class SkillDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsBuiltIn { get; set; } = false;
    public string ContentDirectory { get; set; } = "";  // ライブラリ内のフォルダパス
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
```

### SkillDeployment (新規Model)

```csharp
public class SkillDeployment
{
    public string ProjectName { get; set; } = "";
    public string TargetSubPath { get; set; } = "";
    public string SkillId { get; set; } = "";
    public List<CliTarget> CliTargets { get; set; } = [];
    public DateTimeOffset DeployedAt { get; set; } = DateTimeOffset.Now;
}
```

### AgentHubState 拡張

```csharp
public class AgentHubState
{
    public List<AgentDeployment> AgentDeployments { get; set; } = [];
    public List<RuleDeployment> RuleDeployments { get; set; } = [];
    public List<SkillDeployment> SkillDeployments { get; set; } = [];  // 追加
}
```

## ライブラリ保存構造

```
~/.config/agent_hub/skills/
  project-curator/          # BuiltIn (Embeddedリソースから展開)
    meta.json               # SkillDefinition JSON
    SKILL.md
    MANIFEST
    reference/
      decision-log.md
      session-end.md
      obsidian.md
      update-focus.md
  my-custom-skill/          # ユーザー作成
    meta.json
    SKILL.md
    reference/
      ...
```

## UI設計

### 画面スペースの課題と対策

現状の左パネルはAgents/Context Rulesの2セクションを縦分割しており、3つ目のSkillsセクションを追加すると各リストの表示領域が不足する。

対策: 左パネルのライブラリリストをタブ切り替え方式に変更する。

### 左パネル: タブ付きMaster Library

Agents / Context Rules / Skills を3つのタブで切り替える。一度に表示するリストは1つのみとなり、リストの高さを最大限活用できる。

```
┌─────────────────────────────────┐
│ [Agents(3)] [Rules(2)] [Skills(2)] │  ← タブヘッダー
│─────────────────────────────────│
│  - my-agent-1                   │  ← 選択中タブのリスト
│  - my-agent-2                   │     (高さ全体を使える)
│  - my-agent-3                   │
│                                 │
│                                 │
│                                 │
└─────────────────────────────────┘
│ Preview                         │  ← 既存のプレビュー領域
│ ...                             │
└─────────────────────────────────┘
│ [+ New] [Edit] [Delete] [AI Builder] │  ← アクションボタン
└─────────────────────────────────┘
```

- タブ実装: Border内にStackPanel (Horizontal) でタブボタンを並べ、選択中タブの `Background` を `AppSurface2` に、非選択を `Transparent` に設定
- 各タブボタンにカウント表示: `Agents (3)`, `Rules (2)`, `Skills (2)`
- タブ切り替え時にリストの `ItemsSource` を差し替える、またはVisibility切り替えで実装
- Skillsタブ選択時:
  - BuiltIn Skill (`project-curator`) は名前の横にロックアイコン (LockClosed16) を表示
  - BuiltIn SkillのListBoxItemは `Foreground` をグレー (`AppOverlay0`) に設定
  - BuiltIn Skillは選択してPreviewは可能、Edit/Deleteは不可

### 左パネル: アクションボタン

既存の `+ Agent`, `+ Rule` を統合して `+ New` ボタンに変更。選択中のタブに応じて動作が変わる。

- Agentsタブ: Agent作成ポップアップ
- Rulesタブ: Rule作成ポップアップ
- Skillsタブ: Skill作成ポップアップ
- `Edit`: BuiltIn Skillでも有効 (読み取り専用ダイアログを開く)
- `Delete`: BuiltIn Skill選択時は無効
- `AI Builder` は既存通り

### 右パネル: Deployment

既存の Agents / Context Rules デプロイリストに "Skills" デプロイリストを追加。右パネルは既にScrollViewerで囲まれているため、3セクション表示でも問題ない。

```
┌──────────────────────────────────────────┐
│ Project: [MyProject v]  Target: [. v]    │
│──────────────────────────────────────────│
│ Agents                                   │
│ All | Name              | Cl Cx Cp Gm   │  <- 既存
│──────────────────────────────────────────│
│ Context Rules                            │
│ All | Name              | Cl Cx Cp Gm   │  <- 既存
│──────────────────────────────────────────│
│ Skills                                   │  <- 新規
│ All | Name              | Cl Cx Cp Gm   │
│ [x]  project-curator     x  x  x  x     │
│ [ ]  my-custom-skill     x  -  -  x     │
└──────────────────────────────────────────┘
```

- Skills行のCli Targetチェックボックスは既存のAgent/Ruleデプロイと同じUIパターン
- チェック変更時に `AgentDeploymentService` を通じてデプロイ実行

### Skill作成/編集ポップアップ

Skillの編集はSKILL.mdの内容のみアプリから編集可能とする。reference/配下のファイルはフォルダをエクスプローラーで開いて編集する方式。

- `+ New` (Skillsタブ) / `Edit` クリック時にモーダルダイアログを表示
- ダイアログフィールド:
  - Name (テキスト, 必須)
  - Description (テキスト, 任意)
  - Other Parameters (TextBox, 2行程度, frontmatterの `name` / `description` 以外を編集)
  - SKILL.md content (body only) (TextBox, 複数行, AcceptsReturn=True, フォント=Consolas)
- ダイアログ内の表示ルール:
  - `SKILL.md content` には先頭の `--- ... ---` (frontmatter) を表示しない
  - body先頭の空行は表示時にトリムする (実ファイル先頭が空行でもUI上は1行目から本文表示)
- 保存時の再構築ルール:
  - `name` / `description` は専用欄の値を優先
  - `Other Parameters` は frontmatter にそのまま反映 (`name` / `description` は重複除去)
  - `SKILL.md content` は body として保存 (先頭空行はトリム)
- ダイアログ下部に "Open Folder" ボタン: reference/配下のファイルを追加・編集したい場合にExplorerでスキルフォルダを開く
  - `Process.Start("explorer.exe", skillFolderPath)` で実装
  - 新規作成時はSave後に有効化
- BuiltIn Skillを選択した状態でも `Edit` でダイアログを開けるが、全項目読み取り専用 (Save非表示)
- DashboardPage.xaml.csのポップアップパターンに準拠

## サービス層

### AgentHubService 拡張

```
LoadSkillLibrary() -> List<SkillDefinition>
SaveSkillDefinition(SkillDefinition, Dictionary<string, string> files)
DeleteSkillDefinition(string skillId)
EnsureBuiltInSkills()  // Embedded -> ライブラリに展開
```

- `EnsureBuiltInSkills()` は起動時に呼ばれ、`project-curator` がライブラリに存在しなければEmbeddedリソースから展開する
- BuiltIn Skillの内容は常にEmbeddedリソースを正とし、アプリ更新時に上書き更新する

### AgentDeploymentService 拡張

```
DeploySkillAsync(projectPath, targetSubPath, skillId, cliTargets)
UndeploySkillAsync(projectPath, targetSubPath, skillId, cliTargets)
```

- デプロイ: ライブラリの `{skillId}/` フォルダ内容を `.{cli}/skills/{skillName}/` にコピー
- アンデプロイ: `.{cli}/skills/{skillName}/` フォルダを削除
- Junction関連: 既存の `SetupCliSkillsJunctions` ロジックと同じ方式を適用

### AgentHubState 拡張

- `SkillDeployments` リストを追加
- 既存のLoad/Save処理に統合

## BuiltInスキルの保護ルール

- `project-curator` の `IsBuiltIn = true`
- UI:
  - ライブラリリストでグレーアウト表示 (Foreground: AppOverlay0)
  - ロックアイコン (SymbolIcon: LockClosed) を名前横に表示
  - 選択してPreview表示は可能
  - `Edit` は有効だが読み取り専用で表示
  - `Delete` は `IsBuiltIn` の場合 `IsEnabled = false`
- サービス:
  - `DeleteSkillDefinition()` は `IsBuiltIn` の場合に例外をスロー
  - `SaveSkillDefinition()` は `IsBuiltIn` の場合に例外をスロー
- デプロイ: BuiltIn Skillも他のSkillと同様にデプロイ/アンデプロイ可能 (コンテンツの編集だけが制限される)

## DeploySkillItemViewModel (新規)

既存の `DeployAgentItemViewModel` / `DeployRuleItemViewModel` と同じパターン。

```csharp
public partial class DeploySkillItemViewModel : ObservableObject
{
    public SkillDefinition Definition { get; }
    public string Name => Definition.Name;
    public bool IsBuiltIn => Definition.IsBuiltIn;

    public Action<DeploySkillItemViewModel, CliTarget, bool>? OnCliToggled;

    [ObservableProperty] private bool isClaudeDeployed;
    [ObservableProperty] private bool isCodexDeployed;
    [ObservableProperty] private bool isCopilotDeployed;
    [ObservableProperty] private bool isGeminiDeployed;

    public bool? IsAllDeployed { get; set; }
    // ... 既存パターンと同様
}
```

## ContextCompressionLayerService との統合

- 既存の `SetupCliSkills()` は引き続きSetupフローで使用
- Agent HubのSkillデプロイは別経路 (`AgentDeploymentService`) で実行
- 将来的にSetupフローもAgent Hub経由に統一する余地を残す

## ZIP Export/Import 拡張

- 既存のAgent/Rule ZIP Export/Importに Skills を追加
- ZIPアーカイブ内構造: `skills/{skillId}/` フォルダを含める
- BuiltIn Skill はExportには含めるがImport時にBuiltInフラグを維持

---

## 実装タスク

### Phase 1: データ層

- [ ] 1. データモデル追加: `SkillDefinition`, `SkillDeployment` を `AgentHubModels.cs` に追加、`AgentHubState` に `SkillDeployments` を追加
- [ ] 2. `AgentHubService` 拡張: `LoadSkillLibrary()`, `SaveSkillDefinition()`, `DeleteSkillDefinition()`, `EnsureBuiltInSkills()` を実装
- [ ] 3. `AgentDeploymentService` 拡張: `DeploySkillAsync()`, `UndeploySkillAsync()` を実装

### Phase 2: ViewModel層

- [ ] 4. `DeploySkillItemViewModel` を `AgentHubViewModel.cs` に追加 (既存パターン踏襲)
- [ ] 5. `AgentHubViewModel` 拡張: `SkillDefinitions`, `SelectedSkillDefinition`, `SkillDeployItems` プロパティ追加、ロード/デプロイ処理追加
- [ ] 6. `AgentHubViewModel` 拡張: タブ切り替え状態管理 (`SelectedLibraryTab` プロパティ、`+ New` ボタンのタブ連動)

### Phase 3: UI層 - 左パネルのタブ化リファクタリング

- [ ] 7. `AgentHubPage.xaml` 左パネル: Agents/Rules の2セクション縦分割をタブ切り替え方式に変更
- [ ] 8. `AgentHubPage.xaml` 左パネル: Skillsタブ追加 (BuiltInグレーアウト + ロックアイコン)
- [ ] 9. `AgentHubPage.xaml` 左パネル: `+ Agent` / `+ Rule` を `+ New` に統合、`Edit`/`Delete` のBuiltIn制御

### Phase 4: UI層 - 右パネル + ポップアップ

- [ ] 10. `AgentHubPage.xaml` 右パネル: Skills デプロイリスト追加 (Cli Targetチェックボックス)
- [ ] 11. `AgentHubPage.xaml.cs`: Skill作成/編集ポップアップダイアログの実装 (SKILL.md編集 + Open Folderボタン)

### Phase 5: 統合

- [ ] 12. ZIP Export/Import にSkillsを統合
- [ ] 13. ビルド検証 (`publish.cmd`) と動作確認
