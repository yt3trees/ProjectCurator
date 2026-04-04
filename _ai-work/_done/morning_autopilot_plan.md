# Time-Block → Plan My Day 改修計画

既存の「Smart Time-Block」機能 (Clock24 ボタン / OnTimeBlockClickAsync) を Morning Autopilot 相当に昇格させる改修。
新規ファイルゼロ、変更ファイル 2 つ (DashboardPage.xaml + DashboardPage.xaml.cs)。

## 画面イメージ

### (0) ツールバーボタン

```
[Dashboard]                   [☀ Plan My Day]  [✨ What's Next]  [Auto: 10 min v]  [↻]  [👁]
```

### (1) 自動起動確認ダイアログ (ShowMorningConfirmDialogAsync)

朝の初回起動時のみ表示。幅 360px、SizeToContent。

```
+----------------------------------------------+
| ☀  Plan My Day                               |
+----------------------------------------------+
|                                              |
|   Good morning!                              |
|   Generate today's plan?                     |
|                                              |
|                  [Not today]  [Let's go  >]  |
+----------------------------------------------+
```

### (2) スケジュールヒント入力ダイアログ (ShowScheduleHintDialogAsync)

手動ボタン押下時・[Let's go] 後どちらも表示。幅 500px、SizeToContent。

```
+-----------------------------------------------------------+
| ☀  Plan My Day                                       [x] |
+-----------------------------------------------------------+
|                                                           |
|   Any meetings or time constraints today?                 |
|   (optional — press Plan to skip)                         |
|                                                           |
|   +-----------------------------------------------------+ |
|   | 14:00 ProjectBeta meeting                           | |
|   | 16:30 out of office                                 | |
|   |                                                     | |
|   +-----------------------------------------------------+ |
|                                                           |
|                               [Cancel]  [Plan My Day  >] |
+-----------------------------------------------------------+
```

### (3) ローディングダイアログ (BuildTimeBlockLoadingWindow 改修後)

幅 340px、SizeToContent。

```
+------------------------------------+
| ☀  Plan My Day                    |
+------------------------------------+
|                                    |
|      Planning your day...          |
|                                    |
|           [Cancel]                 |
|                                    |
+------------------------------------+
```

### (4) 結果ダイアログ (ShowTimeBlockResultDialog 改修後)

幅 680px、高さ 520px、CanResize。

```
+----------------------------------------------------------------------+
| ☀  Today's Plan  --  2026-03-27 (Fri)                          [x] |
+----------------------------------------------------------------------+
|                                                                      |
|  Schedule: 14:00 ProjectBeta meeting / 16:30 out of office          |
|                                                                      |
| +-Morning (deep work)------------------------------------------+    |
| |                                                               |    |
| |  [!] [ProjectAlpha]  Resolve auth library decision            |    |
| |       Tension open for 6 days. Morning is the best            |    |
| |       window before cognitive load builds up.     [Open >]    |    |
| |                                                               |    |
| |  [>] [ProjectGamma]  Commit staged migrations                 |    |
| |       4 files uncommitted since Wednesday. Ship               |    |
| |       before end-of-week review.                 [Open >]    |    |
| |                                                               |    |
| +---------------------------------------------------------------+    |
|                                                                      |
| +-Afternoon----------------------------------------------------+    |
| |                                                               |    |
| |  [P] [ProjectBeta]   Prep for 14:00 meeting                  |    |
| |       Review focus and recent decisions. Capture              |    |
| |       open questions before the meeting.         [Open >]    |    |
| |                                                               |    |
| |  [*] [ProjectAlpha]  Complete Asana task: Deploy staging      |    |
| |       Due today. Afternoon slots free before standup.         |    |
| |                                                   [Open >]   |    |
| |                                                               |    |
| +---------------------------------------------------------------+    |
|                                                                      |
| +-Late afternoon (wrap-up)-------------------------------------+    |
| |                                                               |    |
| |  [D] [ProjectBeta]   Record meeting outcomes in decision_log  |    |
| |       Capture decisions before memory fades.     [Open >]    |    |
| |                                                               |    |
| |  [F] [ProjectGamma]  Update current_focus.md                  |    |
| |       Last updated 9 days ago. 3 tasks closed.   [Open >]    |    |
| |                                                               |    |
| +---------------------------------------------------------------+    |
|                                                                      |
|   Friday: close the week strong -- ship the commit and update        |
|   focus before the weekend.                                          |
|                                                                      |
+----------------------------------[View Debug]  [Copy]  [Save]  [Close]+
```

アイコン凡例:
- `[!]` tension (Warning24)
- `[>]` commit (BranchFork24)
- `[P]` meeting_prep (People24)
- `[*]` task (TaskListSquare24)
- `[D]` decision (Gavel24)
- `[F]` focus (DocumentEdit24)
- `[@]` review (Eye24)

セクション背景色:
- Morning: AppSurface0 (通常背景)
- Afternoon: AppSurface1
- Late afternoon: AppSurface0 (Morning と同じ、Afternoon の間に挟まれる視覚的区切り)

---

## 既存実装の要約

- ボタン: Clock24 アイコン (XAML line 39-44)
- ハンドラ: `OnTimeBlockClickAsync` (line 3308)
- データ収集: `CollectTimeBlockDataAsync` — SortBucket <= 2 タスクのみ、focus 先頭 200 文字
- データモデル: `TimeBlockItem { Start, End, Label, Tasks[], Project, Note }`
- 出力: HH:MM 時刻区切りのスケジュール、[Copy] / [Close] のみ

## 変更ゴール

| 項目 | 現在 | 改修後 |
|---|---|---|
| ボタン表示 | Clock24 アイコン | WeatherSunny24 アイコン + "Plan My Day" テキスト |
| データ収集 | SortBucket <= 2 のみ | 全 30 件 + project signals + 昨日 standup |
| 時間表現 | HH:MM 具体時刻 | morning / afternoon / late_afternoon ピリオド |
| 結果ダイアログ | テキストリスト | カテゴリアイコン + [Open] + overall_advice + [Save] |
| スケジュールヒント | なし | ユーザー入力ダイアログ (任意) |
| 自動起動 | なし | 朝の初回起動で確認プロンプト |

---

## データモデル (既存 TimeBlockItem を置き換え)

```csharp
// DashboardPage.xaml.cs 内に追加 (TimeBlockItem の代替)
private sealed class DailyPlan
{
    public List<DayPeriodBlock> Blocks { get; set; } = [];
    public string? OverallAdvice { get; set; }
}

private sealed class DayPeriodBlock
{
    public string Period { get; set; } = "";   // "morning" | "afternoon" | "late_afternoon"
    public List<DayPlanItem> Items { get; set; } = [];
}

private sealed class DayPlanItem
{
    public string Project { get; set; } = "";
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? TargetFile { get; set; }
    public string Category { get; set; } = "";  // task|focus|decision|commit|tension|meeting_prep|review
}
```

TimeBlockItem は削除 (参照箇所は ParseTimeBlockResponse / ShowTimeBlockResultDialog のみ)。

---

## LLM プロンプト

### System Prompt (TimeBlockSystemPrompt を置き換え)

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
- "tension": Address an item in open_issues.md
- "meeting_prep": Prepare for an upcoming meeting
- "review": Review status, summary, or weekly priorities

## Tone
- Action-oriented: "Ship the migration script" not "Consider reviewing the task list"
- Time-aware: explain WHY this time slot specifically
- Encouraging but realistic: acknowledge heavy days, suggest what to defer if overloaded
```

### User Prompt (BuildTimeBlockUserPrompt を置き換え)

```
## Date
{today: yyyy-MM-dd (DayOfWeek)}

## Schedule hints
{ユーザー入力、または "(none)"}

## Yesterday's standup (for continuity)
{昨日 standup/{date}_standup.md 先頭 1000 文字、または "(no standup available)"}

## Today Queue (sorted by priority)
- [{ProjectShortName}] {DisplayMainTitle} ({DueLabel})
  ... (上位 30 件)

## Project Signals

### {ProjectName}
- Focus age: {N} days {(stale) if > 7}
- Uncommitted repos: {count}
- Open tensions: yes/no ({count} items)
- Recent decisions: {date or "none"}
- Active workstreams: {count}
- Focus preview: {先頭 500 文字}

... (全プロジェクト繰り返し)

## Instruction
Create a time-blocked daily plan based on the data above.
Return JSON object only.
```

---

## 実装タスク

### Phase 1: データモデル置き換え

- [ ] 1-1. `TimeBlockItem` を `DailyPlan / DayPeriodBlock / DayPlanItem` に置き換え
  - ファイル: `DashboardPage.xaml.cs` (line 3298-3306)

### Phase 2: スケジュールヒント入力ダイアログ (新規メソッド)

- [ ] 2-1. `ShowScheduleHintDialogAsync()` を実装
  - Window (500 x SizeToContent, NoResize, WindowStyle.None, BorderThickness(1))
  - タイトル: "☀ Plan My Day"
  - TextBox (MultiLine, AcceptsReturn=True, Height=80, Placeholder=任意)
  - [Cancel] → null 返す / [Plan] → 入力文字列 (空でもOK) 返す
  - ファイル: `DashboardPage.xaml.cs`

- [ ] 2-2. 自動起動時の確認プロンプト `ShowMorningConfirmDialogAsync()` を実装
  - タイトル: "☀ Good morning!"
  - 本文: "Generate today's plan?"
  - [Not today] → false / [Let's go] → true
  - ファイル: `DashboardPage.xaml.cs`

### Phase 3: データ収集の強化

- [ ] 3-1. `CollectTimeBlockDataAsync` を `CollectPlanMyDayDataAsync` に改名し拡張
  - 既存: SortBucket <= 2 タスク + focus 200文字
  - 追加: 全 30 件タスク、focus 500文字、project signals (tensions/focus age/uncommitted/decisions)
  - 追加: 昨日 standup ファイル読み込み (`ReadYesterdayStandupAsync`)
  - ファイル: `DashboardPage.xaml.cs`

- [ ] 3-2. `ReadYesterdayStandupAsync()` ヘルパーを実装
  - settings から ObsidianVaultRoot を取得 (ConfigService 経由)
  - `{ObsidianVaultRoot}/standup/{yesterday:yyyyMMdd}_standup.md` を検索
  - 先頭 1000 文字を返す (不在なら空文字)
  - ファイル: `DashboardPage.xaml.cs`

### Phase 4: プロンプト & パーサー置き換え

- [ ] 4-1. `TimeBlockSystemPrompt` を上記 System Prompt に置き換え
  - ファイル: `DashboardPage.xaml.cs` (line 3282)

- [ ] 4-2. `BuildTimeBlockUserPrompt` を `BuildPlanMyDayUserPrompt` に改名し全面書き直し
  - 引数追加: `string scheduleHint`, `string yesterdayStandup`, `Dictionary<string, ProjectSignals> signals`
  - ファイル: `DashboardPage.xaml.cs`

- [ ] 4-3. `ParseTimeBlockResponse` を `ParseDailyPlanResponse` に改名し書き直し
  - JSONオブジェクト `{ blocks, overall_advice }` をパース → DailyPlan
  - パース失敗時は fallback (生テキスト表示) はそのまま維持
  - ファイル: `DashboardPage.xaml.cs`

### Phase 5: ハンドラ更新 (OnTimeBlockClickAsync)

- [ ] 5-1. `OnTimeBlockClickAsync` を書き直し
  - ヒント入力ダイアログ (2-1) → キャンセルで中断
  - データ収集 (3-1) → プロンプト構築 → ローディング → LLM → 計画ダイアログ
  - ファイル: `DashboardPage.xaml.cs`

- [ ] 5-2. ローディングウィンドウのテキスト更新
  - `BuildTimeBlockLoadingWindow`: "Planning your day..." に変更、アイコン ☀ に変更
  - ファイル: `DashboardPage.xaml.cs`

### Phase 6: 結果ダイアログ刷新 (ShowTimeBlockResultDialog)

- [ ] 6-1. `ShowTimeBlockResultDialog` を DailyPlan 対応に全面書き直し
  - タイトル: "☀ Today's Plan — {date}"
  - 3 セクション: Morning / Afternoon / Late afternoon (背景色で区分)
  - 各アイテム: カテゴリアイコン (Symbol) + [Project] Action テキスト + Reason + [Open] ボタン
  - overall_advice フッター (あれば)
  - [Copy] / [Save & Close] / [Close] ボタン
  - サイズ: 680 x 520、CanResize、WindowChrome
  - ファイル: `DashboardPage.xaml.cs`

- [ ] 6-2. [Open] ボタンの実装
  - What's Next と同じナビゲーションロジック (NavigateToSuggestionDirect を流用または参考に)
  - target_file あり → NavigateToEditorAndOpenFile
  - target_file なし + category != "commit" → NavigateToEditor (プロジェクト選択)
  - category == "commit" → NavigateToGitRepos
  - ファイル: `DashboardPage.xaml.cs`

- [ ] 6-3. [Copy] ボタン: `BuildTimeBlockClipboardText` を `BuildPlanMyDayClipboardText` に書き直し
  - Markdown形式: ヘッダー + 時間ピリオドセクション + 番号付きアイテム
  - ファイル: `DashboardPage.xaml.cs`

- [ ] 6-4. [Save & Close] ボタンの実装
  - `{ObsidianVaultRoot}/standup/{date}_plan.md` に Markdown 保存
  - FileEncodingService.WriteFileAsync (UTF-8BOM)
  - ObsidianVaultRoot 未設定時はボタン非表示 (Visibility.Collapsed)
  - ファイル: `DashboardPage.xaml.cs`

### Phase 7: 自動起動

- [ ] 7-1. `ShouldShowMorningAutopilot()` を実装
  - AI有効 && 6時以降 && 平日 && 当日未表示 → true
  - `static DateTime? _planMyDayShownToday` で管理
  - ファイル: `DashboardPage.xaml.cs`

- [ ] 7-2. `OnLoaded` から自動起動判定を呼び出し
  - 既存の Loaded イベントハンドラに追記
  - 条件合致時 → ShowMorningConfirmDialogAsync → [Let's go] で OnTimeBlockClickAsync を呼ぶ
  - ファイル: `DashboardPage.xaml.cs`

### Phase 8: XAML ボタン更新

- [ ] 8-1. Clock24 ボタンを Plan My Day ボタンに変更
  - Icon: `WeatherSunny24`
  - Content: "Plan My Day" (テキスト付きボタンに変更)
  - ToolTip: "Plan my day — AI generates a time-blocked plan with project signals and task priorities"
  - ファイル: `DashboardPage.xaml` (line 39-44)

---

## カテゴリアイコンマッピング (Phase 6-1 で使用)

| category | wpf-ui Symbol |
|---|---|
| task | TaskListSquare24 |
| focus | DocumentEdit24 |
| decision | Gavel24 |
| commit | BranchFork24 |
| tension | Warning24 |
| meeting_prep | People24 |
| review | Eye24 |

---

## ファイル変更一覧

| ファイル | 変更内容 |
|---|---|
| `Views/Pages/DashboardPage.xaml` | Phase 8: ボタン更新 (2行) |
| `Views/Pages/DashboardPage.xaml.cs` | Phase 1-7: データモデル/ハンドラ/ダイアログ全面改修 |

新規ファイル: なし

---

## 実装順序

Phase 1 (モデル) → Phase 3 (データ収集) → Phase 4 (プロンプト) → Phase 5 (ハンドラ) → Phase 2 (ヒント入力ダイアログ) → Phase 6 (結果ダイアログ) → Phase 7 (自動起動) → Phase 8 (XAML)

Phase 1-5 がロジック、Phase 6 が最大の作業 (UI コード量)、Phase 7-8 が仕上げ。

---

## 既存コードの活用方針

| 既存要素 | 扱い |
|---|---|
| `CollectFocusPreviewsAsync` | Phase 3-1 に統合 (focus 500文字に拡大) |
| `BuildWhatsNextUserPrompt` の Project Signals 部分 | Phase 4-2 でそのままコピー流用 |
| `NavigateToSuggestionDirect` / `ResolveWhatsNextTargetFile` | Phase 6-2 で流用 (引数型を DayPlanItem に合わせて調整) |
| `BuildTimeBlockLoadingWindow` | Phase 5-2 でテキスト/アイコンのみ修正、構造は維持 |
| `ShowWhatsNextLogDialog` | Debug ボタンでそのまま流用 |
| `BuildTimeBlockClipboardText` | Phase 6-3 で置き換え |

---

## What's Next との住み分け

| 観点 | What's Next (維持) | Plan My Day (改修後) |
|---|---|---|
| 起動 | ボタン手動 | 朝の自動起動 + ボタン |
| 出力 | 優先順タスク 3-5 件 | 時間ピリオド付き 4-7 件の1日計画 |
| 時間軸 | なし | morning / afternoon / late_afternoon |
| 理由 | 1-2 文 | 「なぜ今日・なぜこの時間帯」 |
| ファイル保存 | なし | Markdown 保存 (任意) |
| ナビゲーション | [Open] ボタンあり | [Open] ボタンあり |
