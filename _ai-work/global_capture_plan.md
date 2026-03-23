# Global Capture + AI Auto-Routing - 実装計画

グローバルホットキーでどこからでもフリーテキストを入力し、AIが「これはタスクか、テンションか、フォーカス更新か、意思決定か」を自動判定して適切なファイル/サービスにルーティングする機能。

ナビゲーション不要、ファイルを開く必要なし。思いついた瞬間にキャプチャして、AIが振り分ける。

## コンセプト

```
どこからでも Ctrl+Shift+C → 軽量キャプチャウィンドウが出現
  ↓
「Xプロジェクトの認証、JWTじゃなくてセッションベースにしたほうがいいかも。
 パフォーマンス的にステートレスが有利だけど、既存のミドルウェアとの整合性が...」
  ↓ Enter で送信
AI が分類:
  種別: tension (未解決の技術課題)
  プロジェクト: ProjectAlpha (「認証」「ミドルウェア」から推定)
  要約: 認証方式の再検討 - JWT vs セッションベース
  ↓
ユーザーに確認 → tensions.md に追記
```

## 方針

- 専用のグローバルホットキー (Ctrl+Shift+C) でキャプチャウィンドウを起動
- 既存の HotkeyService を拡張し、複数ホットキーに対応
- キャプチャウィンドウは MainWindow とは独立した軽量ウィンドウ
- AI 分類は LlmClientService.ChatCompletionAsync で 1 回の呼び出し
- AI 無効時はキャプチャ自体は使えるが、手動でカテゴリとプロジェクトを選択
- ルーティング先への書き込みは既存サービス (FileEncodingService 等) を利用
- 新規サービス: CaptureService (分類 + ルーティングのオーケストレーション)
- 新規ウィンドウ: CaptureWindow.xaml (軽量入力 UI)

## ルーティング先の定義

| カテゴリ | 振り分け先 | 書き込み方式 |
|---|---|---|
| task | プロジェクトの `asana-tasks.md` | 末尾に `- [ ] {要約}` を追記 |
| tension | プロジェクトの `tensions.md` | 末尾に箇条書きで追記 |
| focus_update | プロジェクトの `current_focus.md` | Editor に遷移して差分提案 (FocusUpdate と同パターン) |
| decision | DecisionLogGeneratorService | Editor に遷移して AI Decision Log フローを起動 |
| memo | `_config/capture_log.md` | タイムスタンプ付きで追記 (どこにも属さないメモ) |

## UI 設計

### キャプチャウィンドウ (初期状態)

```
┌──────────────────────────────────────────────────────────┐
│  Quick Capture                                     [×]   │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  ┌────────────────────────────────────────────────────┐  │
│  │ (入力エリア: 複数行 TextBox)                       │  │
│  │                                                    │  │
│  │                                                    │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  Project: [Auto-detect ▼]          [Capture ▶] [Cancel]  │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

- ウィンドウサイズ: 520x240 (コンパクト)
- カーソル位置の近くに表示 (マルチモニター対応)
- Esc で閉じる、Ctrl+Enter で送信
- Project ドロップダウン: "Auto-detect" (デフォルト) + 全プロジェクトリスト
- AI 無効時: Project ドロップダウンの隣に Category ドロップダウンも表示

### 分類結果の確認 (AI 応答後)

```
┌──────────────────────────────────────────────────────────┐
│  Quick Capture                                     [×]   │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  "認証方式の再検討 - JWTじゃなくてセッションベースに     │
│   したほうがいいかも..."                                  │
│                                                          │
│  ─────────────────────────────────────────────────────── │
│                                                          │
│  Category:  🔶 Tension                          [▼]     │
│  Project:   ProjectAlpha                        [▼]     │
│  Summary:   認証方式の再検討 - JWT vs Session   [edit]  │
│                                                          │
│                    [Route ▶] [Back] [Cancel]             │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

- AI の分類結果を表示。ユーザーはドロップダウンで上書き可能
- Summary は AI が生成した要約 (編集可能)
- [Route] で確定、[Back] で入力画面に戻る
- Category / Project の変更は即座に反映 (LLM 再呼び出しなし)

### ルーティング完了

```
┌──────────────────────────────────────────────────────────┐
│  Quick Capture                                     [×]   │
├──────────────────────────────────────────────────────────┤
│                                                          │
│  ✓ Added to ProjectAlpha/tensions.md                     │
│                                                          │
│                              [Open File] [Close]         │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

- [Open File] で該当ファイルを Editor で開く
- focus_update / decision の場合は [Route] で直接 Editor に遷移

## アーキテクチャ

```
CaptureWindow.xaml / .xaml.cs (新規)
  └── [Capture] ボタン (Ctrl+Enter)
        │
        ▼
CaptureService (新規)
  ├── ClassifyAsync(input, projectHint?, ct)
  │     ├── プロンプト構築 (入力テキスト + プロジェクト一覧)
  │     ├── LlmClientService.ChatCompletionAsync()
  │     └── JSON 解析 → CaptureClassification
  │
  └── RouteAsync(classification, originalInput, ct)
        ├── task     → AppendToAsanaTasksAsync()
        ├── tension  → AppendToTensionsAsync()
        ├── memo     → AppendToCaptureLogAsync()
        ├── focus_update → return NavigationRequest (Editor + focus)
        └── decision     → return NavigationRequest (Editor + decision)
              │
              ▼
        FileEncodingService (既存: ファイル読み書き)

HotkeyService (既存: 拡張)
  └── 複数ホットキー対応
        ├── HOTKEY_ID = 9000 (既存: アプリ切り替え)
        └── HOTKEY_ID = 9001 (新規: キャプチャ)

MainWindow.xaml.cs (既存: 変更)
  └── キャプチャホットキー受信 → CaptureWindow 表示
```

## データモデル

### CaptureClassification

```csharp
public class CaptureClassification
{
    public string Category { get; set; }    // "task" | "tension" | "focus_update" | "decision" | "memo"
    public string ProjectName { get; set; } // マッチしたプロジェクト名 or ""
    public string Summary { get; set; }     // AI 生成の要約 (1行)
    public string Body { get; set; }        // ルーティング先に書き込む整形済みテキスト
    public double Confidence { get; set; }  // 0.0-1.0 (低い場合はユーザー確認を強調)
    public string Reasoning { get; set; }   // 分類理由 (デバッグ用)
}
```

### CaptureRouteResult

```csharp
public class CaptureRouteResult
{
    public bool Success { get; set; }
    public string Message { get; set; }           // "Added to ProjectAlpha/tensions.md"
    public string? TargetFilePath { get; set; }   // 書き込み先のフルパス
    public bool RequiresNavigation { get; set; }  // true = Editor 遷移が必要
    public string? NavigationProjectName { get; set; }
    public string? NavigationFilePath { get; set; }
}
```

## 実装タスク

### Phase 1: HotkeyService の複数ホットキー対応

- [ ] 1-1. HotkeyService にキャプチャ用ホットキーの登録機能を追加
  - 新しい定数: `CAPTURE_HOTKEY_ID = 9001`
  - `RegisterCapture(Window window)` メソッドを追加
  - WndProc で HOTKEY_ID を分岐して異なるコールバックを呼ぶ
  - `OnCaptureActivated` コールバックプロパティを追加
  - ファイル: `Services/HotkeyService.cs`

- [ ] 1-2. Win32Interop に CAPTURE_HOTKEY_ID 定数を追加
  - ファイル: `Helpers/Win32Interop.cs`

- [ ] 1-3. AppConfig にキャプチャホットキー設定を追加
  - `CaptureHotkey` プロパティ (HotkeyConfig 型、デフォルト: Ctrl+Shift+C)
  - ファイル: `Models/AppConfig.cs`

- [ ] 1-4. Settings UI にキャプチャホットキー設定を追加
  - 既存のホットキー設定 UI と同パターン
  - ファイル: `Views/Pages/SettingsPage.xaml`, `ViewModels/SettingsViewModel.cs`

### Phase 2: CaptureService (分類エンジン)

- [ ] 2-1. CaptureClassification / CaptureRouteResult モデルを作成
  - ファイル: `Models/CaptureModels.cs` (新規)

- [ ] 2-2. CaptureService の骨格を作成
  - コンストラクタ DI: LlmClientService, ConfigService, FileEncodingService, ProjectDiscoveryService
  - ファイル: `Services/CaptureService.cs` (新規)

- [ ] 2-3. ClassifyAsync() を実装
  - プロジェクト一覧を取得 (ProjectDiscoveryService)
  - System Prompt + User Prompt を構築
  - LlmClientService.ChatCompletionAsync() を呼び出し
  - JSON レスポンスを CaptureClassification に解析
  - パース失敗時: category="memo" にフォールバック
  - ファイル: `Services/CaptureService.cs`

- [ ] 2-4. AI 無効時の手動分類パスを実装
  - ClassifyAsync を呼ばず、ユーザー選択の category + project で CaptureClassification を構築
  - Summary は入力テキストの先頭 50 文字
  - Body は入力テキスト全文
  - ファイル: `Services/CaptureService.cs`

### Phase 3: CaptureService (ルーティングエンジン)

- [ ] 3-1. RouteAsync() のディスパッチロジックを実装
  - category に基づいて個別メソッドに振り分け
  - ファイル: `Services/CaptureService.cs`

- [ ] 3-2. AppendToAsanaTasksAsync() を実装 (task ルート)
  - プロジェクトの asana-tasks.md パスを解決
  - ファイルが存在する場合: `## 未着手` セクションの末尾に `- [ ] {summary}` を追記
  - ファイルが存在しない場合: 新規作成して追記
  - workstream が特定できる場合: workstream 配下の asana-tasks.md に追記
  - ファイル: `Services/CaptureService.cs`

- [ ] 3-3. AppendToTensionsAsync() を実装 (tension ルート)
  - プロジェクトの tensions.md パスを解決 (AiContextContentPath 配下)
  - ファイル末尾に `- {summary}: {body の1行要約}` を追記
  - ファイルが存在しない場合: ヘッダー付きで新規作成
  - ファイル: `Services/CaptureService.cs`

- [ ] 3-4. AppendToCaptureLogAsync() を実装 (memo ルート)
  - `_config/capture_log.md` に追記
  - フォーマット: `## {yyyy-MM-dd HH:mm}\n{original input}\n`
  - ファイル: `Services/CaptureService.cs`

- [ ] 3-5. focus_update / decision ルートの NavigationRequest 生成を実装
  - focus_update: ターゲットプロジェクトの current_focus.md パスを返す
  - decision: ターゲットプロジェクト名 + 入力テキスト (初期入力として) を返す
  - 実際のファイル編集は既存の EditorViewModel フローに委譲
  - ファイル: `Services/CaptureService.cs`

### Phase 4: CaptureWindow (UI)

- [ ] 4-1. CaptureWindow.xaml を作成
  - WindowStyle=None、最小限のフレームレスウィンドウ
  - ダークモードテーマリソース (AppSurface0/1、AppText)
  - WindowChrome 適用 (白枠防止)
  - サイズ: 520x240、Topmost=true
  - ファイル: `Views/CaptureWindow.xaml` (新規)

- [ ] 4-2. CaptureWindow.xaml.cs の初期入力画面を実装
  - TextBox (AcceptsReturn=true、複数行)
  - Project ComboBox ("Auto-detect" + プロジェクトリスト)
  - [Capture] ボタン (Ctrl+Enter) + [Cancel] ボタン (Esc)
  - ウィンドウ位置: マウスカーソル付近 (画面端からはみ出さないよう調整)
  - フォーカス: TextBox に自動フォーカス
  - ファイル: `Views/CaptureWindow.xaml.cs` (新規)

- [ ] 4-3. 分類結果の確認画面を実装
  - AI 応答後に入力エリアを読み取り専用に切り替え
  - Category ドロップダウン (AI 結果をデフォルト選択、手動変更可能)
  - Project ドロップダウン (AI 結果をデフォルト選択、手動変更可能)
  - Summary TextBox (編集可能)
  - [Route] [Back] [Cancel] ボタン
  - ファイル: `Views/CaptureWindow.xaml.cs`

- [ ] 4-4. ルーティング完了画面を実装
  - 成功メッセージ表示
  - [Open File] ボタン (テキスト追記系)
  - focus_update / decision の場合: 自動で Editor に遷移してウィンドウを閉じる
  - 2秒後に自動で閉じるオプション (追記系の場合)
  - ファイル: `Views/CaptureWindow.xaml.cs`

- [ ] 4-5. AI 無効時の手動モード UI
  - Category ドロップダウンを入力画面に表示 (AI 分類をスキップ)
  - Project ドロップダウン必須 (Auto-detect なし)
  - [Capture] で確認画面をスキップし直接ルーティング
  - ファイル: `Views/CaptureWindow.xaml.cs`

- [ ] 4-6. ローディング表示
  - AI 分類中にスピナー (ProgressBar IsIndeterminate=true) を表示
  - [Cancel] で CancellationTokenSource をキャンセル
  - ファイル: `Views/CaptureWindow.xaml.cs`

### Phase 5: MainWindow / App 統合

- [ ] 5-1. App.xaml.cs に CaptureService と CaptureWindow の DI 登録
  - CaptureService: Singleton
  - CaptureWindow: Transient (毎回新しいインスタンス)
  - ファイル: `App.xaml.cs`

- [ ] 5-2. MainWindow.xaml.cs にキャプチャホットキーハンドラを追加
  - `_hotkeyService.OnCaptureActivated = ShowCaptureWindow;`
  - ShowCaptureWindow: CaptureWindow を生成して ShowDialog
  - ルーティング結果が NavigationRequest の場合: Editor に遷移
  - ファイル: `MainWindow.xaml.cs`

- [ ] 5-3. CaptureWindow から MainWindow への遷移コールバックを設定
  - focus_update → MainWindow.NavigateToEditorAndOpenFile(project, focusPath)
  - decision → MainWindow.NavigateToEditor(project) + DecisionLog フロー起動
  - ファイル: `MainWindow.xaml.cs`

### Phase 6: エラーハンドリングと品質

- [ ] 6-1. LLM API エラー時のフォールバック
  - API エラー → 手動モードに切り替え (Category/Project ドロップダウンを表示)
  - エラーメッセージをウィンドウ内に表示 (モーダルダイアログではなく)
  - ファイル: `Views/CaptureWindow.xaml.cs`

- [ ] 6-2. プロジェクト未検出時の処理
  - AI がプロジェクト名を特定できなかった場合 → Project ドロップダウンを必須入力に
  - category が memo の場合はプロジェクト不要
  - ファイル: `Views/CaptureWindow.xaml.cs`

- [ ] 6-3. ファイル書き込み失敗時の処理
  - パスが存在しない、権限エラー等 → エラーメッセージ + memo にフォールバック
  - ファイル: `Services/CaptureService.cs`

- [ ] 6-4. 空入力の防止
  - TextBox が空の場合 [Capture] ボタンを無効化
  - ファイル: `Views/CaptureWindow.xaml.cs`

- [ ] 6-5. 連続キャプチャ対応
  - ルーティング完了後、[New Capture] ボタンで入力画面に戻る (ウィンドウ再生成なし)
  - ファイル: `Views/CaptureWindow.xaml.cs`

## プロンプト設計

### System Prompt

```
You are a classifier for a multi-project manager's quick capture system.
Your job is to analyze free-form input and classify it into the most appropriate category,
identify which project it belongs to, and generate a concise summary.

## Categories
- "task": An actionable to-do item. Something that needs to be done.
  Examples: "○○を実装する", "レビューを依頼する", "ドキュメントを更新する"
- "tension": An unresolved question, concern, trade-off, or risk. Not yet a decision.
  Examples: "AとBどちらがいいか迷っている", "パフォーマンスが心配", "○○との整合性が..."
- "focus_update": A shift in priorities or focus. The user wants to record a change in what they're working on.
  Examples: "今週は○○に集中する", "方針転換: ○○を先にやる", "○○は後回しにする"
- "decision": A concluded choice. The user has decided something and wants to record it.
  Examples: "○○に決めた", "○○を採用する", "○○ではなく○○でいく"
- "memo": General note, idea, or thought that doesn't fit other categories.
  Examples: "○○について調べたい", "来週の会議で○○を話す", random thoughts

## Output rules
- Return a single JSON object. No explanation, no markdown fences.
- Fields:
  {
    "category": "task" | "tension" | "focus_update" | "decision" | "memo",
    "project": "exact project name from the list, or empty string if unclear",
    "summary": "concise one-line summary (max 80 chars)",
    "body": "formatted text suitable for appending to the target file",
    "confidence": 0.0 to 1.0,
    "reasoning": "brief explanation of classification"
  }

## Project matching rules
- Match based on keywords, project names, technology mentions, or domain context
- If the input explicitly mentions a project name, use that
- If ambiguous between projects, set confidence < 0.5 and leave project empty
- Project names are case-insensitive for matching

## Body formatting rules
- For "task": "- [ ] {actionable description}"
- For "tension": "- {question or concern, naturally phrased}"
- For "focus_update": the full input text, lightly edited for clarity
- For "decision": the full input text, structured as "Decision: X. Reason: Y"
- For "memo": the full input text as-is
```

### User Prompt 構造

```
## Available Projects
{For each project:}
- {ProjectName} (Tier: {tier}) - Focus: {first 100 chars of current_focus.md or "no focus file"}

## User Input
{raw input text}

## Context
- Date: {today YYYY-MM-DD}
- User-selected project: {selected project name or "auto-detect"}

Classify the input above.
```

## ファイル追加/変更一覧

### 新規ファイル

| ファイル | 説明 |
|---|---|
| `Models/CaptureModels.cs` | CaptureClassification, CaptureRouteResult モデル |
| `Services/CaptureService.cs` | AI 分類 + ルーティングのオーケストレーション |
| `Views/CaptureWindow.xaml` | キャプチャウィンドウの XAML レイアウト |
| `Views/CaptureWindow.xaml.cs` | キャプチャウィンドウのコードビハインド |

### 変更ファイル

| ファイル | 変更内容 |
|---|---|
| `Services/HotkeyService.cs` | 複数ホットキー対応 (CAPTURE_HOTKEY_ID 追加) |
| `Helpers/Win32Interop.cs` | CAPTURE_HOTKEY_ID 定数追加 |
| `Models/AppConfig.cs` | CaptureHotkey 設定追加 |
| `App.xaml.cs` | CaptureService の DI 登録 |
| `MainWindow.xaml.cs` | キャプチャホットキーハンドラ + CaptureWindow 表示ロジック |
| `Views/Pages/SettingsPage.xaml` | キャプチャホットキー設定 UI |
| `ViewModels/SettingsViewModel.cs` | キャプチャホットキー設定の保存/読み込み |

## 実装順序

Phase 1 (ホットキー拡張) → Phase 2 (分類エンジン) → Phase 3 (ルーティング) → Phase 4 (UI) → Phase 5 (統合) → Phase 6 (品質)

Phase 1-3 が裏側のロジック、Phase 4-5 が UI と統合。
Phase 2 完了時点でユニットテスト的な確認が可能 (コンソールから ClassifyAsync を呼べる)。
Phase 4 完了時点で E2E で動作確認可能。

### 最小動作バージョン (MVP)

Phase 2 + Phase 3 (memo/tension/task のみ) + Phase 4 (4-1, 4-2 のみ) + Phase 5 で最小動作:
- ホットキーは後回し (Dashboard にボタンで代替)
- 確認画面なし (AI 結果をそのまま適用)
- focus_update / decision ルートは後回し

## 他の計画との関係

| 観点 | Global Capture | What's Next | AI Decision Log | Smart Standup |
|---|---|---|---|---|
| LlmClientService | 共通利用 | 共通利用 | 共通利用 | 共通利用 |
| 入力 | ユーザーフリーテキスト | 自動 (メタデータ) | ユーザー入力 + 検出 | 自動 (スケジューラ) |
| 出力 | 複数ファイルに振り分け | ダイアログ表示 | decision_log 保存 | standup 保存 |
| プロジェクト | AI 推定 or 手動選択 | 全プロジェクト横断 | 単一プロジェクト | 全プロジェクト横断 |
| Refine | なし (1回分類) | なし | あり (反復修正) | なし |
| 新規ファイル | 4 | 0 | 2 | 1 |
| HotkeyService 変更 | あり (複数ホットキー) | なし | なし | なし |

Global Capture は他機能と独立して実装可能。decision ルートは AI Decision Log と連携するが、必須ではない (後から接続可能)。

## 将来の拡張案

- 音声入力対応 (Windows Speech Recognition API でテキスト変換後に同じフローに流す)
- キャプチャ履歴の閲覧 (capture_log.md を Timeline ページで時系列表示)
- Asana API 直接書き戻し (task ルートで asana-tasks.md だけでなく Asana にも直接タスク作成)
- コンテキスト添付 (クリップボードの画像やURLを入力と一緒にキャプチャ)
- キーワードベースの即時ルーティング (「TODO:」で始まれば AI 不要で task に直接ルーティング)
