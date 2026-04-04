# AI Decision Log - 自動起票機能 実装計画

既存の「Dec Log」ボタンを拡張し、LLM によるDecision Log エントリの自動生成機能を追加する。
ユーザー入力からの構造化 (モード1) と focus 差分からの決定検出 (モード2) を単一のダイアログに統合する。

## 方針

- 既存の Dec Log ボタン (NoteAdd24) を拡張する (新ボタン追加ではなく、既存ボタンの挙動を変える)
- AI 無効時は従来どおり空テンプレートを作成する (既存動作を維持)
- AI 有効時はダイアログを表示し、ユーザー入力 + 自動検出候補から構造化ドラフトを生成する
- Decision Log のタイトル/ファイル名は英語 (snake_case) で生成する
- FocusUpdateService と同じ diff プレビュー + Refine + 承認フローを踏襲する
- プロンプトは C# 定数として管理 (外部ファイル不要)
- NuGet 追加なし (既存の LlmClientService をそのまま利用)

## 統合フロー

```
[Dec Log ボタン クリック]
  │
  ├── AI 無効 → 従来動作 (ファイル名入力 → 空テンプレート作成)
  │
  └── AI 有効 → AI Decision Log ダイアログを表示
        │
        ┌─────────────────────────────────────────────────────┐
        │  AI Decision Log                                    │
        │                                                     │
        │  ── Input ──────────────────────────────────────     │
        │  What was decided?                                  │
        │  [                                              ]   │
        │                                                     │
        │  Status: (●) Confirmed  ( ) Tentative               │
        │  Trigger: [AI session ▼]                            │
        │                                                     │
        │  ── Detected from recent changes ───────────────    │
        │  [x] Switched from REST to GraphQL for API layer    │
        │  [ ] Adopted PostgreSQL instead of SQLite            │
        │  [ ] Deferred mobile support to next quarter         │
        │                                                     │
        │           [Generate Draft]  [Blank Template]        │
        └─────────────────────────────────────────────────────┘
        │
        ▼
  [LLM がドラフト生成] (入力テキスト + 選択した検出候補)
        │
        ▼
  ┌─────────────────────────────────────────────────────┐
  │  Preview: api_framework_selection                    │
  │                                                      │
  │  (AvalonEdit: 生成された Decision Log 全文)          │
  │                                                      │
  │  Refine: [                              ] [Refine]   │
  │                                                      │
  │  Resolved tension: "API設計の方針未定"               │
  │    → Remove from open_issues.md?  [Yes] [No]            │
  │                                                      │
  │                     [Cancel]  [Save]                  │
  └─────────────────────────────────────────────────────┘
        │
        ▼
  [ファイル保存] → YYYY-MM-DD_{topic}.md
  [ツリー更新] → エディタで自動オープン
  [(任意) open_issues.md から解決項目を削除]
```

## アーキテクチャ

```
EditorViewModel
  ├── NewDecisionLogCommand (既存: AI無効時 or "Blank Template" 選択時)
  └── NewAiDecisionLogCommand (新規: AI有効時のメインフロー)
        │
        ├── DetectCandidates: focus差分 → 候補リスト (ルールベース or LLM)
        │
        └── GenerateDraft: 入力+候補 → 構造化ドラフト (LLM)
              │
              ▼
DecisionLogGeneratorService (新規)
  ├── DetectCandidatesAsync()    差分から決定候補を検出
  ├── GenerateDraftAsync()       入力+コンテキスト → 構造化ドラフト
  └── RefineAsync()              ユーザー指示で修正 (FocusUpdateService と同パターン)
        │
        ▼
LlmClientService (既存: そのまま利用)
```

## データソース

ドラフト生成時にLLMへ渡すコンテキスト:

| ソース | 用途 | 必須/任意 |
|---|---|---|
| ユーザー入力テキスト | 決定内容の核 | いずれか必須 (入力 or 検出候補) |
| 検出候補 (選択分) | 決定内容の核 | いずれか必須 (入力 or 検出候補) |
| current_focus.md | 作業文脈の把握 | 必須 |
| project_summary.md | プロジェクト背景 | 任意 |
| open_issues.md | 解決判定の材料 | 任意 |
| 直近 decision_log 1-2件 | トーン・粒度の参考 | 任意 |
| focus_history 差分 | 候補検出の入力 (モード2) | 候補検出時のみ |

## 実装タスク

### Phase 1: モデルとサービス基盤

- [ ] 1-1. `DecisionLogModels.cs` を新規作成
  - `DecisionLogDraftResult`: 生成されたドラフト内容、推奨ファイル名、解決した tension 等
  - `DetectedDecision`: 検出された決定候補 (要約、根拠、選択フラグ)
  - ファイル: `Models/DecisionLogModels.cs`

- [ ] 1-2. `DecisionLogGeneratorService` を新規作成 (検出ロジック)
  - `DetectCandidatesAsync()`: focus_history の直近バックアップ vs 現在の current_focus.md の差分を LLM に渡し、暗黙の意思決定パターンを検出
  - focus_history が存在しない場合は空リストを返す (エラーにしない)
  - ファイル: `Services/DecisionLogGeneratorService.cs`

- [ ] 1-3. `DecisionLogGeneratorService` にドラフト生成ロジックを実装
  - `GenerateDraftAsync()`: ユーザー入力 + 選択された検出候補 + コンテキストファイル群 → 構造化ドラフト
  - コンテキスト収集: current_focus.md, project_summary.md (任意), open_issues.md (任意), 直近 decision_log 1-2件 (任意)
  - LLM レスポンスからファイル名候補 (英語 snake_case) と解決 tension を抽出
  - ファイル: `Services/DecisionLogGeneratorService.cs`

- [ ] 1-4. `DecisionLogGeneratorService` に Refine ロジックを実装
  - `RefineAsync()`: FocusUpdateService.RefineAsync と同パターン (会話履歴保持)
  - ファイル: `Services/DecisionLogGeneratorService.cs`

- [ ] 1-5. プロンプト設計
  - System Prompt: SKILL.md のテンプレート構造 + 品質基準を埋め込む
  - 候補検出用 System Prompt: focus 差分から意思決定パターンを検出するルール
  - 出力ルール: タイトル/ファイル名は英語、本文はユーザーの言語に合わせる、Options は最低2つ、等
  - ファイル: `Services/DecisionLogGeneratorService.cs` 内に定数定義

- [ ] 1-6. DI 登録
  - `App.xaml.cs` に `DecisionLogGeneratorService` をシングルトン登録
  - ファイル: `App.xaml.cs`

### Phase 2: ViewModel 拡張

- [ ] 2-1. `EditorViewModel` に AI Decision Log コマンドを追加
  - `NewAiDecisionLogCommand` (RelayCommand)
  - AI 有効かつプロジェクト選択済みの場合のみ実行可能
  - ファイル: `ViewModels/EditorViewModel.cs`

- [ ] 2-2. `EditorViewModel` に分岐ロジックを実装
  - 既存の `NewDecisionLogCommand` を変更: AI 有効時は `NewAiDecisionLogCommand` にルーティング
  - AI 無効時は従来の空テンプレート作成動作を維持
  - ファイル: `ViewModels/EditorViewModel.cs`

- [ ] 2-3. コールバック定義を追加
  - `RequestAiDecisionLogInput`: 入力ダイアログ表示用 (EditorPage で実装)
  - `RequestDecisionLogPreview`: プレビューダイアログ表示用 (EditorPage で実装)
  - ファイル: `ViewModels/EditorViewModel.cs`

### Phase 3: UI - 入力ダイアログ

- [ ] 3-1. AI Decision Log 入力ダイアログの実装
  - テキスト入力: "What was decided?" (1-2行、任意 - 検出候補のみでも可)
  - Status: Confirmed / Tentative (ラジオボタン)
  - Trigger: AI session / Meeting / Solo decision (ドロップダウン)
  - 検出候補リスト: チェックボックス付き (focus差分から自動検出)
  - 検出候補がない場合はセクション自体を非表示
  - ボタン: [Generate Draft] [Blank Template] [Cancel]
  - DashboardPage.xaml.cs のポップアップパターンを踏襲 (ダークモード対応)
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

- [ ] 3-2. Workstream 判定
  - 現在エディタで開いているファイルが workstream 配下かを判定 (既存の DetectWorkstreamPath を利用)
  - workstream 配下の場合、そのworkstream のコンテキストを使用
  - ファイル: `ViewModels/EditorViewModel.cs`

### Phase 4: UI - プレビュー/承認ダイアログ

- [ ] 4-1. Decision Log プレビューダイアログの実装
  - タイトルバー: アイコン + 推奨ファイル名 (編集可能)
  - 本文表示: AvalonEdit (読み取り専用、Markdown ハイライト付き)
  - Refine: テキスト入力 + Refine ボタン (FocusUpdate ダイアログと同パターン)
  - Tensions 解決通知: open_issues.md で解決した項目があれば表示 + 削除確認
  - ボタン: [Cancel] [Save]
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

- [ ] 4-2. ローディング表示
  - LLM 呼び出し中のスピナー/プログレス表示
  - CancellationToken によるキャンセル対応
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

### Phase 5: ファイル保存と後処理

- [ ] 5-1. Decision Log ファイル保存処理
  - ファイル名: `YYYY-MM-DD_{topic}.md` (topic は LLM が提案、ユーザーが編集可能)
  - 保存先: 現在の workstream の decision_log/ またはプロジェクトルートの decision_log/
  - 同日同名の場合は `_a`, `_b` サフィックスを自動付与
  - ファイル: `ViewModels/EditorViewModel.cs`

- [ ] 5-2. ツリー更新とエディタ遷移
  - 保存後にファイルツリーを再構築 (既存の BuildFileTree を利用)
  - 新規ファイルをエディタで自動オープン (既存の OpenFileAndSelectNodeAsync を利用)
  - ファイル: `ViewModels/EditorViewModel.cs`

- [ ] 5-3. open_issues.md 解決項目の削除 (任意)
  - LLM が解決と判定した open_issues.md の項目を表示
  - ユーザーが承認した場合のみ、該当行を削除して保存
  - ファイル: `ViewModels/EditorViewModel.cs`

### Phase 6: エラーハンドリング

- [ ] 6-1. API キー未設定時のガイダンス
  - AI 有効だが API キーが空の場合、Settings への誘導メッセージを表示
  - ファイル: `ViewModels/EditorViewModel.cs`

- [ ] 6-2. コンテキストファイル不在時の処理
  - current_focus.md が存在しない場合: 検出候補はスキップし、ユーザー入力のみで生成
  - focus_history が存在しない場合: 検出候補セクションを非表示
  - project_summary.md / open_issues.md が存在しない場合: プロンプトから除外 (エラーにしない)
  - ファイル: `Services/DecisionLogGeneratorService.cs`

- [ ] 6-3. LLM API 呼び出し失敗時の処理
  - エラーダイアログ表示 (既存の ShowScrollableError パターン)
  - タイムアウト、レート制限、認証エラーの個別メッセージ
  - ファイル: `Services/DecisionLogGeneratorService.cs`, `ViewModels/EditorViewModel.cs`

### Phase 7: 複数候補対応 (発展)

- [ ] 7-1. 複数の検出候補を一括でドラフト生成
  - 検出候補を複数選択した場合、それぞれ個別のドラフトとして生成
  - プレビューダイアログで候補間をタブ or ページ送りで切り替え
  - 各候補を個別に Save / Skip できる
  - ファイル: `Services/DecisionLogGeneratorService.cs`, `Views/Pages/EditorPage.xaml.cs`

## プロンプト設計 (詳細)

### 候補検出用 System Prompt

```
You are an assistant that detects implicit decisions in document changes.

## Input
You receive a diff between the previous and current version of a focus document.

## Detection rules
Detect lines that indicate a decision was made:
- "Using X instead of Y" / "Switched to X"
- "Adopted X" / "Going with X" / "Chose X"
- "Dropped X" / "Decided against X"
- Any statement comparing alternatives and reaching a conclusion
- Tentative decisions: "For now, using X" / "Temporarily X"

Do NOT detect:
- Minor wording changes or formatting fixes
- Factual observations without a choice
- Hypothetical statements ("if we...", "might...")

## Output
Return a JSON array of detected decisions. Each item:
{"summary": "...", "evidence": "...", "status": "confirmed|tentative"}

If no decisions detected, return [].
Output ONLY the JSON array, no explanation.
```

### ドラフト生成用 System Prompt

```
You are an assistant that creates structured decision log entries.

## Output rules
- Output the decision log in Markdown following the template below.
- Title and filename must be in English.
- Body text should match the language of the user's input and context files.
- Options section must list at least 2 alternatives (if info is insufficient, note what was implicitly rejected).
- Why section must cite specific reasoning (not "AI recommended").
- Revisit Trigger must be a measurable condition.
- If information is insufficient, write "TBD" for that field.

## Template
# Decision: {English Title}

> Date: {YYYY-MM-DD}
> Status: Confirmed / Tentative
> Trigger: {AI session / Meeting / Solo decision}

## Context
{2-3 sentences based on current_focus.md and project_summary.md}

## Options

### Option A: {Name}
- Pros:
- Cons:

### Option B: {Name}
- Pros:
- Cons:

## Chosen
Option {X}: {Name}

## Why
{2-4 sentences}

## Risk
-

## Revisit Trigger
-

## Additional output (after --- separator)
After the decision log content, output a separator line "---" followed by:
FILENAME: {english_snake_case_topic}
RESOLVED_TENSION: {item text from open_issues.md that this decision resolves, or "none"}
```

### ドラフト生成用 User Prompt 構造

```
## Decision to record
{ユーザーの入力テキスト}
{選択された検出候補の summary + evidence}

## Metadata
- Status: {Confirmed / Tentative}
- Trigger: {AI session / Meeting / Solo decision}
- Date: {today}
- Project: {project name}
- Workstream: {workstream id, if applicable}

## Context files

### current_focus.md
{content}

### project_summary.md (background)
{content, if exists}

### open_issues.md (check if this decision resolves any item)
{content, if exists}

### Recent decision logs (for tone/granularity reference)
{latest 1-2 entries, if exist}
```

## ファイル追加/変更一覧

### 新規ファイル

| ファイル | 説明 |
|---|---|
| `Models/DecisionLogModels.cs` | Decision Log 生成のデータモデル |
| `Services/DecisionLogGeneratorService.cs` | 候補検出 + ドラフト生成 + Refine のオーケストレーション |

### 変更ファイル

| ファイル | 変更内容 |
|---|---|
| `App.xaml.cs` | DecisionLogGeneratorService の DI 登録追加 |
| `ViewModels/EditorViewModel.cs` | AI 分岐ロジック、NewAiDecisionLogCommand、コールバック追加 |
| `Views/Pages/EditorPage.xaml.cs` | 入力ダイアログ、プレビューダイアログの実装 |

XAML ファイルの変更は不要 (既存の Dec Log ボタンをそのまま利用し、コードビハインドで分岐)。

## 実装順序

Phase 1 (モデル+サービス) → Phase 2 (ViewModel) → Phase 3 (入力ダイアログ) → Phase 4 (プレビュー) → Phase 5 (保存) → Phase 6 (エラー処理)

Phase 7 (複数候補対応) は基本機能が安定してから着手する。

## FocusUpdate 機能との共通点/相違点

| 観点 | FocusUpdate | AI Decision Log |
|---|---|---|
| トリガー | 専用ボタン (AI有効時のみ表示) | 既存 Dec Log ボタンの分岐 (AI有効時) |
| 入力データ | asana-tasks.md (自動) | ユーザーの1行入力 + focus差分検出 |
| 出力先 | 既存ファイル上書き | 新規ファイル作成 |
| バックアップ | focus_history に自動保存 | 不要 (新規作成) |
| diff 表示 | 現在 vs 提案 | 全文プレビュー (新規なのでdiff不要) |
| Refine | 会話履歴保持で反復修正 | 同パターン |
| 後処理 | なし | open_issues.md 解決チェック |
| Workstream | ダイアログで選択 | 現在開いているファイルから自動判定 |
