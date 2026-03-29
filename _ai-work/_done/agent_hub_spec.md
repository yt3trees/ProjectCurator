# Agent Hub Page - 機能仕様書

作成日: 2026-03-29

## 概要

ProjectCuratorに新しいページ「Agent Hub」を追加する。複数のAI CLIツール(Claude Code, Codex CLI, GitHub Copilot, Gemini CLI)のサブエージェント定義とコンテキストルールを一元管理し、GUIのトグル操作で対象プロジェクトに物理ファイルとして配置(Deploy)・削除(Undeploy)するコントロールセンター。

## 既存アーキテクチャとの整合性 (重要)

原案から以下の点を変更する。すべてコードベースの実態に基づく判断。

### 1. ジャンクション構造の尊重

現在のProjectCuratorは `.claude/`, `.codex/`, `.gemini/` をBox同期パスへのジャンクションとして管理している (`ContextCompressionLayerService.SetupCliSkillsJunctions`)。Agent Hubはこの構造を壊さず、Boxパス側(ジャンクションのターゲット)にファイルを書き出す。ジャンクション未設定のプロジェクトでは、ローカルパスに直接書き出す。

```
Local: Documents/Projects/MyProject/.claude/ → (junction) → Box/Projects/MyProject/.claude/
Agent Hub writes to: Box/Projects/MyProject/.claude/agents/my-agent.md
Result: Local path also sees the file via junction
```

### 2. SQLite不使用 - JSON Config

既存のConfigServiceパターンに従い、`_config/agent_hub/` ディレクトリにJSON + Markdownファイルで管理する。SQLiteは導入しない。

### 3. 「Target CLI」単一選択の廃止

既存のCCLが3つのCLIに同時展開しているのと同様に、Agent Hubでも各エージェントを個別のCLIにマルチ展開可能とする。「プロジェクト全体で1つのCLIだけ」という制限は設けない。

### 4. CLI切り替え時の一括削除(GC)の不採用

ジャンクション構造を破壊するリスクがある。代わりに、個別エージェント/ルール単位でのON/OFF管理とする。不要なファイルは個別に削除する。

### 5. `.github/` ディレクトリの扱い

GitHub Copilotの `.github/agents/` と `.github/copilot-instructions.md` は、ジャンクション管理対象外(Gitリポジトリ内で管理されるべきファイル)。Agent Hubはプロジェクトのローカルパスに直接書き出す。

---

## コア設計原則

1. Zero Prompt Pollution: サブエージェントのシステムプロンプトにパスやフォルダ制限を記述しない。フォーカス制御はファイルの物理配置のみで行う。
2. Junction-Aware Deployment: ジャンクション構成を検出し、書き出し先を自動決定する。
3. Unified Adapter Logic: CLIごとに「ベースフォルダ名」と「ファイル形式」を変えるだけのシンプルなアダプタ。
4. File-System as Source of Truth: ON/OFF状態はファイルの存在有無を正とし、JSON設定はメタデータ補助のみ。

---

## 画面レイアウト

```
+---------------------------------------------------------------------------------+
| Agent Hub                                                                       |
+-----------------------------------------+---------------------------------------+
| [ Master Library ]                      | [ Deployment ]                        |
|                                         | Project: [ ERP-Core           v ]     |
| > Agents                                | Target:  [ ./  (root)         v ]     |
|   - strict-code-reviewer                |                                       |
|   - wpf-ui-expert                       | Agents                                |
|   - erp-logic-specialist                | +----+-------------------------+------+|
|                                         | | ON | strict-code-reviewer    | CcCG ||
| > Context Rules                         | | -- | wpf-ui-expert           | CcCG ||
|   - csharp-12-practices                 | | ON | erp-logic-specialist    | Cc-- ||
|   - react-tauri-guide                   | +----+-------------------------+------+|
|                                         |   Cc = Claude  C = Codex              |
|   [ + New ] [ AI Builder ]              |   G = Copilot  g = Gemini             |
|                                         |                                       |
|-----------------------------------------| Context Rules                         |
| [Preview / Edit]                        | +----+-------------------------+------+|
|                                         | | ON | csharp-12-practices     | Cc-- ||
| # strict-code-reviewer                  | | -- | react-tauri-guide       | ---- ||
|                                         | +----+-------------------------+------+|
| You are a strict code reviewer...       |                                       |
| Focus on security, performance...       | [ Deploy Selected ] [ Sync Status ]   |
+-----------------------------------------+---------------------------------------+
```

左ペイン: マスターライブラリ(全プロジェクト共通のエージェント/ルール定義)
右ペイン: 選択プロジェクトへのデプロイ状態。各エージェントのON/OFFと対象CLI。

---

## データモデル

### Master Agent Definition

```json
// _config/agent_hub/agents/strict-code-reviewer.json
{
  "id": "strict-code-reviewer",
  "name": "strict-code-reviewer",
  "description": "コードの品質とセキュリティを厳格に審査するレビュアー",
  "createdAt": "2026-03-29T10:00:00+09:00",
  "updatedAt": "2026-03-29T10:00:00+09:00",
  "contentFile": "strict-code-reviewer.md"
}
```

```markdown
<!-- _config/agent_hub/agents/strict-code-reviewer.md -->
You are a strict code reviewer focused on security and performance.

## Responsibilities
- Review code for security vulnerabilities (OWASP Top 10)
- Enforce consistent naming conventions
- Flag performance anti-patterns
...
```

### Master Context Rule Definition

```json
// _config/agent_hub/rules/csharp-12-practices.json
{
  "id": "csharp-12-practices",
  "name": "csharp-12-practices",
  "description": "C# 12のベストプラクティスとコーディング規約",
  "createdAt": "2026-03-29T10:00:00+09:00",
  "updatedAt": "2026-03-29T10:00:00+09:00",
  "contentFile": "csharp-12-practices.md"
}
```

### Deployment State

```json
// _config/agent_hub_state.json
{
  "deployments": [
    {
      "projectName": "ERP-Core",
      "targetSubPath": "",
      "agentId": "strict-code-reviewer",
      "cliTargets": ["claude", "codex", "copilot", "gemini"],
      "deployedAt": "2026-03-29T10:30:00+09:00"
    }
  ]
}
```

この状態ファイルは「何をデプロイしたか」の記録であり、起動時にファイルシステムと突合してUIに反映する。ファイルが外部削除されていればUIも自動OFF。

---

## CLI Adapter 仕様

各CLIが期待するファイルパスとフォーマットの定義。

### Claude Code

| 種別 | Deploy先 | フォーマット |
|---|---|---|
| Agent | `<Target>/.claude/agents/<name>.md` | Markdown |
| Context Rule | `<Target>/CLAUDE.md` (append) | Markdown |

注意: CLAUDE.mdは既存の `@AGENTS.md` 参照を保持した上で、ルール内容をappendする。既存内容を破壊しない。

### Codex CLI

| 種別 | Deploy先 | フォーマット |
|---|---|---|
| Agent | `<Target>/.codex/agents/<name>.md` | Markdown |
| Context Rule | `<Target>/AGENTS.md` (append) | Markdown |

注意: Codex CLIはAGENTS.mdを読む。Claude Codeと同じファイルを参照するため、Context Ruleの配置は共通化可能。

### GitHub Copilot

| 種別 | Deploy先 | フォーマット |
|---|---|---|
| Agent | `<Target>/.github/agents/<name>.md` | Markdown |
| Context Rule | `<Target>/.github/copilot-instructions.md` (append) | Markdown |

注意: `.github/` はジャンクション管理対象外。常にローカルパスに直接書き出す。

### Gemini CLI

| 種別 | Deploy先 | フォーマット |
|---|---|---|
| Agent | `<Target>/.gemini/agents/<name>.md` | Markdown |
| Context Rule | `<Target>/GEMINI.md` (append) | Markdown |

---

## Junction-Aware Deployment ロジック

```
Deploy(projectInfo, targetSubPath, agentDef, cliTarget):
  1. projectLocalRoot = projectInfo.Path
  2. targetDir = Path.Combine(projectLocalRoot, targetSubPath)

  3. CLI別の書き出し先を決定:
     - claude/codex/gemini:
       localCliDir = Path.Combine(targetDir, ".claude") etc.
       if localCliDir is junction:
         resolvedTarget = Directory.ResolveLinkTarget(localCliDir)
         writeDir = Path.Combine(resolvedTarget, "agents")
       else:
         writeDir = Path.Combine(localCliDir, "agents")
     - copilot:
       writeDir = Path.Combine(targetDir, ".github", "agents")

  4. EnsureDirectory(writeDir)
  5. File.WriteAllText(Path.Combine(writeDir, name + ext), content)
  6. agent_hub_state.jsonにデプロイ記録を追加
```

---

## 状態同期 (State Synchronization)

アプリ起動時およびAgent Hubページ表示時に実行:

1. `agent_hub_state.json` のデプロイ記録を読み込む
2. 各記録について、実際のファイルが存在するか確認
3. ファイルが存在しない記録は `deployments` から削除
4. UIのトグル状態をファイルの存在有無に基づいて反映

外部操作(git checkout, 手動削除等)でファイルが消えた場合も、次回の同期で自動的にUIがOFFに戻る。

---

## AI Builder (Phase 2)

LlmClientServiceを使用してエージェント定義を自動生成する。

メタプロンプト:
```
Generate a sub-agent system prompt and trigger conditions.
IMPORTANT: Do NOT include any folder paths, directory restrictions,
or working directory references. Define only the role, skills,
and best practices.
```

フロー:
1. ユーザーが目的を自然言語で入力
2. LlmClientService.ChatCompletionAsync でシステムプロンプト + ユーザー入力を送信
3. レスポンスをMarkdownとしてプレビュー表示
4. ユーザーが確認・編集後、マスターライブラリに保存

ゲーティング: `settings.AiEnabled == true` の場合のみ AI Builder ボタンを有効化 (無効時は表示維持)。

---

## 実装計画

### Phase 1: 基盤 (Models, Service, Config)

- [x] P1-1: Models作成
  - [x] `AgentDefinition` model (Id, Name, Description, ContentFile, CreatedAt, UpdatedAt)
  - [x] `ContextRuleDefinition` model (同上)
  - [x] `AgentDeployment` model (ProjectName, TargetSubPath, AgentId, CliTargets, DeployedAt)
  - [x] `AgentHubState` model (Deployments list)
  - [x] `CliTarget` enum (Claude, Codex, Copilot, Gemini)
- [x] P1-2: AgentHubService作成
  - [x] マスターライブラリCRUD (agents, rules の JSON + MD ファイル読み書き)
  - [x] `_config/agent_hub/agents/`, `_config/agent_hub/rules/` ディレクトリ管理
  - [x] Agent定義一覧取得 `GetAgentDefinitions()`
  - [x] Rule定義一覧取得 `GetRuleDefinitions()`
  - [x] Agent定義の作成/更新/削除
  - [x] Rule定義の作成/更新/削除
- [x] P1-3: DeploymentService作成
  - [x] Junction検出ロジック (既存CCLサービスのパターンを参照)
  - [x] CLI Adapter: Claude Code (agents → `.claude/agents/`)
  - [x] CLI Adapter: Codex CLI (agents → `.codex/agents/`)
  - [x] CLI Adapter: GitHub Copilot (agents → `.github/agents/`)
  - [x] CLI Adapter: Gemini CLI (agents → `.gemini/agents/`)
  - [x] Deploy処理 (ファイル書き出し + state記録)
  - [x] Undeploy処理 (ファイル削除 + state記録削除)
  - [x] 状態同期 `SyncDeploymentState()` (ファイル存在チェック → state更新)
  - [x] Context Rule Deploy/Undeploy (append/remove方式)
- [x] P1-4: ConfigService拡張
  - [x] `LoadAgentHubState()` / `SaveAgentHubState()` メソッド追加
  - [x] `agent_hub_state.json` の読み書き

### Phase 2: ViewModel + Page (UI)

- [x] P2-1: AgentHubViewModel作成
  - [x] プロパティ: AgentDefinitions (ObservableCollection)
  - [x] プロパティ: RuleDefinitions (ObservableCollection)
  - [x] プロパティ: SelectedAgent / SelectedRule (プレビュー用)
  - [x] プロパティ: Projects (ProjectDiscoveryServiceから取得)
  - [x] プロパティ: SelectedProject
  - [x] プロパティ: TargetSubPath (デフォルト: "" = root)
  - [x] プロパティ: DeploymentItems (ObservableCollection, 各agentのON/OFF + CLI状態)
  - [x] コマンド: CreateAgent / EditAgent / DeleteAgent
  - [x] コマンド: CreateRule / EditRule / DeleteRule
  - [x] コマンド: ToggleDeploy (agent + CLI) ← DeployAgentItemViewModel の OnCliToggled コールバックで実現
  - [x] コマンド: SyncStatus (状態同期の手動実行)
  - [x] コマンド: RefreshProjects
  - [x] OnSelectedProjectChanged → デプロイ状態を再読み込み
- [x] P2-2: AgentHubPage.xaml 作成
  - [x] 左ペイン: マスターライブラリ (ListBox: Agents / Rules)
  - [x] 左ペイン下部: プレビュー/編集エリア (TextBox)
  - [x] 右ペイン上部: プロジェクト選択 ComboBox + ターゲットパス入力
  - [x] 右ペイン: デプロイ状態リスト (ItemsControl)
  - [x]   各行: ON/OFFトグル + Agent名 + CLI別チェックボックス(4つ)
  - [x] 右ペイン下部: Sync Status / Refresh Projects ボタン
  - [x] ダークテーマ対応 (既存テーマリソース使用)
- [x] P2-3: Agent作成/編集ダイアログ
  - [x] Name, Description 入力フィールド
  - [x] Content (Markdown) 編集エリア
  - [x] DashboardPage.xaml.csのダイアログパターンに準拠
- [x] P2-4: Rule作成/編集ダイアログ
  - [x] Agent作成ダイアログと同構造 (共通 ShowItemDialogAsync で実現)

### Phase 3: 統合 (DI, Navigation, 起動処理)

- [x] P3-1: DI登録 (App.xaml.cs)
  - [x] AgentHubService をシングルトン登録
  - [x] AgentDeploymentService をシングルトン登録
  - [x] AgentHubViewModel をシングルトン登録
  - [x] AgentHubPage をシングルトン登録
- [x] P3-2: ナビゲーション追加 (MainWindow.xaml)
  - [x] NavigationViewItem追加 (Icon: BotSparkle24)
  - [x] MenuItemsの適切な位置に配置 (AsanaSync の後)
  - [x] キーボードショートカット追加 (Ctrl+6 = Agent Hub, Ctrl+7 = Setup, Ctrl+8 = Settings)
- [x] P3-3: PageService登録
  - [x] AgentHubPageをPageServiceに登録 (DI経由で自動解決)

### Phase 4: AI Builder

- [x] P4-1: AI Builder UI
  - [x] 目的入力ダイアログ (自然言語テキスト入力)
  - [x] 生成結果プレビュー (ShowItemDialogAsync でレビュー & 保存)
  - [x] 確認→保存フロー
- [x] P4-2: AI Builder ロジック
  - [x] メタプロンプト定義 (パス/ディレクトリ制限を含めない指示)
  - [x] LlmClientService.ChatCompletionAsync 呼び出し
  - [x] レスポンスのパース・バリデーション
  - [x] AiEnabled ゲーティング (IsAiEnabled + AiEnabledChangedMessage)

### Phase 5: 高度な機能

- [x] P5-1: サブフォルダターゲティング
  - [x] プロジェクト内のサブフォルダ一覧取得 (development/source/ 配下等)
  - [x] フォルダ選択UI
  - [x] サブフォルダ単位のDeploy/Undeploy
- [x] P5-2: バッチ操作
  - [x] 複数プロジェクトへの一括Deploy
  - [x] 全プロジェクトの状態同期
- [x] P5-3: インポート/エクスポート
  - [x] マスターライブラリのZIPエクスポート
  - [x] 外部Markdownファイルからのインポート
- [x] P5-5: Claude公式Frontmatter対応
  - [x] Agent編集ダイアログでFrontmatter YAMLを設定可能
  - [x] Claude向けdeploy時に `name` / `description` 必須項目を補完
  - [x] 追加Frontmatterフィールド (`tools`, `model`, `permissionMode` など) を反映
- [x] P5-6: Agent Hub UI安定化
  - [x] Actionボタンを常時表示 + IsEnabled制御に変更 (レイアウトジャンプ防止)
  - [x] Edit/Deleteを選択対象共通ボタンに統合
  - [x] AI Builderボタンの段落ちを解消
  - [x] 右ペイン操作ボタンを記号ベースに調整
- [x] P5-7: CLI別Agent設定
  - [x] Agent設定欄を CLI別に編集可能 (Claude / Codex / Copilot / Gemini)
  - [x] Codex deploy を TOML (`.toml`) 出力に対応
  - [x] ヘルプ表示を CLI別テーブルに分離

---

## Context Rule の Append/Remove 戦略

Context Ruleの配置は、既存ファイルへの追記と削除が必要なため、Agentファイルの単純な配置/削除より複雑になる。

### 方針

マーカーコメントで囲んだセクションとして追記し、削除時はマーカー間を除去する。

```markdown
<!-- existing content of CLAUDE.md -->
@AGENTS.md

<!-- [AgentHub:csharp-12-practices] -->
## C# 12 Best Practices
- Use file-scoped namespaces
- Prefer primary constructors
...
<!-- [/AgentHub:csharp-12-practices] -->
```

Deploy: マーカー付きセクションをファイル末尾に追記
Undeploy: 対応するマーカー間のテキストを除去
既にマーカーが存在する場合: 内容を置換(再Deploy扱い)

### CLAUDE.md の特殊処理

現在の CLAUDE.md は `@AGENTS.md` のみ。Agent Hubが初めてContext Ruleを追記する際:
1. 既存内容を保持
2. 改行2つ + マーカー付きセクションを追記
3. 既存の `@AGENTS.md` 参照は絶対に削除しない

---

## ファイル構成 (新規作成ファイル一覧)

```
Models/
  AgentHubModels.cs          # AgentDefinition, ContextRuleDefinition,
                              # AgentDeployment, AgentHubState, CliTarget

Services/
  AgentHubService.cs          # マスターライブラリ CRUD
  AgentDeploymentService.cs   # Deploy/Undeploy/Sync + CLI Adapter

ViewModels/
  AgentHubViewModel.cs        # Page ViewModel

Views/Pages/
  AgentHubPage.xaml           # Page XAML
  AgentHubPage.xaml.cs        # Code-behind (ダイアログ等)
```

既存ファイル変更:
```
App.xaml.cs                   # DI登録追加
MainWindow.xaml               # NavigationViewItem追加
MainWindow.xaml.cs            # キーボードショートカット追加 (optional)
Services/ConfigService.cs     # LoadAgentHubState/SaveAgentHubState追加
```

---

## リスク・注意事項

1. CLAUDE.md 編集の競合: ユーザーが手動でCLAUDE.mdを編集している場合、マーカーベースのappend/removeが意図しない結果になる可能性がある。Phase 1ではAgent配置のみに注力し、Context Ruleの自動追記はPhase 2以降で慎重に実装する。
2. Git管理との衝突: `.github/agents/` はGit管理下に入る。デプロイしたエージェントファイルをコミットするかどうかはユーザー判断。`.gitignore` への追記は自動では行わない。
3. Box同期の遅延: ジャンクション経由でBoxに書き出す場合、クラウド同期に数秒の遅延がある。UIには「デプロイ完了」の即座のフィードバックを出し、同期状態は表示しない。
4. 既存CCLとの棲み分け: ContextCompressionLayerServiceの `skills/` 管理と、Agent Hubの `agents/` 管理は独立。skills/ は既存のCCLが、agents/ はAgent Hubがそれぞれ管理する。
