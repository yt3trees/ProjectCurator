# Smart Time-Block - 実装計画

Dashboard から1クリックで「今日の時間割」をAIが提案する機能。
TodayQueueService の優先度バケットと current_focus.md を入力に、午前/午後の集中ブロックを構造化して表示する。

## 背景・課題

TodayQueueService はタスクを `overdue / today / soon / normal` の4バケットで優先順位付けしているが、「どのタスクを何時ごろやるか」という時間配分の判断はユーザー任せ。
複数プロジェクトのタスクが混在するとき、「何から手をつければいいか」が直感的に分からない。

- What's Next が「次にやること」(アクション提案) なのに対し、本機能は「今日1日の時間割」
- overdue タスクの緊急処理 → 集中作業 → 翌日以降の種まき、という日単位の流れを示す

## 方針

- Dashboard ツールバーに `🕐 Time Block` ボタンを追加 (What's Next の隣)
- `IsAiEnabled` に連動して表示/非表示
- 実装は `DashboardPage.xaml.cs` のコードビハインドに追加 (What's Next と同パターン)
- 新規サービス不要。`TodayQueueService` + `FileEncodingService` + `LlmClientService` を直接利用
- AI 無効時または API 失敗時はボタン押下不可 / エラーメッセージのみ

## 機能仕様

### 入力データ

| データ | ソース | 用途 |
|---|---|---|
| 全タスク (overdue/today/soon) | TodayQueueService.GetAllTasksSorted() | スケジュール対象タスク |
| current_focus.md 先頭200文字 | FileEncodingService + ProjectInfo.CurrentFocusFile | 各プロジェクトの集中ポイント |
| 現在時刻・曜日 | DateTime.Now | 「残り時間」の算出 |
| SortBucket / DueLabel | TodayQueueTask | 優先度情報 |

### 出力フォーマット

LLM には JSON で返させる:

```json
[
  {
    "start": "09:00",
    "end": "10:30",
    "label": "ProjectAlpha - overdue タスク処理",
    "tasks": ["DBマイグレーション確認", "スクリプトレビュー"],
    "project": "ProjectAlpha",
    "note": "期限2日超過。最優先で片付ける。"
  },
  ...
]
```

ダイアログでは時間ブロックを縦に並べたリスト表示 + コピーボタン。

### UI 配置

```
Dashboard ツールバー:
[ ↺ Refresh ] [ 💡 What's Next ] [ 🕐 Time Block ] [ ⚙ Settings ]
                                        ↑ IsAiEnabled=true のとき表示
```

ダイアログ構成 (What's Next 結果ダイアログと同スタイル):

```
┌─ Today's Time Block ─────────────────────────────┐
│ 2026-03-26 (Thursday)  残り実働: 約6時間          │
│                                                   │
│ 09:00 – 10:30  ProjectAlpha                       │
│   overdue 対応: DBマイグレーション確認             │
│   期限2日超過。最優先で片付ける。                  │
│                                                   │
│ 10:30 – 12:00  ProjectBeta                        │
│   今日締切: UIレビュー準備                         │
│   午後のレビューまでに完了すること。               │
│                                                   │
│ 13:00 – 15:00  ProjectAlpha                       │
│   集中作業: 認証モジュールのテスト作成             │
│                                                   │
│ 15:00 – 16:00  ProjectGamma                       │
│   フォローアップ: 仕様確認メール返信               │
│                                                   │
│ [ Copy ]  [ View Debug ]              [ Close ]   │
└───────────────────────────────────────────────────┘
```

## アーキテクチャ

```
DashboardPage.xaml.cs (既存: 拡張)
  ├── OnTimeBlockClickAsync()       新規: ボタンクリックハンドラ
  ├── CollectTimeBlockDataAsync()   新規: タスク + focus 収集
  ├── BuildTimeBlockUserPrompt()    新規: ユーザープロンプト組み立て
  ├── ParseTimeBlockResponse()      新規: JSON パース
  ├── BuildTimeBlockLoadingWindow() 新規: ローディングダイアログ
  └── ShowTimeBlockResultDialog()   新規: 結果ダイアログ表示
        │
        ▼
  LlmClientService.ChatCompletionAsync()  既存: そのまま利用
  TodayQueueService.GetAllTasksSorted()   既存: そのまま利用
  FileEncodingService.ReadFile()          既存: そのまま利用
```

DashboardPage.xaml への変更:
- ツールバーに `Button x:Name="TimeBlockButton"` を追加 (What's Next の隣)
- `Visibility="{Binding ViewModel.IsAiEnabled, Converter=...}"` でゲート

## プロンプト設計

### System Prompt

```
You are a daily time-block planner for a developer managing multiple parallel projects.
Given today's task queue and project focus notes, create a realistic schedule for the day.

Output rules:
- Output ONLY a JSON array of time blocks. No preamble, no explanation.
- Each block: { "start": "HH:MM", "end": "HH:MM", "label": "...", "tasks": [...], "project": "...", "note": "..." }
- Use 24-hour format. Assume work hours 09:00-18:00 with a 12:00-13:00 lunch break.
- Schedule overdue tasks first, then today-due tasks, then soon tasks.
- Group tasks from the same project when possible to minimize context switches.
- Each block is 30-120 minutes. Do not schedule more than 3 blocks per project.
- Keep note to 1 sentence. Focus on why this block matters today.
- If current time is past 09:00, start scheduling from the next 30-minute boundary.
- Omit tasks with no due date unless there is spare time.
```

### User Prompt 構造

```
## Current Time
{HH:MM} ({DayOfWeek})

## Today's Task Queue

### Overdue
{task list: "- [ProjectName] TaskTitle (Overdue Nd)"}

### Due Today
{task list: "- [ProjectName] TaskTitle"}

### Due Soon (within 2 days)
{task list: "- [ProjectName] TaskTitle (In Nd)"}

## Project Focus Notes
### {ProjectName}
{current_focus.md 先頭200文字 or "(no focus file)"}

## Instruction
Create a time-blocked schedule for today.
Prioritize overdue tasks. Minimize context switches between projects.
```

## 実装タスク

### Phase 1: XAML ボタン追加

- [ ] 1-1. `DashboardPage.xaml` のツールバーに `TimeBlockButton` を追加
  - What's Next ボタンの隣 (右側)
  - `Content="🕐 Time Block"`, `IsEnabled="{Binding ViewModel.IsAiEnabled}"`
  - Click イベント: `OnTimeBlockClickAsync`
  - ファイル: `Views/Pages/DashboardPage.xaml`

### Phase 2: データ収集

- [ ] 2-1. `CollectTimeBlockDataAsync()` を実装
  - `TodayQueueService.GetAllTasksSorted()` で全タスク取得 (limit: 50)
  - overdue / today / soon (bucket 0/1/2) のみ抽出 (bucket 4以上は除外)
  - 各プロジェクトの `current_focus.md` を `FileEncodingService` で読み込み、先頭200文字を取得
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 2-2. タスクが0件の場合の処理
  - overdue/today/soon タスクが0件なら「今日のタスクがありません。Asana Sync を実行してください。」のメッセージボックスを表示して終了
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 3: プロンプト組み立て

- [ ] 3-1. `BuildTimeBlockUserPrompt()` を実装
  - 現在時刻・曜日のセクション
  - タスクをバケット別 (Overdue / Due Today / Due Soon) にリスト化
  - プロジェクトごとの focus プレビューを追加
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 3-2. `TimeBlockSystemPrompt` 定数を定義
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 4: LLM 呼び出し + レスポンスパース

- [ ] 4-1. `OnTimeBlockClickAsync()` を実装
  - ローディングウィンドウ (キャンセルボタン付き) を表示
  - `CollectTimeBlockDataAsync()` → `BuildTimeBlockUserPrompt()` → `LlmClientService.ChatCompletionAsync()`
  - 成功: `ParseTimeBlockResponse()` → `ShowTimeBlockResultDialog()`
  - 失敗: エラーメッセージボックス
  - キャンセル: サイレント終了
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 4-2. `ParseTimeBlockResponse()` を実装
  - レスポンスから JSON 配列を抽出 (コードブロック除去)
  - `System.Text.Json` で `List<TimeBlockItem>` にデシリアライズ
  - パース失敗時は raw テキストをそのまま返すフォールバック
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 4-3. `TimeBlockItem` シールドクラスを定義
  - `Start`, `End`, `Label`, `Tasks`, `Project`, `Note` プロパティ
  - ファイル: `Views/Pages/DashboardPage.xaml.cs` 内に private sealed class として定義

### Phase 5: 結果ダイアログ

- [ ] 5-1. `BuildTimeBlockLoadingWindow()` を実装
  - What's Next の `BuildWhatsNextLoadingWindow()` と同スタイル
  - タイトル: "Generating time blocks..."
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 5-2. `ShowTimeBlockResultDialog()` を実装
  - タイムブロックを縦リストで表示
  - 各ブロック: 時間帯 (太字) + プロジェクト名 + タスクリスト + note
  - `Copy` ボタン: Markdown形式でクリップボードにコピー
  - `View Debug` ボタン: 送信プロンプト + raw レスポンスを表示
  - `Close` ボタン
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 5-3. クリップボードコピー用 Markdown の生成
  - 形式: `## Today's Schedule\n### 09:00-10:30 ProjectAlpha\n- タスク1\n...`
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 6: 動作確認

- [ ] 6-1. overdue タスクが先頭に来ることを確認
- [ ] 6-2. タスクが0件のとき適切なメッセージが出ることを確認
- [ ] 6-3. キャンセルボタンが機能することを確認
- [ ] 6-4. AI 無効時にボタンが非表示になることを確認
- [ ] 6-5. `dotnet publish -p:PublishProfile=SingleFile` でビルド成功を確認

## ファイル変更一覧

### 変更ファイル

| ファイル | 変更内容 |
|---|---|
| `Views/Pages/DashboardPage.xaml` | ツールバーに TimeBlock ボタン追加 |
| `Views/Pages/DashboardPage.xaml.cs` | OnTimeBlockClickAsync 他5メソッド追加、TimeBlockItem クラス追加 |

### 新規ファイルなし

既存の What's Next / Briefing パターンをそのまま踏襲するため、新規サービス・モデルファイルは不要。

## What's Next との比較

| 観点 | What's Next | Smart Time-Block |
|---|---|---|
| 問い | 「次に何をすべきか」 | 「今日何時に何をするか」 |
| 出力粒度 | 3-5件のアクション提案 | 1日分の時間割 (30-120分ブロック) |
| 入力 | 全プロジェクトのfocus/tasks/signals | overdue+today+soonタスク + focus先頭 |
| 出力形式 | テキスト番号リスト | JSON → 構造化タイムライン表示 |
| トリガー | 手動のみ | 手動のみ |
| 実装場所 | DashboardPage.xaml.cs | DashboardPage.xaml.cs (同パターン) |

## 実装順序

Phase 1 (ボタン追加) → Phase 2-3 (データ収集・プロンプト) → Phase 4 (LLM 呼び出し) → Phase 5 (ダイアログ) → Phase 6 (確認)

Phase 1-4 で最小動作版が完成。Phase 5 の UI 品質を後から磨く。
