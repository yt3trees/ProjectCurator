# Meeting Notes Import - 会議メモ → コンテキスト反映 実装仕様

会議メモやセッションメモを貼り付けると、LLM が内容を解析して
decision_log / current_focus.md / open_issues.md への反映を一括提案する機能。

実装完了: 2026-03-26

## 解決する課題

会議後に手動で行っている3つの作業を1つの入力でまとめて処理する:

1. 決定事項 → `decision_log/YYYY-MM-DD_{topic}.md` を作成
2. 次のアクション / 最近の文脈 → `current_focus.md` の「最近あったこと」「次やること」を更新
3. 未解決の問いやリスク → `open_issues.md` に追記

## フロー全体像

```
[入力ダイアログ]
  - プロジェクト表示 (ラベル固定、ViewModel が保持する SelectedProject)
  - Workstream 選択 ComboBox (workstream がある場合のみ表示、なければ "Workstream: General" テキスト)
  - TextBox (MultiLine, AcceptsReturn=True, Height=180, 縦スクロール付き)
  - [Analyze] ボタン (入力が空の間は無効)
  - ウィンドウ MaxHeight = WorkArea.Height - 40 (画面外へのはみ出し防止)
        │
        ▼ ViewModel で MeetingNotesService.AnalyzeAsync() を呼び出す
[LLM が1回の呼び出しで3種類の出力を構造化]
        │
        ▼
[プレビューダイアログ: 3タブ構成]
  ┌────────────────────────────────────────────────────────┐
  │  📋 Meeting Analysis — 2026-03-26                       │
  │  2 decision(s)  ·  3 focus item(s)  ·  1 tension(s)    │
  │                                                         │
  │  [Decisions (2)]  [Focus (3)]  [Tensions (1)]           │
  │                                                         │
  │  (Decisions タブ選択時)                                  │
  │  ┌─────────────────────────────────────────────────┐   │
  │  │ [x] APIフレームワーク選定                         │   │
  │  │     [Show draft ▼]                               │   │
  │  │     (展開時: decision_log ドラフト全文)            │   │
  │  ├─────────────────────────────────────────────────┤   │
  │  │ [x] デプロイ戦略                                  │   │
  │  │     [Show draft ▼]                               │   │
  │  └─────────────────────────────────────────────────┘   │
  │                                                         │
  │  (Focus タブ選択時)                                      │
  │  [x] Apply focus update to current_focus.md             │
  │  Proposed changes  (+ added  - removed)                 │
  │  ┌─────────────────────────────────────────────────┐   │
  │  │   # ProjectAlpha - Current Focus                 │   │
  │  │ + - DBスキーマ見直しの会議を実施                  │   │  ← 緑背景
  │  │   ## 次やること                                   │   │
  │  │ + - GraphQL スキーマ定義の作成                   │   │  ← 緑背景
  │  └─────────────────────────────────────────────────┘   │
  │                                                         │
  │                         [Cancel]  [Apply Selected]      │
  └────────────────────────────────────────────────────────┘
        │
        ▼
[Apply: チェックされた項目だけを適用]
  - Decisions: decision_log ファイルを1件ずつ作成
  - Focus: current_focus.md を更新 (focus_history/ バックアップあり)
  - Tensions: open_issues.md のセクションに追記 (ファイル不在時は新規作成)
        │
        ▼
[結果サマリ MessageBox] → 作成/更新したファイルをエディタで順に開く
  (decision_log を優先して先頭で開く)
```

## アーキテクチャ

```
EditorViewModel / CommandPaletteViewModel
  └── ImportMeetingNotesCommand
        │
        ▼
MeetingNotesService (Services/MeetingNotesService.cs)
  ├── AnalyzeAsync()           会議メモ → LLM 1回 → JSON パース → 構造化結果
  ├── ApplyDecisionsAsync()    decision_log ファイルを作成
  ├── ApplyFocusAsync()        current_focus.md を更新 (focus_history/ バックアップ)
  ├── ApplyTensionsAsync()     open_issues.md のセクション別追記
  └── BuildDecisionLogContent() (static) decision_log ドラフト文字列生成 (UI 側からも呼ぶ)
        │
        ▼
LlmClientService        (既存: ChatCompletionAsync を利用)
FileEncodingService     (既存: BOM 保持読み書き)
ProjectDiscoveryService (既存: プロジェクト一覧・パス解決)
```

### 既存サービスとの役割分担

| 処理 | 既存コード | MeetingNotesService での扱い |
|---|---|---|
| open_issues.md パス解決 | `CaptureService` の同様ロジック | 同じロジックを独自実装 (workstream フォールバックあり) |
| open_issues.md 末尾への1行追記 | `CaptureService.AppendToTensionsAsync()` | セクション別挿入が必要なため別実装 |
| current_focus.md backup | `FocusUpdateService` の `focus_history/` コピー | 同じパターンを流用 |
| decision_log ファイル命名 | `EditorViewModel.GetUniqueDecisionLogPath()` | 同じロジックをサービス内に実装 |
| LLM 1回呼び出し | `LlmClientService.ChatCompletionAsync()` | そのまま利用 |

## LLM 呼び出し設計

呼び出し回数: 1回 (入力1回で Decisions / Focus / Tensions を同時に出力)

### System Prompt の分類ルール (実装済み)

- `filename_topic`: 常に English snake_case (ファイル名用)
- `title` / body text: ユーザーの言語設定に従う (LlmUserProfile のプロファイル注入に委ねる)
- decisions: 実際に選択肢の比較があった場合のみ記録。1択は除外
- focus_updates: `最近あったこと` / `次やること` セクションへの追記項目
- tensions: 3カテゴリ (technical_questions / tradeoffs / concerns)

### User Prompt 構造

```
## Meeting notes to analyze
{会議メモの全文}

## Context
- Project: {project name}
- Workstream: {workstream id or "general"}
- Date: {today}

## Existing tensions (to avoid duplicates)
{open_issues.md の現在の内容、または "(none)"}

## Existing focus (for context)
{current_focus.md の現在の内容、または "(none)"}
```

## データモデル (Models/MeetingNotesModels.cs)

```csharp
public class MeetingAnalysisResult
{
    public List<MeetingDecision> Decisions { get; set; } = [];
    public MeetingFocusUpdate FocusUpdate  { get; set; } = new();
    public MeetingTensions Tensions        { get; set; } = new();
    public string DebugPrompt   { get; set; } = "";
    public string DebugResponse { get; set; } = "";
}

public class MeetingDecision
{
    public string FilenameTopic  { get; set; } = "";  // "api_framework_selection"
    public string Title          { get; set; } = "";  // ユーザーの言語で生成
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
    public bool   IsSelected     { get; set; } = true;
}

public class MeetingFocusUpdate
{
    public List<string> RecentContext { get; set; } = [];  // "最近あったこと" に追記
    public List<string> NextActions   { get; set; } = [];  // "次やること" に追記
    public bool   IsSelected      { get; set; } = true;
    public string ProposedContent { get; set; } = "";  // current_focus.md にマージ済み全文
    public string CurrentContent  { get; set; } = "";  // diff 表示の基準
}

public class MeetingTensions
{
    public List<string> TechnicalQuestions { get; set; } = [];
    public List<string> Tradeoffs          { get; set; } = [];
    public List<string> Concerns           { get; set; } = [];
    public bool   IsSelected     { get; set; } = true;
    public string AppendContent  { get; set; } = "";  // プレビュー表示用テキスト
    public string CurrentContent { get; set; } = "";
    public bool HasItems => TechnicalQuestions.Any() || Tradeoffs.Any() || Concerns.Any();
}

public class MeetingNotesInputResult
{
    public string  MeetingNotes  { get; set; } = "";
    public string? WorkstreamId  { get; set; }
}
```

## サービス設計 (Services/MeetingNotesService.cs)

### AnalyzeAsync

1. `ResolveFocusPath` / `ResolveTensionsPath` でパス解決 (workstream フォールバックあり)
2. current_focus.md と open_issues.md を読み込み (不在なら空文字)
3. `ChatCompletionAsync` で LLM 呼び出し (1回)
4. JSON パース → `MeetingAnalysisResult`。パース失敗時は空結果を返す
5. `BuildFocusProposed` で ProposedContent を組み立て (`InsertItemsAfterSection` でセクション末尾に追記)
6. `BuildTensionsAppend` で AppendContent を組み立て (プレビュー表示用)

### ApplyFocusAsync

- `IsSelected == false` または `ProposedContent` が空なら何もしない
- current_focus.md が存在しない場合も何もしない
- `focus_history/YYYY-MM-DD.md` にバックアップ (同日分が既にあればスキップ)
- ProposedContent を書き込み (エンコーディング保持)

### ApplyTensionsAsync

- `IsSelected == false` または `HasItems == false` なら何もしない
- ファイル不在時は `BuildNewTensionsTemplate()` から新規作成
  - テンプレート: `## 技術的なオープンクエスチョン` / `## 未解決のトレードオフ` / `## プロジェクト上の懸念・違和感`
- `InsertTensionItems` で各セクション末尾に追記 (セクションが存在しない場合はファイル末尾に追加)
- `UpdateLastUpdateLine` で `Last Update: YYYY-MM-DD` を更新 / 追加

### open_issues.md セクション対応表

| JSON キー | 見出し候補 (先頭優先) |
|---|---|
| `technical_questions` | `## 技術的なオープンクエスチョン` / `## Technical Questions` / `## Open Questions` |
| `tradeoffs` | `## 未解決のトレードオフ` / `## Tradeoffs` / `## Trade-offs` |
| `concerns` | `## プロジェクト上の懸念・違和感` / `## Concerns` / `## Risks` |

## UI 設計

### エントリーポイント

1. Command Palette: `"meeting"` コマンド → EditorPage に遷移後 `ImportMeetingNotesCommand.ExecuteAsync`
   - AI 有効時のみコマンドリストに追加
2. Editor ツールバー: `ImportMeetingNotesButton` (NotebookAdd24 アイコン)
   - `Visibility={Binding IsAiEnabled, Converter=BoolToVisibility}` で AI 無効時は非表示
   - `CanExecute`: `SelectedProject != null && IsAiEnabled`

### 起動フロー

```
[ツールバーボタン / Command Palette]
        │
        ▼
EditorViewModel.ImportMeetingNotesAsync()
        ├─ SelectedProject == null → return
        ├─ LlmApiKey 未設定 → MessageBox → return
        │
        ▼ RequestMeetingNotesInput コールバック
EditorPage.xaml.cs: ShowMeetingNotesInputDialogAsync()
        │ [Analyze] ボタン押下
        ▼
MeetingNotesService.AnalyzeAsync()  ← LLM 1回
        │ 失敗 → ShowScrollableError → return
        ▼ RequestMeetingNotesPreview コールバック
EditorPage.xaml.cs: ShowMeetingNotesPreviewDialogAsync()
        │ [Apply Selected]
        ▼
MeetingNotesService.Apply*Async() (順番: Decisions → Focus → Tensions)
        │ 失敗 → MessageBox → return
        ▼
BuildFileTree() + OpenFileAndSelectNodeAsync() (decision_log を優先)
MessageBox: "{N} file(s) created/updated."
```

### コールバック定義

```csharp
// EditorViewModel.cs
public Func<ProjectInfo, List<WorkstreamInfo>, Task<MeetingNotesInputResult?>>? RequestMeetingNotesInput;
public Func<MeetingAnalysisResult, Task<bool>>? RequestMeetingNotesPreview;
// ShowScrollableError は既存を共用

// EditorPage.xaml.cs コンストラクタ
ViewModel.RequestMeetingNotesInput   = ShowMeetingNotesInputDialogAsync;
ViewModel.RequestMeetingNotesPreview = ShowMeetingNotesPreviewDialogAsync;
```

### ダイアログ1: 入力ダイアログ

```
Window (600 x SizeToContent, NoResize, MaxHeight=WorkArea.Height-40)
├─ titleBar: "📋 Import Meeting Notes"
├─ content (StackPanel):
│  ├─ プロジェクト名ラベル ("Project: {name}")
│  ├─ Workstream ComboBox (workstream がある場合) / テキスト "Workstream: General" (ない場合)
│  ├─ ラベル "Paste meeting notes here..."
│  ├─ TextBox (MultiLine, AcceptsReturn=True, Height=180, VerticalScroll)
│  └─ ヒントテキスト
└─ footer: [Cancel] [Analyze]  ← Analyze は入力が空の間 IsEnabled=false
```

### ダイアログ2: プレビュー/承認ダイアログ

```
Window (780 x 580, CanResize, WindowChrome)
├─ titleBar: "📋 Meeting Analysis — {date}"
├─ summaryBar: "{N} decision(s)  ·  {N} focus item(s)  ·  {N} tension(s)"
├─ TabControl
│  ├─ Tab "Decisions (N)"
│  │   ScrollViewer > StackPanel:
│  │   各 decision:
│  │     [x] CheckBox: "{title}  [{status}]"
│  │     Button "Show draft ▼" → 展開時 AvalonEdit (readonly, MaxHeight=200)
│  │
│  ├─ Tab "Focus (N)"
│  │   Grid (Row*):
│  │     Row 0: [x] "Apply focus update to current_focus.md"  (N=0 なら IsEnabled=false)
│  │     Row 1: "Proposed changes  (+ added  - removed)"
│  │     Row 2: AvalonEdit (DiffLineBackgroundRenderer, IsReadOnly, タブ全高を使用)
│  │             N=0 の場合は "No focus updates detected." を表示
│  │
│  └─ Tab "Tensions (N)"
│      ScrollViewer > StackPanel:
│        [x] "Add to open_issues.md"  (HasItems=false なら IsEnabled=false)
│        AvalonEdit (readonly): AppendContent を表示
│        open_issues.md 不在時: "open_issues.md not found — will be created"
│
└─ footer: [Cancel] [Apply Selected]
```

注: 計画段階にあった Refine バーは今回の実装では省略。必要に応じて後から追加する。

## ファイル追加/変更一覧

### 新規ファイル

| ファイル | 説明 |
|---|---|
| `Models/MeetingNotesModels.cs` | 会議分析のデータモデル |
| `Services/MeetingNotesService.cs` | 分析・Apply のオーケストレーション |

### 変更ファイル

| ファイル | 変更内容 |
|---|---|
| `App.xaml.cs` | `MeetingNotesService` をシングルトン登録 |
| `ViewModels/EditorViewModel.cs` | `ImportMeetingNotesCommand` / コールバック追加 / コンストラクタ DI 追加 |
| `ViewModels/CommandPaletteViewModel.cs` | `"meeting"` コマンド追加 (AI 有効時のみ) |
| `Views/Pages/EditorPage.xaml` | `ImportMeetingNotesButton` 追加 (NotebookAdd24 アイコン) |
| `Views/Pages/EditorPage.xaml.cs` | コールバック登録 / 入力ダイアログ / プレビューダイアログ実装 |

### 参照したが変更しないファイル

| ファイル | 参照用途 |
|---|---|
| `Services/CaptureService.cs` | open_issues.md パス解決・新規作成テンプレートのパターン |
| `Services/FocusUpdateService.cs` | focus_history/ バックアップロジック |
| `Views/ProposalReviewDialog.cs` | DiffLineBackgroundRenderer / RefreshDiff パターン |

## 他の計画との比較

| 観点 | Global Capture (実装済み) | AI Decision Log (実装済み) | Meeting Notes Import (実装済み) |
|---|---|---|---|
| 入力 | 1行のフリーテキスト | ユーザーの1行入力 + focus差分 | 自由形式のメモ (多) |
| 出力ファイル数 | 1 (カテゴリ次第) | 1 (decision_log) | 1〜N (decision_log + focus + tensions) |
| LLM 呼び出し | 1回 (分類) | 1回 + Refine | 1回 (Refine は未実装) |
| ユーザー操作 | 入力 → 確認 → Route | 入力 → プレビュー → 承認 | 入力 → タブ選択 → 承認 |
| 新規サービス | CaptureService | DecisionLogGeneratorService | MeetingNotesService |
| open_issues.md 書き込み | 末尾への1行追記 | なし | セクション別の複数行追記 |

## 今後の拡張候補

- Refine バー (タブ単位での再生成)
- 入力ダイアログでの LLM 分析中ローディング表示
- current_focus.md が存在しない場合の "(file not found)" 表示
