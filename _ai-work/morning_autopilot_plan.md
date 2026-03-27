# Morning Autopilot - 今日の作戦を自動生成

朝アプリを開いた瞬間に「今日一日の作戦」を時間ブロック付きで提案する機能。
What's Nextが「何をやるか」のリストなら、Morning Autopilotは「いつ・何を・なぜ」の時間割を生成する。

## 解決する課題

- 朝一で「今日は何から始めるか」を考える判断コスト (毎朝10-15分)
- 複数プロジェクトの優先度をその場の感覚で決めてしまう問題
- 期限が近いタスクやメンテナンス作業 (focus更新、commit等) の見落とし
- 時間配分の偏り (気がつくと1プロジェクトだけに時間を使っている)

## What's Next との違い

| 観点 | What's Next | Morning Autopilot |
|---|---|---|
| 出力 | 優先タスク3-5件のリスト | 時間ブロック付きの1日計画 |
| 時間軸 | なし (優先度順のみ) | 午前/午後/夕方の時間帯に配分 |
| 粒度 | 「これをやれ」 | 「この順で、この時間帯にやれ」 |
| 起動 | ボタン手動 | 朝の初回起動で自動提案 + ボタン手動 |
| 理由 | 1-2文 | 「なぜ今日」「なぜこの時間帯」を含む |
| ファイル保存 | なし (一時表示) | Markdown保存 (振り返り用、任意) |
| Today Queue連携 | 読み取りのみ | 承認した項目をToday Queueにスター付与 |

## フロー全体像

```
[アプリ起動 / Dashboardボタン]
        │
        ▼
┌─ 自動起動判定 ─────────────────────────────────────┐
│  条件: 今日初回起動 && AI有効 && 6時以降 && 平日     │
│  (ボタン押下時はこの判定をスキップ)                   │
└──────────────────────────────────────────────────────┘
        │
        ▼
┌─ データ収集 ───────────────────────────────────────┐
│  1. Today Queue全タスク (TodayQueueService)         │
│  2. 全プロジェクトのシグナル (ProjectDiscoveryService)│
│  3. 各プロジェクトのfocusプレビュー (先頭500文字)    │
│  4. 昨日のstandup (あれば: 何をやったかの文脈)       │
│  5. 今日のスケジュールヒント (ユーザー入力、任意)     │
└──────────────────────────────────────────────────────┘
        │
        ▼
┌─ ローディングダイアログ ──────────────────────────┐
│  ⟳ Planning your day...             [Cancel]       │
└──────────────────────────────────────────────────────┘
        │
        ▼ LLM 1回 (時間ブロック付き計画を生成)
        │
        ▼
┌─ 計画表示ダイアログ ─────────────────────────────────────┐
│  ☀ Today's Plan — 2026-03-26 (Thu)                 [×]  │
├──────────────────────────────────────────────────────────┤
│  Schedule hint: 14時にProject Bの会議                    │
│                                                          │
│  🌅 Morning  (deep work)                                 │
│  ┌────────────────────────────────────────────────────┐  │
│  │ 1. [ProjectAlpha] Finalize auth decision           │  │
│  │    Tension open for 5 days. No meeting blockers    │  │
│  │    in the morning — best time for deep thinking.   │  │
│  │                                       [Open ▶]    │  │
│  ├────────────────────────────────────────────────────┤  │
│  │ 2. [ProjectAlpha] Commit staged changes            │  │
│  │    3 files uncommitted since Tuesday.              │  │
│  │                                       [Open ▶]    │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  🌤 Afternoon                                            │
│  ┌────────────────────────────────────────────────────┐  │
│  │ 3. [ProjectBeta] Prep for 14:00 meeting            │  │
│  │    Review focus and recent decisions before         │  │
│  │    the meeting. Update focus afterward.             │  │
│  │                                       [Open ▶]    │  │
│  ├────────────────────────────────────────────────────┤  │
│  │ 4. [ProjectGamma] Update current_focus.md          │  │
│  │    Last updated 9 days ago. Asana shows 4 tasks    │  │
│  │    completed since then.                           │  │
│  │                                       [Open ▶]    │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│  🌆 Late afternoon  (wrap-up)                            │
│  ┌────────────────────────────────────────────────────┐  │
│  │ 5. [ProjectBeta] Record meeting decisions          │  │
│  │    Capture outcomes from 14:00 meeting into        │  │
│  │    decision_log before end of day.                 │  │
│  │                                       [Open ▶]    │  │
│  └────────────────────────────────────────────────────┘  │
│                                                          │
│                          [Copy]  [Save & Close]  [Close] │
└──────────────────────────────────────────────────────────┘
```

## アーキテクチャ

```
DashboardPage.xaml
  ├── [Morning Autopilot] ボタン (Click)
  └── App起動時の自動トリガー (DashboardPage.Loaded)
        │
        ▼
DashboardPage.xaml.cs
  └── OnMorningAutopilotClickAsync()
        │
        ├── 自動起動判定 (IsFirstLaunchToday + IsAiEnabled + 時間帯)
        ├── データ収集:
        │     ├── ViewModel.Projects (ProjectDiscoveryService経由)
        │     ├── ViewModel.GetTopTasksForAi(30) (TodayQueueService経由)
        │     ├── CollectFocusPreviewsAsync() (既存メソッド流用)
        │     └── ReadYesterdayStandupAsync() (新規)
        │
        ├── スケジュールヒント入力 (任意、省略可能)
        │
        ├── プロンプト構築 + LlmClientService.ChatCompletionAsync()
        │
        ├── レスポンス解析 (JSON → DailyPlan)
        │
        └── 計画表示ダイアログ
              ├── [Open] → Editor / Git Repos に遷移
              ├── [Copy] → クリップボード
              └── [Save & Close] → Markdown ファイル保存
                    {ObsidianVaultRoot}/standup/{date}_plan.md
```

ViewModel にコマンドを追加するのではなく、What's Next と同じくコードビハインドで直接処理する。
理由: ダイアログ表示と MainWindow への遷移はビューの責務。既存のパターンと統一。

## データソース

LLM に渡す入力データ:

| データ | ソース | 計画への活用 |
|---|---|---|
| Today Queue 全タスク | TodayQueueService | 期限ベースの優先度と時間配分 |
| FocusAge (全プロジェクト) | ProjectInfo | 「focus更新」アクションの生成 |
| current_focus.md 要約 | ProjectInfo.FocusFile | 各プロジェクトの現在の文脈 |
| 未コミット変更 | ProjectInfo.UncommittedRepoPaths | 「コミット」アクションの生成 |
| tensions.md 有無+項目数 | ファイル存在チェック | 「tension解消」の優先度判定 |
| decision_log 直近日付 | ProjectInfo.DecisionLogDates | 「決定記録」の必要性判定 |
| 昨日のstandup | standup/{date}_standup.md | 継続作業の文脈 (あれば) |
| スケジュールヒント | ユーザー入力 (任意) | 会議前後の計画調整 |
| 曜日 | DateTime.Now.DayOfWeek | 週初め=計画、週末=振り返り傾向 |

### トークン予算の管理

What's Next と同じ方式:
- current_focus.md は先頭 500 文字のみ
- tensions.md は項目数のみ (全文不要)
- Today Queue は上位 30 件に制限
- 昨日のstandup は先頭 1000 文字のみ (あれば)
- プロジェクトメタデータは数値で軽量

## データモデル

DashboardPage.xaml.cs 内にネストクラスとして定義 (What's Next の WhatsNextSuggestion と同パターン):

```csharp
private class DailyPlan
{
    public List<TimeBlock> Blocks { get; set; } = [];
    public string? OverallAdvice { get; set; }     // 全体を通じたアドバイス (任意)
}

private class TimeBlock
{
    public string Period { get; set; } = "";        // "morning" | "afternoon" | "late_afternoon"
    public List<PlanItem> Items { get; set; } = [];
}

private class PlanItem
{
    public string Project { get; set; } = "";       // プロジェクト名
    public string Action { get; set; } = "";        // 具体的アクション (命令形、10語以内)
    public string Reason { get; set; } = "";        // なぜ今日・なぜこの時間帯 (1-2文)
    public string? TargetFile { get; set; }         // 対象ファイルの相対パス (任意)
    public string Category { get; set; } = "";      // task|focus|decision|commit|tension|meeting_prep|review
}
```

## 自動起動の判定ロジック

```csharp
private bool ShouldShowMorningAutopilot()
{
    // AI無効なら出さない
    if (!ViewModel.IsAiEnabled) return false;

    // 6時前なら出さない
    if (DateTime.Now.Hour < 6) return false;

    // 今日既に表示済みなら出さない (static フラグ)
    if (_morningAutopilotShownToday == DateTime.Today) return false;

    // 土日は出さない (設定で変更可能にする余地あり)
    var dow = DateTime.Now.DayOfWeek;
    if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday) return false;

    return true;
}
```

- `_morningAutopilotShownToday` は `static DateTime?` で管理 (アプリ再起動まで保持)
- Dashboard の `Loaded` イベントで判定し、条件を満たせば確認ダイアログ:
  "Good morning! Generate today's plan?" [Yes] [Not today]
- [Not today] でフラグを立てて当日は再表示しない
- ボタン押下時はこの判定をスキップして常に実行

## スケジュールヒント入力

計画精度を上げるための任意入力 (省略可能):

```
┌─────────────────────────────────────────────────────┐
│  ☀ Morning Autopilot                          [×]   │
├─────────────────────────────────────────────────────┤
│                                                      │
│  Any meetings or time constraints today?             │
│  (optional — press Plan to skip)                     │
│                                                      │
│  ┌────────────────────────────────────────────────┐ │
│  │ 14:00 Project Beta meeting                     │ │
│  │ 16:00 out of office                            │ │
│  └────────────────────────────────────────────────┘ │
│                                                      │
│                              [Cancel]  [Plan ▶]     │
└─────────────────────────────────────────────────────┘
```

- TextBox は空でもOK (空ならスケジュールヒントなしで計画生成)
- 将来的にカレンダー連携を入れる場合はここに自動挿入できる

## LLM プロンプト設計

### System Prompt

```
You are a daily planning assistant for a professional managing multiple parallel projects.
Your job is to create a time-blocked plan for today that maximizes productivity and prevents things from falling through the cracks.

## Output rules
- Return a JSON object with this structure:
  {
    "blocks": [
      {
        "period": "morning" | "afternoon" | "late_afternoon",
        "items": [
          {
            "project": "exact project name",
            "action": "concise action (imperative, max 10 words)",
            "reason": "why today AND why this time slot (1-2 sentences)",
            "target_file": "relative file path if applicable, or null",
            "category": "task|focus|decision|commit|tension|meeting_prep|review"
          }
        ]
      }
    ],
    "overall_advice": "one sentence of overall advice for the day, or null"
  }
- Output ONLY the JSON object. No explanation, no markdown fences.
- Generate 4-7 items total across all time blocks.
- Every time block must have at least 1 item.

## Time block principles
- morning: Deep work, critical decisions, creative tasks. Brain is freshest.
- afternoon: Meetings, collaborative work, reviews. Lower cognitive demand tasks.
- late_afternoon: Wrap-up tasks — commits, focus updates, quick maintenance. Closing the loop.

## Schedule hint handling
- If the user provides meeting times, plan around them:
  - Place meeting prep BEFORE the meeting time
  - Place meeting follow-up (notes, decisions) AFTER
  - Protect deep work blocks from meeting interruption
- If no schedule hint, assume a standard workday.

## Planning rules (in priority order)
1. Overdue tasks — must be in morning block
2. Today-due tasks — morning or early afternoon
3. Meeting prep/follow-up — anchored to meeting times
4. Stale focus (7+ days) — afternoon or late_afternoon
5. Uncommitted changes — late_afternoon (end-of-day cleanup)
6. Unresolved tensions (5+ days old) — morning (needs thinking)
7. Upcoming tasks (1-2 days) — afternoon if time permits
8. Decision recording — late_afternoon

## Day-of-week awareness
- Monday: Include a "review weekly priorities" item if not already planned
- Friday: Include a "review and close the week" item (commit, update focus, resolve quick tensions)

## Anti-patterns to avoid
- Do not schedule more than 3 projects in morning (focus fragmentation)
- Do not put deep thinking tasks after 15:00
- Do not ignore overdue items regardless of other priorities

## Category mapping
- "task": Complete an Asana task
- "focus": Update current_focus.md
- "decision": Record a decision in decision_log
- "commit": Commit or review uncommitted changes
- "tension": Address an item in tensions.md
- "meeting_prep": Prepare for an upcoming meeting
- "review": Review status, summary, or weekly priorities

## Tone
- Action-oriented: "Ship the migration script" not "Consider reviewing the task list"
- Time-aware: explain WHY this time slot specifically
- Encouraging but realistic: acknowledge heavy days, suggest what to defer if overloaded
```

### User Prompt 構造

```
## Date
{today: YYYY-MM-DD (Day of week)}

## Schedule hints
{ユーザー入力のスケジュールヒント、または "(none)"}

## Yesterday's standup (for continuity)
{昨日のstandupファイル先頭1000文字、または "(no standup available)"}

## Today Queue (sorted by priority)
{For each task, max 30:}
- [{ProjectShortName}] {DisplayMainTitle} ({DueLabel})

## Project Signals

### {ProjectName}
- Focus age: {N} days {(stale) if > 7}
- Uncommitted repos: {count}
- Open tensions: {yes/no} ({count} items)
- Recent decisions: {latest date or "none"}
- Active workstreams: {count}
- Focus preview: {first 500 chars of current_focus.md}

{Repeat for each project}

## Instruction
Create a time-blocked daily plan based on the data above.
Return JSON object only.
```

## ファイル保存 (任意)

[Save & Close] ボタン押下時に Markdown ファイルとして保存:

### 保存先

`{ObsidianVaultRoot}/standup/{date}_plan.md`

standupファイルと同じディレクトリに配置 (standup は `_standup.md`、plan は `_plan.md` の接尾辞で区別)。

### 保存フォーマット

```markdown
# Daily Plan — 2026-03-26 (Thu)

Schedule: 14:00 Project Beta meeting

## Morning (deep work)

1. [ProjectAlpha] Finalize auth decision
   - Tension open for 5 days. No meeting blockers in the morning.
   - -> decision_log

2. [ProjectAlpha] Commit staged changes
   - 3 files uncommitted since Tuesday.

## Afternoon

3. [ProjectBeta] Prep for 14:00 meeting
   - Review focus and recent decisions before the meeting.

4. [ProjectGamma] Update current_focus.md
   - Last updated 9 days ago. 4 tasks completed since then.
   - -> current_focus.md

## Late afternoon (wrap-up)

5. [ProjectBeta] Record meeting decisions
   - Capture outcomes from 14:00 meeting into decision_log.
   - -> decision_log
```

## UI 設計

### ボタン配置

Dashboard ヘッダーのツールバー、What's Next ボタンの左隣に追加:

```
[Dashboard]    [☀ Plan My Day]  [✨ What's Next]  [▼ 10 min] [↻] [👁 2]
```

- アイコン: `WeatherSunny24` (wpf-ui SymbolRegular)
- ToolTip: "Plan my day"
- Visibility: `IsAiEnabled` バインディング (BooleanToVisibilityConverter)

### 確認プロンプト (自動起動時のみ)

```
┌──────────────────────────────────────────────┐
│  ☀ Good morning!                       [×]   │
├──────────────────────────────────────────────┤
│                                               │
│  Generate today's plan?                       │
│                                               │
│              [Not today]  [Let's go ▶]       │
└──────────────────────────────────────────────┘
```

### 計画表示ダイアログ

- サイズ: 680 x 520, CanResize, WindowChrome適用
- スタイル: What's Next 結果ダイアログと統一感のあるデザイン
- 時間ブロックはセクション分けで背景色を微妙に変える:
  - Morning: 通常背景
  - Afternoon: AppSurface1
  - Late afternoon: AppSurface0
- overall_advice がある場合はフッター上部にイタリック表示
- 各アイテムのカテゴリに応じたアイコン:
  - task: `TaskListSquare24`
  - focus: `DocumentEdit24`
  - decision: `Gavel24`
  - commit: `BranchFork24`
  - tension: `Warning24`
  - meeting_prep: `People24`
  - review: `Eye24`

### ボタン

- [Open] (各アイテム): プロジェクトの Editor / Git Repos に遷移。target_file があればファイルを直接開く
- [Copy]: 全計画をプレーンテキスト (Markdown形式) でクリップボードにコピー。チャット/メールに貼れる
- [Save & Close]: Markdown ファイルに保存してダイアログを閉じる
- [Close]: 保存せずに閉じる

## 実装タスク

### Phase 1: データ収集

- [ ] 1-1. 昨日のstandup読み込みヘルパーを実装
  - standup ディレクトリから昨日の `{date}_standup.md` を検索
  - 先頭 1000 文字を返す (不在なら空文字)
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 1-2. tensions.md の項目数カウントヘルパーを実装
  - ファイルを読み、`- ` で始まる行数をカウント (簡易)
  - What's Next の CollectFocusPreviewsAsync と並行で呼べる設計
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 1-3. CollectProjectSignalsAsync ヘルパーを実装
  - 各プロジェクトから: FocusAge, 未コミット数, tension項目数, 最新decision_log日付, workstream数
  - 構造化データとして返す (What's Next の BuildWhatsNextUserPrompt 内のロジックを分離)
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 2: スケジュールヒント入力ダイアログ

- [ ] 2-1. ヒント入力ダイアログの実装
  - Window (500 x SizeToContent, NoResize)
  - TextBox (MultiLine, AcceptsReturn=True, Height=80)
  - [Cancel] / [Plan] ボタン (Plan は空入力でもOK)
  - 自動起動時は「Good morning!」ヘッダー付き
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 3: プロンプト構築と LLM 呼び出し

- [ ] 3-1. System Prompt を定数として定義
  - 上記プロンプト設計に基づく
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 3-2. User Prompt の組み立てロジック (BuildMorningAutopilotUserPrompt)
  - 日付 + 曜日 + スケジュールヒント + 昨日standup + Today Queue + Project Signals
  - What's Next の BuildWhatsNextUserPrompt を参考にしつつ、追加データを含む
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 3-3. OnMorningAutopilotClickAsync イベントハンドラを実装
  - ヒント入力ダイアログ → データ収集 → プロンプト構築 → ローディング → LLM → 計画ダイアログ
  - CancellationToken 対応
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 3-4. LLM レスポンスの解析 (ParseDailyPlanResponse)
  - JSON オブジェクトをパース → DailyPlan
  - blocks 配列 → TimeBlock → PlanItem
  - パース失敗時はプレーンテキストとしてフォールバック表示
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 4: 計画表示ダイアログ

- [ ] 4-1. 計画ダイアログの実装
  - 680 x 520, CanResize, WindowChrome
  - 時間ブロックごとのセクション表示 (Morning / Afternoon / Late afternoon)
  - 各アイテム: カテゴリアイコン + [Project] Action + Reason + [Open]
  - ダークモード対応 (テーマリソース使用)
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 4-2. [Open] ボタンの実装
  - What's Next と同じナビゲーションロジック
  - target_file あり → NavigateToEditorAndOpenFile
  - target_file なし → NavigateToEditor (プロジェクト選択状態)
  - category が "commit" → NavigateToGitRepos
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 4-3. [Copy] ボタンの実装
  - DailyPlan を Markdown形式のプレーンテキストに変換
  - ヘッダー + 時間ブロック + 番号付きアイテム
  - Clipboard.SetText で設定
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 4-4. [Save & Close] ボタンの実装
  - Markdown ファイルを `{ObsidianVaultRoot}/standup/{date}_plan.md` に保存
  - FileEncodingService.WriteFileAsync (UTF-8)
  - 保存成功後にダイアログを閉じる
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 5: XAML ボタン + 自動起動

- [ ] 5-1. Dashboard ヘッダーにボタンを追加
  - wpf-ui の `<ui:Button>` で配置 (What's Next ボタンの左隣)
  - SymbolIcon: `WeatherSunny24`
  - Content: "Plan My Day"
  - Visibility: `IsAiEnabled` バインディング
  - Click: `OnMorningAutopilotClickAsync`
  - ファイル: `Views/Pages/DashboardPage.xaml`

- [ ] 5-2. 自動起動判定ロジックの実装
  - ShouldShowMorningAutopilot() メソッド
  - static DateTime? _morningAutopilotShownToday フラグ
  - DashboardPage Loaded イベントから呼び出し
  - 条件合致時: 確認プロンプト表示 → [Let's go] で実行 / [Not today] でスキップ
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 5-3. ローディングダイアログの実装
  - What's Next の BuildWhatsNextLoadingWindow と同パターン
  - テキスト: "Planning your day..."
  - [Cancel] で CancellationTokenSource をキャンセル
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

### Phase 6: エラーハンドリング

- [ ] 6-1. API キー未設定時のガイダンス
  - "AI features are enabled but API key is not configured. Go to Settings?" ダイアログ
  - What's Next と同じパターン
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 6-2. LLM API エラー時の表示
  - タイムアウト、認証エラー → エラーダイアログ
  - レスポンス解析失敗 → プレーンテキストとしてフォールバック表示
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 6-3. プロジェクトゼロ件の処理
  - LLM を呼ばず "No projects found" メッセージ
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

- [ ] 6-4. standup ディレクトリ不在時の処理
  - ObsidianVaultRoot 未設定 or standup/ 不在 → Save 機能を無効化 (ボタン非表示)
  - 計画生成自体は問題なく動作
  - ファイル: `Views/Pages/DashboardPage.xaml.cs`

## ファイル追加/変更一覧

### 新規ファイル

なし。

### 変更ファイル

| ファイル | 変更内容 |
|---|---|
| `Views/Pages/DashboardPage.xaml` | ヘッダーに Plan My Day ボタン追加 |
| `Views/Pages/DashboardPage.xaml.cs` | 自動起動判定、ヒント入力ダイアログ、データ収集、プロンプト構築、LLM呼び出し、計画ダイアログ、保存ロジック |
| `ViewModels/DashboardViewModel.cs` | IsAiEnabled プロパティ (What's Nextで追加済みなら変更不要) |

## 実装順序

Phase 1 (データ収集) → Phase 2 (ヒント入力) → Phase 3 (プロンプト+LLM) → Phase 4 (計画ダイアログ) → Phase 5 (XAML+自動起動) → Phase 6 (エラー処理)

Phase 1-3 が裏側のロジック、Phase 4-5 が見た目、Phase 6 が堅牢化。
全体で新規ファイルゼロ、変更ファイル2-3つの軽量実装 (What's Next と同パターン)。

## 他の計画との関係

| 観点 | What's Next | Morning Autopilot | Standup Generator |
|---|---|---|---|
| トリガー | ボタン手動 | 朝の自動起動 + ボタン | タイマー (1時間) |
| LlmClientService | 利用 | 利用 | 不使用 |
| ProjectDiscoveryService | 全プロジェクト走査 | 全プロジェクト走査 | 全プロジェクト走査 |
| TodayQueueService | Today Queue読取 | Today Queue読取 | Today Queue読取 |
| 昨日のstandup | 不使用 | 読み取り (継続性のため) | 生成する側 |
| スケジュールヒント | なし | ユーザー入力 (任意) | なし |
| 出力 | 一時表示 (ダイアログ) | ダイアログ + ファイル保存 (任意) | ファイル保存 |
| 時間軸 | なし (優先度のみ) | 時間ブロック (午前/午後/夕方) | 昨日/今日/今週 |
| 新規ファイル | 0 | 0 | 0 |

What's Next のデータ収集ロジック (CollectFocusPreviewsAsync, BuildWhatsNextUserPrompt 内のプロジェクトシグナル部分) はそのまま流用可能。Morning Autopilot はその上にスケジュールヒントと時間ブロック生成を追加する形。

## 将来の拡張候補

- カレンダー連携 (Outlook/Google Calendar API) でスケジュールヒントを自動取得
- 過去の plan ファイルからの学習 (どのアイテムが実際に完了されたか)
- 「午後の残り」再計画ボタン (午前が予定通りに行かなかった時用)
- Today Queue へのスター付与 (承認した計画アイテムをハイライト)
- ウィジェット表示 (ダイアログではなくDashboard上にインライン表示)
