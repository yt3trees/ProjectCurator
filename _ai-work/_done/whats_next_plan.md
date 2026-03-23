# "What should I do next?" - 実装計画

Dashboard に AI ボタンを1つ追加し、全プロジェクトのコンテキストから「今やるべきこと」を優先順位付きで提案する機能。

朝一でアプリを開いてボタンを押すだけで、複数プロジェクト横断の判断コストを削減する。

## 方針

- Dashboard ヘッダーのツールバーに1つボタンを追加
- AI 無効時はボタン非表示 (Focus Update ボタンと同パターン)
- 1回の LLM 呼び出しで 3-5 件の優先アクションを提案
- 各提案から該当プロジェクト/ファイルにワンクリックで遷移可能
- 新規サービスは作らず、DashboardViewModel + DashboardPage.xaml.cs 内で完結
- データ収集は既存の ProjectDiscoveryService + TodayQueueService を利用

## UI 設計

### ボタン配置

Dashboard ヘッダーのツールバー (DashboardPage.xaml 32-78行) に追加:

```
[Dashboard]                [What's Next ✨] [▼ 10 min] [↻] [👁 2]
```

- アイコン: `Lightbulb24` or `Sparkle24` (wpf-ui SymbolRegular)
- ToolTip: "What should I do next?"
- Visibility: `IsAiEnabled` バインディング (BooleanToVisibilityConverter)

### 結果ダイアログ

```
┌─────────────────────────────────────────────────────────────┐
│  ✨ What's Next                                      [×]   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  1. [ProjectAlpha] Review migration script                  │
│     Overdue by 2 days. Blocking next sprint.                │
│                                              [Open ▶]      │
│  ─────────────────────────────────────────────────────────  │
│  2. [ProjectBeta] Update current_focus.md                   │
│     Last updated 8 days ago. Recent Asana activity          │
│     suggests progress not reflected.                        │
│                                              [Open ▶]      │
│  ─────────────────────────────────────────────────────────  │
│  3. [ProjectAlpha] Record decision: auth strategy           │
│     Focus file mentions JWT adoption but no                 │
│     decision log entry exists.                              │
│                                              [Open ▶]      │
│  ─────────────────────────────────────────────────────────  │
│  4. [ProjectGamma] Commit staged changes                    │
│     3 repos have uncommitted changes.                       │
│                                              [Open ▶]      │
│                                                             │
│                                        [Copy] [Close]      │
└─────────────────────────────────────────────────────────────┘
```

- 各提案は番号付き、プロジェクト名 + アクション + 理由の3行構成
- [Open] ボタン: 該当プロジェクトの Editor / Timeline / Git Repos に遷移
- [Copy]: 全提案をクリップボードにコピー (standup やチャットに貼れる)
- ダイアログは DashboardPage.xaml.cs の既存パターン (ShowUncommittedDetailsDialogAsync) を踏襲

## データソース

LLM に渡す入力データ:

| データ | ソース | 提案への活用 |
|---|---|---|
| Today Queue 全タスク | TodayQueueService | 期限超過・当日タスクの優先提案 |
| FocusAge (全プロジェクト) | ProjectInfo | 「focus を更新すべき」提案 |
| current_focus.md 要約 | ProjectInfo.FocusFile | 現在の作業コンテキスト |
| 未コミット変更 | ProjectInfo.UncommittedRepoPaths | 「コミットすべき」提案 |
| tensions.md 有無 | ファイル存在チェック | 「未解決課題に対応すべき」提案 |
| decision_log 直近日付 | ProjectInfo.DecisionLogDates | 「決定を記録すべき」提案 |
| Workstream 状態 | ProjectInfo.Workstreams | クローズ候補の検出 |

### トークン予算の管理

プロジェクト数が多い場合にトークンが膨れないよう:
- current_focus.md は先頭 500 文字のみ抽出 (プレビュー)
- tensions.md は項目数のみ (全文は不要)
- Today Queue は上位 30 件に制限
- プロジェクトメタデータ (FocusAge, 未コミット数等) は数値なので軽量

## アーキテクチャ

```
DashboardPage.xaml
  └── [What's Next] ボタン (Click イベント)
        │
        ▼
DashboardPage.xaml.cs
  └── OnWhatsNextClickAsync()
        ├── データ収集: ViewModel.Projects + ViewModel.TodayQueueTasks
        ├── focus プレビュー読み込み (FileEncodingService)
        ├── プロンプト構築
        ├── LlmClientService.ChatCompletionAsync()
        ├── レスポンス解析 (JSON → 提案リスト)
        └── ダイアログ表示 (提案 + [Open] ボタン)
              │
              └── [Open] クリック → MainWindow.NavigateToEditor(project)
                                  or MainWindow.NavigateToEditorAndOpenFile(project, file)
```

ViewModel にコマンドを追加するのではなく、コードビハインドで直接処理する。
理由: ダイアログ表示と MainWindow への遷移はビューの責務であり、既存のパターン (ShowUncommittedDetailsDialogAsync 等) と統一できる。

## 実装タスク

### Phase 1: データ収集とプロンプト

- [ ] 1-1. focus プレビューの収集ヘルパーを実装
  - 各プロジェクトの current_focus.md を先頭 500 文字だけ読み込む
  - FileEncodingService.ReadFileAsync を利用
  - ファイルが存在しない場合は "(no focus file)" を返す
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 1-2. プロジェクトシグナルの収集ヘルパーを実装
  - 各プロジェクトから以下を構造化:
    - FocusAge、SummaryAge
    - 未コミットリポ数
    - tensions.md の存在 + 項目数 (ファイル行数で概算)
    - 直近の decision_log 日付
    - アクティブな Workstream 数
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 1-3. System Prompt の設計
  - 定数として DashboardPage.xaml.cs に定義
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 1-4. User Prompt の組み立てロジック
  - プロジェクトごとのデータブロック + Today Queue タスク一覧
  - トークン予算内に収まるよう、プロジェクト情報を段階的にトリム
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 2: LLM 呼び出しと解析

- [ ] 2-1. OnWhatsNextClickAsync イベントハンドラを実装
  - ローディング表示 → データ収集 → プロンプト構築 → LLM 呼び出し → ダイアログ表示
  - CancellationToken 対応 (ダイアログ閉じたらキャンセル)
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 2-2. LLM レスポンスの解析
  - JSON 配列形式で受け取り、各提案を構造化
  - 各提案: project_name, action, reason, target_file (任意), priority
  - JSON パースに失敗した場合はプレーンテキストとしてフォールバック表示
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 3: UI - ダイアログ

- [ ] 3-1. 結果ダイアログの実装
  - ShowUncommittedDetailsDialogAsync を参考にカスタム Window を構築
  - 各提案を番号付きで表示 (プロジェクト名ハイライト、アクション、理由)
  - ダークモード対応 (AppSurface0/1/2、AppText、AppBlue 等のテーマリソース)
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 3-2. [Open] ボタンの実装
  - 各提案の横に [Open] ボタンを配置
  - クリック時: ダイアログを閉じて該当プロジェクトの Editor に遷移
  - target_file が指定されている場合: NavigateToEditorAndOpenFile で直接ファイルを開く
  - target_file がない場合: NavigateToEditor でプロジェクトを選択状態にする
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 3-3. [Copy] ボタンの実装
  - 全提案をプレーンテキストとしてクリップボードにコピー
  - standup やチャットに貼り付けるユースケース
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 3-4. ローディングダイアログの実装
  - LLM 呼び出し中にスピナー付きのモーダルを表示
  - [Cancel] ボタンで CancellationTokenSource をキャンセル
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 4: XAML ボタン追加

- [ ] 4-1. Dashboard ヘッダーにボタンを追加
  - wpf-ui の `<ui:Button>` でツールバーに配置
  - SymbolIcon: `Sparkle24` or `Lightbulb24`
  - Visibility: `IsAiEnabled` バインディング
  - Click: `OnWhatsNextClickAsync`
  - ファイル: `Views/Pages/DashboardPage.xaml`

- [ ] 4-2. IsAiEnabled の DashboardViewModel への追加
  - AiEnabledChangedMessage を受信して更新 (EditorViewModel と同パターン)
  - 初期値は ConfigService から取得
  - ファイル: `ViewModels/DashboardViewModel.cs`

### Phase 5: エラーハンドリング

- [ ] 5-1. API キー未設定時のガイダンス
  - "AI features are enabled but API key is not configured. Go to Settings?" ダイアログ
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 5-2. LLM API エラー時の表示
  - タイムアウト、認証エラー → エラーダイアログ
  - レスポンス解析失敗 → プレーンテキストとしてフォールバック表示
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 5-3. プロジェクトゼロ件の処理
  - プロジェクトが1件もない場合 → LLM を呼ばず "No projects found" メッセージ
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

## プロンプト設計

### System Prompt

```
You are a productivity assistant for a professional managing multiple parallel projects.
Your job is to suggest the 3-5 most impactful actions they should take right now.

## Output rules
- Return a JSON array of 3-5 suggestions, ordered by priority (highest first).
- Each suggestion object:
  {
    "project": "exact project name",
    "action": "concise action (imperative, max 10 words)",
    "reason": "why this matters now (1-2 sentences)",
    "target_file": "relative file path if applicable, or null",
    "category": "task|focus|decision|commit|tension|review"
  }
- Output ONLY the JSON array. No explanation, no markdown fences.

## Priority rules (in order)
1. Overdue tasks - any task past its due date is highest priority
2. Today-due tasks - tasks due today
3. Stale focus - current_focus.md not updated in 7+ days with recent task activity
4. Uncommitted changes - repos with changes that should be committed
5. Unrecorded decisions - focus file mentions conclusions/choices without matching decision log
6. Unresolved tensions - open items in tensions.md
7. Upcoming tasks (1-2 days) - tasks due soon

## Category mapping
- "task": Complete an overdue or due-today Asana task
- "focus": Update current_focus.md (stale or missing)
- "decision": Record a decision in decision_log
- "commit": Commit or review uncommitted changes
- "tension": Address an item in tensions.md
- "review": Review or update project_summary.md

## Tone
- Action-oriented, concise
- Focus on outcomes ("Ship the migration script") not process ("Review the task list")
- Include specific task/file names when available
```

### User Prompt 構造

```
## Date
{today: YYYY-MM-DD (Day of week)}

## Today Queue (sorted by priority)
{For each task, max 30:}
- [{ProjectShortName}] {DisplayMainTitle} ({DueLabel})

## Project Signals

### {ProjectName}
- Focus age: {N} days {(stale) if > 7}
- Uncommitted repos: {count}
- Open tensions: {yes/no}
- Recent decisions: {latest date or "none"}
- Focus preview: {first 500 chars of current_focus.md}

{Repeat for each project}

## Instruction
Suggest 3-5 highest-priority actions based on the data above.
Return JSON array only.
```

## ファイル追加/変更一覧

### 新規ファイル

なし。

### 変更ファイル

| ファイル | 変更内容 |
|---|---|
| `Views/Pages/DashboardPage.xaml` | ヘッダーに What's Next ボタン追加 |
| `Views/Pages/DashboardPage.xaml.cs` | OnWhatsNextClickAsync、データ収集、プロンプト構築、ダイアログ実装 |
| `ViewModels/DashboardViewModel.cs` | IsAiEnabled プロパティ + AiEnabledChangedMessage 受信 |

## 実装順序

Phase 1 (データ+プロンプト) → Phase 2 (LLM呼び出し) → Phase 3 (ダイアログ) → Phase 4 (XAML) → Phase 5 (エラー処理)

Phase 1-2 が裏側のロジック、Phase 3-4 が見た目。
全体で新規ファイルゼロ、変更ファイル3つの軽量実装。

## 他の計画との関係

| 観点 | What's Next | AI Decision Log | Smart Standup |
|---|---|---|---|
| LlmClientService | 共通利用 | 共通利用 | 共通利用 |
| ProjectDiscoveryService | 全プロジェクト走査 | 単一プロジェクト | 全プロジェクト走査 |
| TodayQueueService | Today Queue 全件 | 不使用 | Today Queue 全件 |
| 入力 | 自動 (ボタン1つ) | ユーザー入力 | 自動 (スケジューラ) |
| 出力 | ダイアログ表示 (一時的) | ファイル保存 | ファイル保存 |
| Refine | なし (一発生成) | あり (反復修正) | なし (一発生成) |
| 新規ファイル | 0 | 2 | 1 |

3機能は独立して実装可能。最も軽量なのは What's Next (新規ファイル0、変更3つ)。
