# Meeting Notes Import - 会議メモ → コンテキスト反映 実装計画

会議メモやセッションメモを貼り付けると、LLM が内容を解析して
decision_log / current_focus.md / tensions.md への反映を一括提案する機能。

## 解決する課題

会議後に手動で行っている3つの作業を1つの入力でまとめて処理する:

1. 決定事項 → `decision_log/YYYY-MM-DD_{topic}.md` を作成
2. 次のアクション / 最近の文脈 → `current_focus.md` の「最近あったこと」「次やること」を更新
3. 未解決の問いやリスク → `tensions.md` に追記

## フロー全体像

```
[入力ダイアログ]
  - テキストエリア: 会議メモ (自由形式、貼り付け)
  - プロジェクト選択 (現在エディタで開いているプロジェクトをデフォルト)
  - Workstream 選択 (任意)
  - [Analyze] ボタン
        │
        ▼
[LLM が1回の呼び出しで3種類の出力を構造化]
        │
        ▼
[プレビューダイアログ: 3タブ構成]
  ┌────────────────────────────────────────────────────────┐
  │  📋 Meeting Notes: ProjectAlpha  2026-03-22             │
  │                                                         │
  │  [Decisions (2)]  [Focus]  [Tensions (1)]               │
  │                                                         │
  │  (Decisions タブ選択時)                                  │
  │  ┌─────────────────────────────────────────────────┐   │
  │  │ [x] api_framework_selection                      │   │
  │  │     → REST から GraphQL に変更...                │   │
  │  │     [展開して全文を表示 ▼]                        │   │
  │  ├─────────────────────────────────────────────────┤   │
  │  │ [x] deployment_strategy                          │   │
  │  │     → Blue-Green デプロイを採用...               │   │
  │  │     [展開して全文を表示 ▼]                        │   │
  │  └─────────────────────────────────────────────────┘   │
  │                                                         │
  │  Refine: [                              ] [Refine]      │
  │                                                         │
  │                         [Cancel]  [Apply Selected]      │
  └────────────────────────────────────────────────────────┘
        │
        ▼
[Apply: チェックされた項目だけを適用]
  - Decisions: decision_log ファイルを1件ずつ作成
  - Focus: current_focus.md を更新 (バックアップあり)
  - Tensions: tensions.md に追記
        │
        ▼
[結果サマリ] → 作成/更新したファイルをエディタで順に開く
```

## アーキテクチャ

```
EditorViewModel / CommandPaletteViewModel
  └── ImportMeetingNotesCommand
        │
        ▼
MeetingNotesService (新規)
  ├── AnalyzeAsync()        会議メモ → 3種類の提案を構造化
  ├── ApplyDecisionsAsync() decision_log ファイルを作成
  ├── ApplyFocusAsync()     current_focus.md を更新
  └── ApplyTensionsAsync()  tensions.md に追記
        │
        ▼
LlmClientService (既存: そのまま利用)
FileEncodingService (既存: そのまま利用)
```

## LLM 呼び出し設計

**呼び出し回数: 1回** (入力1回でDecisions/Focus/Tensions を同時に出力)

### System Prompt

```
You are an assistant that analyzes meeting or session notes and categorizes information
into three types for project context management.

## Output format
Output ONLY a JSON object with exactly these three keys.

{
  "decisions": [
    {
      "filename_topic": "english_snake_case",
      "title": "English Title",
      "status": "confirmed|tentative",
      "trigger": "meeting|ai_session|solo",
      "context": "2-3 sentences",
      "option_a_name": "Name",
      "option_a_pros": "...",
      "option_a_cons": "...",
      "option_b_name": "Name",
      "option_b_pros": "...",
      "option_b_cons": "...",
      "chosen": "Option A/B: Name",
      "why": "2-4 sentences",
      "risk": "...",
      "revisit_trigger": "measurable condition"
    }
  ],
  "focus_updates": {
    "recent_context": ["item 1", "item 2"],
    "next_actions": ["action 1", "action 2"]
  },
  "tensions": {
    "technical_questions": ["question 1"],
    "tradeoffs": ["tradeoff 1"],
    "concerns": ["concern 1"]
  }
}

## Classification rules

### decisions[]
- Record only when a real CHOICE was made between alternatives
- If only one option was discussed (no real comparison), omit from decisions
- Status "tentative" if explicitly described as provisional/temporary
- filename_topic and title: always English
- Body text: match the language of the input
- Options: infer the alternative that was NOT chosen if not explicitly stated
- Minimum quality: option_a and option_b must be meaningfully different

### focus_updates
- recent_context: facts or outcomes the user should record in "最近あったこと" (what happened)
  e.g. "DBスキーマ見直しの会議を実施、PostgreSQL採用が決定"
- next_actions: concrete tasks for "次やること"
  e.g. "GraphQL スキーマ定義の作成"
- Omit if no relevant content

### tensions
- technical_questions: unresolved technical questions still open
- tradeoffs: competing constraints with no clear resolution
- concerns: risks or inconsistencies to watch
- Omit categories that are empty

## General rules
- Output ONLY the JSON. No explanation, no markdown fences.
- If a section has no items, use an empty array [].
- If focus_updates has no items, use empty arrays.
```

### User Prompt 構造

```
## Meeting notes to analyze
{会議メモの全文}

## Context
- Project: {project name}
- Workstream: {workstream id or "general"}
- Date: {today}

## Existing tensions (to avoid duplicates)
{tensions.md の現在の内容、または "(none)"}

## Existing focus (for context)
{current_focus.md の現在の内容、または "(none)"}
```

## データモデル

```csharp
// Models/MeetingNotesModels.cs

public class MeetingAnalysisResult
{
    public List<MeetingDecision> Decisions { get; set; } = [];
    public MeetingFocusUpdate FocusUpdate { get; set; } = new();
    public MeetingTensions Tensions { get; set; } = new();
    public string DebugPrompt { get; set; } = "";
    public string DebugResponse { get; set; } = "";
}

public class MeetingDecision
{
    public string FilenameTopic  { get; set; } = "";  // "api_framework_selection"
    public string Title          { get; set; } = "";  // "API Framework Selection"
    public string Status         { get; set; } = "";  // "confirmed" | "tentative"
    public string Trigger        { get; set; } = "";  // "meeting" | "ai_session" | "solo"
    public string Context        { get; set; } = "";
    public string OptionAName    { get; set; } = "";
    public string OptionAPros    { get; set; } = "";
    public string OptionACons    { get; set; } = "";
    public string OptionBName    { get; set; } = "";
    public string OptionBPros    { get; set; } = "";
    public string OptionBCons    { get; set; } = "";
    public string Chosen         { get; set; } = "";
    public string Why            { get; set; } = "";
    public string Risk           { get; set; } = "";
    public string RevisitTrigger { get; set; } = "";
    public bool   IsSelected     { get; set; } = true;  // UI チェックボックス用
}

public class MeetingFocusUpdate
{
    public List<string> RecentContext { get; set; } = [];  // "最近あったこと" に追記
    public List<string> NextActions   { get; set; } = [];  // "次やること" に追記
    public bool IsSelected { get; set; } = true;
    // 実際に更新されるテキスト (ApplyFocusAsync で生成)
    public string ProposedContent { get; set; } = "";
    public string CurrentContent  { get; set; } = "";
}

public class MeetingTensions
{
    public List<string> TechnicalQuestions { get; set; } = [];
    public List<string> Tradeoffs          { get; set; } = [];
    public List<string> Concerns           { get; set; } = [];
    public bool IsSelected { get; set; } = true;
    // 実際に追記されるテキスト (BuildTensionsAppend で生成)
    public string AppendContent  { get; set; } = "";
    public string CurrentContent { get; set; } = "";
    public bool HasItems => TechnicalQuestions.Any() || Tradeoffs.Any() || Concerns.Any();
}
```

## サービス設計

```csharp
// Services/MeetingNotesService.cs

public class MeetingNotesService
{
    // 1. 分析: 会議メモ → 構造化結果
    public async Task<MeetingAnalysisResult> AnalyzeAsync(
        string meetingNotes,
        ProjectInfo project,
        string? workstreamId,
        CancellationToken ct)
    {
        // 1. コンテキストファイル読み込み
        //    - current_focus.md (既存コンテンツ、FocusUpdate の入力)
        //    - tensions.md (重複チェック用)
        // 2. プロンプト組み立て
        // 3. LLM 呼び出し (1回)
        // 4. JSON パース → MeetingAnalysisResult
        // 5. FocusUpdate の ProposedContent を組み立て (current_focus.md にマージ)
        // 6. Tensions の AppendContent を組み立て (tensions.md のセクションに追記)
    }

    // 2. Decision Log 作成 (選択された decision ごとに1ファイル)
    public async Task<List<string>> ApplyDecisionsAsync(
        MeetingAnalysisResult result,
        ProjectInfo project,
        string? workstreamId)
    {
        // IsSelected == true の decision だけ処理
        // ファイル名: YYYY-MM-DD_{topic}.md
        // 同日同名なら _a, _b サフィックス
        // 保存先: GetActiveDecisionLogDir() と同じロジック
        // 返り値: 作成したファイルパスのリスト
    }

    // 3. current_focus.md 更新
    public async Task<string> ApplyFocusAsync(
        MeetingAnalysisResult result,
        ProjectInfo project,
        string? workstreamId)
    {
        // IsSelected == false なら何もしない
        // FocusUpdateService と同じバックアップ処理 (focus_history/)
        // ProposedContent を書き込み
        // 返り値: 更新したファイルパス
    }

    // 4. tensions.md 更新
    public async Task<string?> ApplyTensionsAsync(
        MeetingAnalysisResult result,
        ProjectInfo project,
        string? workstreamId)
    {
        // IsSelected == false または HasItems == false なら何もしない
        // AppendContent を tensions.md の各セクションに追記
        // Last Update 行を更新
        // 返り値: 更新したファイルパス (変更なければ null)
    }
}
```

## UI 設計

### エントリーポイント

1. Command Palette: `"meeting"` コマンド → 入力ダイアログを表示
2. Editor ツールバー: AI 有効時に「Import Meeting Notes」ボタン追加 (プロジェクト選択済みの時のみ有効)

### ダイアログ1: 入力ダイアログ

Pattern B (DashboardPage の multi-section dialog) を参考に実装。

```
Window (600 x SizeToContent, NoResize)
├─ titleBar: "📋 Import Meeting Notes"
├─ content:
│  ├─ プロジェクト表示 (選択済みの場合はラベルのみ、未選択は ComboBox)
│  ├─ Workstream 選択 ComboBox (任意: "General" or workstream リスト)
│  ├─ TextBox (MultiLine, AcceptsReturn=True, MinHeight=160)
│  │   PlaceholderText: "Paste meeting notes here..."
│  └─ ヒントテキスト: "Decisions, next actions, and open questions will be detected."
└─ footer: [Cancel] [Analyze →]
```

### ダイアログ2: プレビュー/承認ダイアログ

Pattern D (ShowFocusUpdateProposalDialogAsync) をベースにタブ構造を追加。

```
Window (780 x 580, CanResize)
├─ titleBar: "📋 Meeting Analysis: {project} {date}"
├─ summary bar: "2 decisions · 3 focus items · 1 tension detected"
├─ tabs: TabControl
│  ├─ Tab "Decisions (N)" - チェックボックス付きリスト
│  │   各エントリ: チェックボックス + タイトル + "展開 ▼" ボタン
│  │   展開時: decision_log ドラフト全文 (AvalonEdit readonly)
│  ├─ Tab "Focus" - current_focus.md の before/after
│  │   上段: diff ビュー (DiffLineBackgroundRenderer 使用)
│  │   チェックボックス: "Apply focus update"
│  └─ Tab "Tensions (N)" - 追記予定テキスト
│      AvalonEdit readonly で追記内容を表示
│      チェックボックス: "Add to tensions.md"
├─ refine bar: [Refine instructions TextBox] [Refine]
└─ footer: [Cancel] [Apply Selected (N items)]
```

### Refine の対象

Refine ボタンは現在アクティブなタブの内容を対象とする:
- Decisions タブ: 全 decision の再生成 (instruction を追加して LLM 再呼び出し)
- Focus タブ: focus の変更のみ再生成
- Tensions タブ: tensions の追記のみ再生成

## 実装タスク

### Phase 1: モデルとサービス基盤

- [ ] 1-1. `MeetingNotesModels.cs` を新規作成
  - `MeetingAnalysisResult`, `MeetingDecision`, `MeetingFocusUpdate`, `MeetingTensions`
  - ファイル: `Models/MeetingNotesModels.cs`

- [ ] 1-2. `MeetingNotesService` を新規作成: AnalyzeAsync()
  - プロンプト定数を定義 (System + User Prompt の組み立て)
  - LLM 呼び出し (1回) → JSON パース
  - JSON パースエラー時のフォールバック (空結果を返す)
  - ファイル: `Services/MeetingNotesService.cs`

- [ ] 1-3. `MeetingNotesService` に FocusUpdate 組み立てロジックを実装
  - `result.FocusUpdate.RecentContext` を `## 最近あったこと` セクションに追記
  - `result.FocusUpdate.NextActions` を `## 次やること` セクションに追記
  - current_focus.md のセクション構造を保持 (正規表現でセクション検出)
  - ProposedContent / CurrentContent をセット
  - ファイル: `Services/MeetingNotesService.cs`

- [ ] 1-4. `MeetingNotesService` に TensionsAppend 組み立てロジックを実装
  - tensions.md の3セクション (`## 技術的なオープンクエスチョン`, `## 未解決のトレードオフ`, `## プロジェクト上の懸念・違和感`) を検出
  - 各セクションの末尾に追記
  - `Last Update` 行を更新
  - AppendContent / CurrentContent をセット
  - ファイル: `Services/MeetingNotesService.cs`

- [ ] 1-5. `MeetingNotesService` に Apply メソッド群を実装
  - `ApplyDecisionsAsync()`: AI Decision Log 計画と同じファイル保存ロジック (重複確認)
  - `ApplyFocusAsync()`: FocusUpdateService と同じバックアップ + 保存ロジック
  - `ApplyTensionsAsync()`: tensions.md への追記保存
  - ファイル: `Services/MeetingNotesService.cs`

- [ ] 1-6. DI 登録
  - `App.xaml.cs` に `MeetingNotesService` をシングルトン登録
  - ファイル: `App.xaml.cs`

### Phase 2: ViewModel とコマンド

- [ ] 2-1. `EditorViewModel` に `ImportMeetingNotesCommand` を追加
  - AI 有効かつプロジェクト選択済みの場合のみ実行可能
  - コールバック `RequestMeetingNotesInput` / `RequestMeetingNotesPreview` を定義
  - ファイル: `ViewModels/EditorViewModel.cs`

- [ ] 2-2. Command Palette に `"meeting"` コマンドを追加
  - ラベル: `"meeting"`, 表示: `"[>]  meeting (Import Meeting Notes)"`
  - AI 有効の場合のみリストに追加 (AI 無効時は非表示)
  - ファイル: `ViewModels/CommandPaletteViewModel.cs`

### Phase 3: UI - 入力ダイアログ

- [ ] 3-1. 入力ダイアログを実装
  - Pattern B (DashboardPage の multi-section) を参考
  - プロジェクト・Workstream 表示/選択
  - 会議メモ入力 TextBox (MultiLine, MinHeight=160)
  - [Analyze] ボタン → LLM 呼び出し → プレビューダイアログへ
  - ローディング中はボタンを無効化、インジケータ表示
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

### Phase 4: UI - プレビューダイアログ

- [ ] 4-1. プレビューダイアログの骨格を実装
  - Pattern D (ShowFocusUpdateProposalDialogAsync) をベースに TabControl を追加
  - タブヘッダーに件数バッジ: "Decisions (2)", "Focus", "Tensions (1)"
  - TaskCompletionSource で非同期返値
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

- [ ] 4-2. Decisions タブを実装
  - `MeetingDecision` ごとにチェックボックス + タイトル行を生成
  - 「展開 ▼」で decision_log ドラフト全文を AvalonEdit (readonly) で表示
  - チェック/アンチェックで `IsSelected` を更新
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

- [ ] 4-3. Focus タブを実装
  - 上段: diff ビュー (DiffLineBackgroundRenderer、FocusUpdate と同じパターン)
  - チェックボックス: "Apply focus update to current_focus.md"
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

- [ ] 4-4. Tensions タブを実装
  - AvalonEdit (readonly) で追記予定テキストを表示
  - チェックボックス: "Add to tensions.md"
  - tensions.md が存在しない場合: "tensions.md not found — will be created" と表示
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

- [ ] 4-5. Refine を実装
  - テキスト入力 + Refine ボタン
  - 現在アクティブなタブの内容を再生成 (全体再生成は負荷が高いのでタブ単位)
  - FocusUpdate の RefineAsync と同パターン (会話履歴を保持)
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

- [ ] 4-6. Apply Selected ボタンを実装
  - チェックされた項目を順番に Apply (Decisions → Focus → Tensions)
  - 作成/更新されたファイルパスを収集
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

### Phase 5: 結果処理

- [ ] 5-1. 適用後のツリー更新とファイルオープン
  - ファイルツリーを再構築 (BuildFileTree)
  - 作成したファイルを順にエディタで開く (先頭の decision_log を優先)
  - ファイル: `ViewModels/EditorViewModel.cs`

- [ ] 5-2. 結果サマリの表示
  - 「3 files created/updated」等のメッセージを簡易ダイアログで表示
  - または Editor のステータスバーに一時表示
  - ファイル: `ViewModels/EditorViewModel.cs`

### Phase 6: エラーハンドリング

- [ ] 6-1. JSON パース失敗時のフォールバック
  - LLM が非 JSON テキストを返した場合、エラーダイアログで内容を表示
  - ユーザーが手動でコピーできるよう、生のレスポンスを表示
  - ファイル: `Services/MeetingNotesService.cs`

- [ ] 6-2. コンテキストファイル不在時の処理
  - current_focus.md が存在しない場合: FocusUpdate をスキップ、tabs に "(file not found)" 表示
  - tensions.md が存在しない場合: 新規作成する (テンプレートから)
  - ファイル: `Services/MeetingNotesService.cs`

- [ ] 6-3. プロジェクト未選択時の処理
  - Command Palette から起動した場合、プロジェクト選択ドロップダウンを表示
  - AI 無効時は Command Palette からも非表示
  - ファイル: `ViewModels/CommandPaletteViewModel.cs`

## ファイル追加/変更一覧

### 新規ファイル

| ファイル | 説明 |
|---|---|
| `Models/MeetingNotesModels.cs` | 会議分析のデータモデル |
| `Services/MeetingNotesService.cs` | 分析・Apply のオーケストレーション |

### 変更ファイル

| ファイル | 変更内容 |
|---|---|
| `App.xaml.cs` | MeetingNotesService の DI 登録 |
| `ViewModels/EditorViewModel.cs` | ImportMeetingNotesCommand とコールバック追加 |
| `ViewModels/CommandPaletteViewModel.cs` | "meeting" コマンド追加 |
| `Views/Pages/EditorPage.xaml` | ツールバーにボタン追加 |
| `Views/Pages/EditorPage.xaml.cs` | 入力ダイアログ + プレビューダイアログの実装 |

## 他の計画との比較

| 観点 | AI Decision Log | Smart Standup | Meeting Notes Import |
|---|---|---|---|
| 入力 | ユーザーの1行入力 + focus差分 | 自動収集 | 自由形式のメモ (多) |
| 出力ファイル数 | 1 (decision_log) | 1 (standup) | 1〜N (decision_log + focus + tensions) |
| LLM 呼び出し | 1回 + Refine | 1回 | 1回 + Refine (タブ単位) |
| ユーザー操作 | 入力 → プレビュー → 承認 | 全自動 | 入力 → タブ選択 → 承認 |
| 新規サービス | DecisionLogGeneratorService | StandupGeneratorService 拡張 | MeetingNotesService |
| 依存する既存機能 | FocusUpdateService (パターン参照) | TodayQueueService / StandupGeneratorService | FocusUpdateService + DecisionLogGenerator (パターン参照) |

## 実装順序

Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6

Phase 1-3 で「入力 → LLM 分析 → 結果の確認」が動作する最小構成。
Phase 4 でユーザーが選択的に承認できる完全な UI が完成。
