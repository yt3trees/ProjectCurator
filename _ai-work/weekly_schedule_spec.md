# Weekly Schedule - 実装計画

Outlook の週表示のように、30分単位のグリッド上にタスクをカード配置してスケジュールできる機能を追加する。

`tasks.md` に記録された各プロジェクトのタスクをカード化し、週カレンダーの任意時間帯にドラッグで貼り付ける。時間ブロッキング (タイムブロッキング) 運用を支援する。

Outlook と同様に、2種類のブロックをサポートする:

- Timed block: 特定の時刻 (例: 13:00 〜 15:00) を占有する、30分単位のカード。週グリッドの時間エリアに描画
- All-day block: 特定の1日以上 (例: 4/15 〜 4/17) を占有する、時刻を持たないカード。週グリッド上部の「終日レーン」に帯として描画。1日のみも複数日またぎも表現可能

## コンセプト

```
tasks.md (source of truth for task content)
        │
        ▼
  [ WeeklySchedulePage ]
   左: 未配置タスクリスト (プロジェクト横断, tasks.md 由来)
   右: 週カレンダー (月〜日 x 30分刻み)
        │ ドラッグ&ドロップ
        ▼
  schedule.json (time block assignments)
```

- タスク本体の source of truth は引き続き `tasks.md`。スケジュール機能はタスクの「時間割当」だけを別ファイルで管理する
- 完了状態・タイトル・Due 等はカード側では持たず、常に `tasks.md` 側を参照する (TodayQueueService のパース結果を再利用)
- カードは「tasks.md のタスク identity 」 + 「開始時刻 / 長さ」の組で表現する
- Asana 有無にかかわらず動作する (Local Task Mode と同様)

## 用語

| 用語 | 意味 |
|---|---|
| Schedule Block | カレンダーに配置された1枚のカード。1つの tasks.md タスクに対応 |
| Timed Block | 時刻指定のカード。30分単位で `StartAt` + `DurationSlots` を持つ |
| All-day Block | 終日カード。時刻を持たず `StartDate` + `EndDate` (両端含む) で1日以上を占有 |
| Time Slot | 30分の最小単位。1日 = 48 slot |
| Week Grid | 月〜日 x (All-day lane + 48 slot) の2次元格子 |
| All-day Lane | 週グリッド上部の、日付列ごとに横帯で終日ブロックを描画する領域 |
| Task Identity | tasks.md 内でタスクを一意特定するキー。Asana GID があれば GID、なければ `ProjectShortName|WorkstreamId|Title` |

## Phase 0: データモデル

### 1. 新規モデル `ScheduleBlock` (Models/ScheduleBlock.cs)

Timed / All-day を `Kind` プロパティで区別する単一モデルにする (Outlook の ScheduleItem と同型)。

```csharp
namespace Curia.Models;

public enum ScheduleBlockKind
{
    Timed,    // 時刻指定 (例: 13:00〜15:00)
    AllDay,   // 終日 / 複数日またぎ (例: 4/15〜4/17)
}

public class ScheduleBlock
{
    /// <summary>ブロック識別用 GUID (移動/削除操作用)。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Timed / AllDay の区別。</summary>
    public ScheduleBlockKind Kind { get; set; } = ScheduleBlockKind.Timed;

    /// <summary>タスク識別キー。AsanaTaskGid があればそれ、なければ合成キー。</summary>
    public string TaskIdentity { get; set; } = "";

    /// <summary>表示用プロジェクト名 (ProjectShortName)。</summary>
    public string ProjectShortName { get; set; } = "";

    /// <summary>表示用タスクタイトル (カード見出し)。キャッシュ用途。</summary>
    public string TitleSnapshot { get; set; } = "";

    // --- Timed 用 ---
    /// <summary>開始時刻 (ローカル時刻, 30分境界にスナップ)。Kind=Timed 時のみ有効。</summary>
    public DateTime? StartAt { get; set; }

    /// <summary>長さ (30分単位, 最小 1 = 30分, 最大 48 = 24時間)。Kind=Timed 時のみ有効。</summary>
    public int DurationSlots { get; set; } = 2;

    // --- AllDay 用 ---
    /// <summary>終日ブロックの開始日 (0:00 基準, ローカル時刻)。Kind=AllDay 時のみ有効。</summary>
    public DateTime? StartDate { get; set; }

    /// <summary>終日ブロックの終了日 (両端含む, 例: 4/15〜4/17 = 3日間)。Kind=AllDay 時のみ有効。</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>ユーザーが手動で付けたメモ (任意)。</summary>
    public string? Note { get; set; }

    /// <summary>カード表示色のキー (optional, project 単位の自動割り当て or 手動)。</summary>
    public string? ColorKey { get; set; }

    // --- 算出プロパティ ---
    public DateTime EndAtExclusive => Kind switch
    {
        ScheduleBlockKind.Timed  => StartAt!.Value.AddMinutes(DurationSlots * 30),
        ScheduleBlockKind.AllDay => EndDate!.Value.Date.AddDays(1),
        _ => throw new InvalidOperationException()
    };

    public int SpanDays => Kind == ScheduleBlockKind.AllDay
        ? (int)(EndDate!.Value.Date - StartDate!.Value.Date).TotalDays + 1
        : 1;
}
```

バリデーション:

- Timed 作成時: `StartAt`, `DurationSlots >= 1` 必須、`StartAt` は分が 00/30 のみ許容 (それ以外は 30分境界に丸める)
- AllDay 作成時: `StartDate`, `EndDate` 必須、`EndDate >= StartDate`
- Kind に合わないフィールドは JSON 出力時に null とする (`JsonIgnoreCondition.WhenWritingNull`)

### 2. 永続化ファイル `schedule.json`

配置場所: `[config dir]/schedule.json` (ユーザー単位, 全プロジェクト横断)

理由:
- 「いつ何をやるか」はユーザーの時間管理情報であり、特定プロジェクトの成果物ではない
- プロジェクト横断でカレンダーを見たいので単一ファイルの方が扱いやすい
- `%USERPROFILE%\.curia\` または legacy config dir に配置 (既存の `ConfigService.ConfigDir` と同じ場所)

フォーマット:

```json
{
  "version": 1,
  "blocks": [
    {
      "id": "a3f2...",
      "kind": "Timed",
      "taskIdentity": "1201234567890",
      "projectShortName": "ProjectAlpha",
      "titleSnapshot": "API 設計レビュー",
      "startAt": "2026-04-13T13:00:00",
      "durationSlots": 4,
      "note": null,
      "colorKey": "blue"
    },
    {
      "id": "b7e1...",
      "kind": "AllDay",
      "taskIdentity": "ProjectBeta||Onsite workshop",
      "projectShortName": "ProjectBeta",
      "titleSnapshot": "Onsite workshop",
      "startDate": "2026-04-15T00:00:00",
      "endDate": "2026-04-17T00:00:00",
      "note": "Tokyo office",
      "colorKey": "green"
    }
  ]
}
```

### 3. 新規サービス `ScheduleService`

`Services/ScheduleService.cs` に配置、`App.xaml.cs` で singleton 登録。

```csharp
public class ScheduleService
{
    private readonly ConfigService _configService;
    private readonly FileEncodingService _encoding;
    private readonly object _lock = new();
    private List<ScheduleBlock> _blocks = new();
    private bool _loaded;

    public ScheduleService(ConfigService configService, FileEncodingService encoding);

    /// <summary>週 (月曜 0:00 〜 次週月曜 0:00) と重なる全ブロックを返す。
    /// AllDay は [StartDate, EndDate] のいずれかが週に含まれれば該当。
    /// Timed は [StartAt, EndAtExclusive) が週と重なれば該当。</summary>
    public IReadOnlyList<ScheduleBlock> GetBlocksForWeek(DateTime weekStart);

    public void AddBlock(ScheduleBlock block);
    public void UpdateBlock(ScheduleBlock block);        // 移動/リサイズ (Timed/AllDay 共通)
    public void RemoveBlock(string blockId);
    public void RemoveBlocksByTaskIdentity(string taskIdentity); // タスク削除時の整合

    private void EnsureLoaded();
    private void SaveToDisk();                           // debounce 200ms で呼ぶ
}
```

- `EnsureLoaded()` は初回呼び出し時のみ `schedule.json` を読み込む
- 書き込みは 200ms の debounce で集約 (ドラッグ中に連続 `UpdateBlock` が来るため)
- JSON シリアライズは `System.Text.Json` を使用 (既存 `ConfigService` と同じ)

## Phase 1: Weekly Schedule ページ

### 4. 新規ページ `WeeklySchedulePage`

`Views/Pages/WeeklySchedulePage.xaml(.cs)` + `ViewModels/WeeklyScheduleViewModel.cs` を新規追加。

Dashboard / Editor / Timeline / Settings に並ぶ5番目のナビゲーション項目として追加。

ナビゲーション追加箇所:
- `MainWindow.xaml.cs` の navigation items に "Weekly" (仮) を追加
- `App.xaml.cs` に `WeeklySchedulePage` / `WeeklyScheduleViewModel` を singleton 登録

### 5. 画面レイアウト

```
+--------------------------------------------------------------------------------+
| [< Prev week]  2026-04-13 〜 04-19  [Today] [Next week >]   Zoom: [30m ▼]      |
+----------------+---------------------------------------------------------------+
| Unscheduled    |      |  Mon   Tue   Wed   Thu   Fri   Sat   Sun              |
| Tasks          |      | 04/13 04/14 04/15 04/16 04/17 04/18 04/19             |
|                +------+-------------------------------------------------------+
|                |      |                                                       |
| [ProjectAlpha] |All-  |              [=== Onsite workshop ===]                |
|  - API review  |day   |  [Release freeze]                                     |
|  - Bug fix #42 +------+-------------------------------------------------------+
|                |  8:00|                                                       |
| [ProjectBeta]  |  8:30|                                                       |
|  - Design sync |  9:00|                                                       |
|  - Draft spec  |  9:30|                                                       |
|                | 10:00|                                                       |
|  (drag → grid) | 10:30|                                                       |
|                | 11:00|                                                       |
|                | 11:30|                                                       |
|                | 12:00|                                                       |
|                | 12:30|                                                       |
|                | 13:00|  [API                                                 |
|                | 13:30|   review                                              |
|                | 14:00|   13:00-15:00]                                        |
|                | 14:30|                                                       |
|                | 15:00|                                                       |
+----------------+------+-------------------------------------------------------+
```

- 左ペイン (幅 280): 未配置タスク一覧 (プロジェクトごとにグループ化)
- 右ペイン: 週グリッド
  - 最上段の「All-day lane」: 日付列ごとに終日ブロックを横帯として描画。複数ブロックが重なる場合は下方向に段組 (Outlook と同じ)
  - その下: 月〜日 x 30分スロットの時間グリッド。スクロール可能、表示時間帯は 6:00 〜 22:00 をデフォルト、上下スクロールで 0:00 〜 24:00 まで
- All-day lane の高さは動的: 同じ日に重なる AllDay ブロック数に応じて段数を増やす (1段 = 24 px, 最小1段)
- 上部ツールバー: 前週/次週/Today ジャンプ, ズーム (将来用, 初期は 30m 固定)

### 6. 左ペイン: Unscheduled Task List

ソース: `TodayQueueService` で全プロジェクトのタスクをロード → 「In Progress」かつ「週グリッドに未配置のもの」だけを抽出して表示。

```csharp
// WeeklyScheduleViewModel
public ObservableCollection<UnscheduledTaskGroup> UnscheduledGroups { get; } = new();

private async Task LoadUnscheduledAsync()
{
    var allTasks = await _todayQueueService.GetAllOpenTasksAsync();
    var scheduledIdentities = _scheduleService.GetBlocksForWeek(_weekStart)
        .Select(b => b.TaskIdentity)
        .ToHashSet();

    var filtered = allTasks
        .Where(t => !scheduledIdentities.Contains(BuildIdentity(t)))
        .GroupBy(t => t.ProjectShortName)
        .Select(g => new UnscheduledTaskGroup(g.Key, g.ToList()));

    UnscheduledGroups.Clear();
    foreach (var group in filtered) UnscheduledGroups.Add(group);
}
```

`TodayQueueService` に `GetAllOpenTasksAsync` が既存でない場合は、既存の Today Queue 組み立て経路を再利用する形で未完了タスクを取得するヘルパーを切り出す (既存パースロジックは変更しない)。

### 7. 右ペイン: Week Grid

XAML 構造 (概略):

```xaml
<DockPanel>
    <!-- ヘッダー (日付行) : 固定 -->
    <Grid DockPanel.Dock="Top" x:Name="HeaderRow">
        <!-- 時刻列 60px + 月〜日の7列 -->
    </Grid>

    <!-- All-day lane : 固定 (スクロールしない) -->
    <Grid DockPanel.Dock="Top" x:Name="AllDayLane" MinHeight="28">
        <!-- 時刻列のプレースホルダ + 各曜日のドロップ領域 + AllDay ブロックの Canvas オーバーレイ -->
    </Grid>

    <!-- 時刻グリッド : 垂直スクロール -->
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <Grid x:Name="TimeGrid">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="60"/>
                <ColumnDefinition Width="*"/>  <!-- Mon 〜 Sun の7列 -->
                ...
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <!-- 48 行 × 30分 = 24時間。各行 Height=20 で合計 960 px -->
                <RowDefinition Height="20"/>
                ...
            </Grid.RowDefinitions>

            <!-- 時刻ラベル (1時間ごとにテキスト, 30分は細線のみ) -->
            <!-- 各セルは Border で枠線、DragEnter/Drop をハンドル -->
            <!-- Timed ScheduleBlock は上層 Canvas にオーバーレイ描画 -->
        </Grid>
    </ScrollViewer>
</DockPanel>
```

実装アプローチ:

- 3段構造 (ヘッダー / All-day lane / 時刻グリッド) を `DockPanel` で縦に積む。時刻グリッドだけ `ScrollViewer` で縦スクロール
- Timed 層: 時刻グリッドの上に `Canvas` をもう1枚重ね、`Canvas.Left` / `Canvas.Top` / `Height` を計算して絶対配置
  - `Top = slotIndex * 20`、`Height = DurationSlots * 20`
  - `Left = dayIndex * dayColumnWidth + 60`、`Width = dayColumnWidth - 2`
- AllDay 層: All-day lane 内に `Canvas` を配置
  - `Top = laneIndex * 24` (重なり解決のレーン割当結果)
  - `Left = startDayIndex * dayColumnWidth + 60`
  - `Width = SpanDays * dayColumnWidth - 2` (週境界を超える分はクリップ)
- 1 slot = 20 px (固定)、1 day 列幅 = 可変 (`*`) → day column の実幅は `SizeChanged` で再計算してブロックの絶対座標を更新
- 具体的には `WeekGridControl` を UserControl として切り出し、時刻グリッドと All-day lane の配置ロジックをそこに集約

### 7-1. All-day lane の重なり解決

同じ日に複数の AllDay ブロックが被る場合の段組 (レーン割当) アルゴリズム:

```csharp
// 単純な greedy 割当。Outlook も実質これ。
int AssignAllDayLane(List<ScheduleBlock> allDayBlocks)
{
    var lanes = new List<DateTime>(); // 各レーンの「次に空く日」を保持
    foreach (var b in allDayBlocks.OrderBy(x => x.StartDate))
    {
        int placed = -1;
        for (int i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] <= b.StartDate!.Value.Date)
            {
                placed = i;
                break;
            }
        }
        if (placed < 0) { placed = lanes.Count; lanes.Add(DateTime.MinValue); }
        lanes[placed] = b.EndDate!.Value.Date.AddDays(1);
        b.LaneIndex = placed; // ViewModel 層の一時プロパティ
    }
    return lanes.Count; // 必要な段数
}
```

All-day lane の高さは `max(1, 必要段数) * 24 px` で動的に決める。

### 7-2. 週境界をまたぐ AllDay ブロックの扱い

例: `StartDate = 4/11 (金)`, `EndDate = 4/14 (月)` のブロックを 4/13 (月) 始まりの週で表示する場合:

- 該当週の描画上の開始日 = `max(StartDate, weekStart)` = 4/13 (月)
- 該当週の描画上の終了日 = `min(EndDate, weekEnd)` = 4/14 (火)
- つまり月〜火の2日分だけ帯を描画
- 帯の左右に「継続マーカー」(◀ / ▶ の小さな三角) を表示して、前後週にも続いていることを示す

### 8. ドラッグ&ドロップ

Timed / AllDay の両方に対する操作をサポート:

| 操作 | Source | Target | 結果 |
|---|---|---|---|
| 新規配置 (Timed) | 左ペインのタスク項目 | 時刻グリッドの空きセル | `ScheduleBlock(Kind=Timed)` を新規作成 (`DurationSlots=2` = 60分) |
| 新規配置 (AllDay) | 左ペインのタスク項目 | All-day lane のセル | `ScheduleBlock(Kind=AllDay, StartDate=EndDate=その日)` を新規作成 (1日) |
| 時刻範囲での新規配置 | 時刻グリッドの空き領域を縦ドラッグ | 開始セル〜終了セル | ドラッグ範囲で `StartAt` + `DurationSlots` を決定し、その後ポップオーバーでタスクを選択 |
| 移動 (Timed) | 既存 Timed ブロック | 時刻グリッド内の別セル | `StartAt` を更新。異なる曜日間も可 |
| 移動 (AllDay) | 既存 AllDay ブロック | All-day lane 内の別日 | `StartDate` / `EndDate` を同じだけシフト |
| リサイズ (Timed) | 既存 Timed ブロックの上端/下端 | 上下にドラッグ | `StartAt` または `DurationSlots` を更新 |
| リサイズ (AllDay) | 既存 AllDay ブロックの左端/右端 | 左右にドラッグ | `StartDate` または `EndDate` を更新 |
| Timed ⇄ AllDay 相互変換 | 既存ブロックを反対レーンにドラッグ | All-day lane ⇔ 時刻グリッド | `Kind` を切り替え、必要なフィールドを埋め直す |

#### 新規配置: DragDrop (左ペインから)

```csharp
// 左ペインの ListBoxItem.PreviewMouseMove で DoDragDrop を開始
private void OnTaskItemMouseMove(object sender, MouseEventArgs e)
{
    if (e.LeftButton != MouseButtonState.Pressed) return;
    if (sender is not FrameworkElement fe || fe.DataContext is not TodayQueueTask task) return;

    var data = new DataObject("Curia.TaskDragPayload", new TaskDragPayload(task));
    DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
}

// 時刻グリッドのセル: Timed ブロックを作成
private void OnTimedCellDrop(object sender, DragEventArgs e)
{
    if (e.Data.GetData("Curia.TaskDragPayload") is not TaskDragPayload payload) return;
    var (dayIndex, slotIndex) = GetCellPosition((FrameworkElement)sender);
    var startAt = _weekStart.AddDays(dayIndex).AddMinutes(slotIndex * 30);
    ViewModel.CreateTimedBlockCommand.Execute(new(payload.Task, startAt, durationSlots: 2));
}

// All-day lane のセル: AllDay ブロックを作成
private void OnAllDayCellDrop(object sender, DragEventArgs e)
{
    if (e.Data.GetData("Curia.TaskDragPayload") is not TaskDragPayload payload) return;
    var dayIndex = GetDayIndex((FrameworkElement)sender);
    var date = _weekStart.AddDays(dayIndex).Date;
    ViewModel.CreateAllDayBlockCommand.Execute(new(payload.Task, date, date));
}
```

#### 時刻範囲ドラッグでの新規作成

Outlook のように「空き時間を縦ドラッグ → 選択範囲で予定作成」を実現する。

```
1. 時刻グリッド上で MouseLeftButtonDown
   → 開始 slot を記録、Canvas に「選択中の半透明ブロック」を描画開始
2. MouseMove
   → 現在位置まで半透明ブロックを伸縮 (30分単位にスナップ)
3. MouseLeftButtonUp
   → 範囲が確定。その場にポップオーバーを出して
     「どのタスクを割り当てるか」リストから選択
   → 選択後 ScheduleBlock(Kind=Timed, StartAt, DurationSlots) を作成
   → ポップオーバー上で [New task…] を選ぶと、CaptureWindow の
     fixedCategory=task モードを開いてその場でタスクを新規作成 → 配置
```

この経路は「13:00〜15:00 でこのタスク」という時間指定配置そのもの。単発クリック (MouseDown + すぐに Up) の場合は 60分 (2 slot) の既定幅で扱う。

#### All-day lane での日数ドラッグ

時刻範囲ドラッグと同じ UX を All-day lane に適用する。月〜水を横ドラッグすれば `StartDate=月, EndDate=水` の3日間ブロックが作れる。

#### 既存ブロックの移動 / リサイズ

- 移動: ブロック本体を掴んで `MouseMove` で追随、`MouseLeftButtonUp` で最寄りスロット (または日) にスナップして確定
- リサイズ:
  - Timed: ブロック上端/下端に 4px の `Thumb` を置き、上下ドラッグで `StartAt` / `DurationSlots` を更新
  - AllDay: ブロック左端/右端に 4px の `Thumb` を置き、左右ドラッグで `StartDate` / `EndDate` を更新
- 30分 / 1日 へのスナップは `round(delta / 20)` / `round(deltaX / dayColumnWidth)` で計算

#### Timed ⇄ AllDay の相互変換

既存ブロックを反対レーンへドロップした場合:

- Timed → AllDay: `StartDate = EndDate = StartAt.Date`、`StartAt` / `DurationSlots` をクリア
- AllDay → Timed: `StartAt = StartDate + 9:00`、`DurationSlots = 2`、`StartDate` / `EndDate` をクリア

初期実装では相互変換は任意 (Phase 2 以降)。最低限「削除 → 新規配置」で代替できる。

### 9. カードクリック時の操作

- クリック (単発): ポップオーバーでブロック詳細を表示
  - Timed: タイトル, プロジェクト, `開始 - 終了` (例: 13:00 - 15:00), 所属曜日, Note
  - AllDay: タイトル, プロジェクト, `開始日 - 終了日` (例: 4/15 - 4/17 / 3日間), Note
  - 共通ボタン: [Open in Editor] / [Unschedule] / [Delete block]
  - 時刻や日付はポップオーバー内でその場編集可能 (数値/DatePicker)
- ダブルクリック: そのタスクを EditorPage で開く (既存の `OnOpenInEditor` コールバック経由)
- 右クリック: コンテキストメニュー ([Unschedule], [Delete block], [Open in Editor], [Convert to All-day] / [Convert to Timed])

`Unschedule` はブロック削除 (タスク自体は残る)。`Delete block` と同じだが将来的な差分用の語彙として用意しておく。

## Phase 2: タスクとブロックの整合

### 10. タスク完了時のブロック扱い

Today Queue などでタスクが完了された場合の方針:

- ブロックは自動削除しない (履歴として残す)
- 完了済みタスクに紐づくブロックはカード上に「取り消し線 + グレーアウト」で表示
- 次週に繰り越さない

完了判定: `ScheduleBlock.TaskIdentity` で対応する `TodayQueueTask` を検索し、ない場合 (= 完了/削除済み) はグレーアウト。

### 11. タイトル変更 / タスク削除の追従

タスクのタイトルが変わった場合、`TitleSnapshot` は古いまま残る。ロード時に `TaskIdentity` で現在のタイトルを引き当てて差し替える。

```csharp
// WeeklyScheduleViewModel の LoadWeek 時
var identityToTask = allTasks.ToDictionary(BuildIdentity, t => t);
foreach (var block in blocksInWeek)
{
    if (identityToTask.TryGetValue(block.TaskIdentity, out var live))
    {
        block.TitleSnapshot = live.DisplayMainTitle;
    }
}
```

タスクが完全に消えた場合 (ファイル編集で行を削除等) は `TitleSnapshot` を保持したままグレーアウト表示。ユーザーが手動で [Delete block] できる。

## Phase 3: 小さな便利機能

### 12. Today Queue からの「今日にブロック化」

Dashboard の Today Queue に [Schedule] ボタンを追加し、クリックで WeeklySchedulePage を開いて当該タスクを今日 / 次の空き時間に自動配置する。

- 初期実装では WeeklySchedulePage に遷移するだけで充分 (自動配置は Phase 4)

### 13. 現在時刻インジケータ

今日の列に、現在時刻の位置に赤い水平線を描画。1分ごとに更新。

### 14. 空き時間の濃淡ヒント

ブロックが存在しないセルは薄い背景、存在する範囲は少し濃い背景 (単純な視覚強化)。初期実装では省略可。

## 影響範囲まとめ

| ファイル | 変更種別 | 規模 |
|---|---|---|
| Models/ScheduleBlock.cs | 新規 | 小 |
| Services/ScheduleService.cs | 新規 | 中 |
| ViewModels/WeeklyScheduleViewModel.cs | 新規 | 中 |
| Views/Pages/WeeklySchedulePage.xaml | 新規 | 中 |
| Views/Pages/WeeklySchedulePage.xaml.cs | 新規 | 小 |
| Views/Controls/WeekGridControl.xaml(.cs) | 新規 (グリッド+ブロック描画) | 中 |
| App.xaml.cs | DI 登録追加 | 小 |
| MainWindow.xaml(.cs) | ナビゲーション項目追加 | 小 |
| Services/TodayQueueService.cs | `GetAllOpenTasksAsync` ヘルパー追加 (必要に応じて) | 小 |

### 変更しないもの

- `tasks.md` のフォーマット
- `TodayQueueService` のパースロジック
- `AsanaSyncService` / `FocusUpdateService` など AI 系機能
- 既存の Dashboard / Editor / Timeline ページ

## 実装順序

### Phase 0: データ層

- [x] 1. `ScheduleBlock` モデル追加
- [x] 2. `ScheduleService` 追加 (CRUD + JSON 永続化, debounce save)
- [x] 3. `App.xaml.cs` で singleton 登録
- [x] 4. `dotnet build` で型チェック

### Phase 1: 閲覧だけの Week Grid

- [x] 5. `WeeklyScheduleViewModel` の骨格 (週移動, ブロックリスト公開)
- [x] 6. `WeeklySchedulePage` + 左ペイン (未配置タスク) + 右ペイン (空の週グリッド)
- [x] 7. ナビゲーション追加 (MainWindow, App.xaml.cs)
- [ ] 8. 既存の `schedule.json` をダミーで配置して、ブロックが正しく描画されることを確認

### Phase 2: ドラッグで配置

- [x] 9. 左ペイン → 時刻グリッドへの DragDrop (Timed 新規配置, 既定 60分)
- [x] 10. 左ペイン → All-day lane への DragDrop (AllDay 新規配置, 1日)
- [x] 11. ブロック移動 (時刻グリッド内 / All-day lane 内)
- [x] 12. 右クリックメニュー / Unschedule
- [ ] 13. タスクのロードとブロックの整合 (完了判定, タイトル追従) ※タイトル追従は実装済み、完了タスクのグレーアウト未実装

### Phase 3: 時間範囲指定と仕上げ

- [x] 14. 時刻グリッド上の縦ドラッグ → 範囲選択 → タスク割り当てポップオーバー
- [ ] 15. All-day lane 上の横ドラッグ → 日数範囲選択 → タスク割り当てポップオーバー
- [x] 16. Timed ブロックの上下リサイズ (上端/下端 Thumb)
- [ ] 17. AllDay ブロックの左右リサイズ (左端/右端 Thumb)
- [x] 18. 現在時刻インジケータ (今日列に赤い水平線, 1分ごと更新)
- [ ] 19. カードクリック → ポップオーバー詳細 (時刻/日付の編集も可)
- [ ] 20. [Open in Editor] 連携 (既存の OnOpenInEditor コールバック経由)
- [x] 21. カード色のプロジェクト別自動割り当て

### Phase 4 (任意)

- [ ] 22. Timed ⇄ AllDay 相互変換 (反対レーンへのドロップ / 右クリック)
- [ ] 23. Today Queue の [Schedule] ボタン + 自動配置
- [ ] 24. LLM で「明日の予定を自動作成」(AI Features 連携, AiEnabled ゲート必須)
- [x] 25. 週境界またぎの AllDay ブロックの継続マーカー表示

## テスト方針

自動テストはないため手動確認:

Phase 0 〜 1:
- `schedule.json` が存在しない初期状態でページを開いても落ちないこと
- 手動で `schedule.json` に1件書いて、該当週を表示すると描画されること
- 前週/次週ナビで描画対象が切り替わること

Phase 2:
- 左ペインのタスクを時刻グリッドにドラッグすると Timed ブロックが作成され、`schedule.json` に保存されること
- 左ペインのタスクを All-day lane にドラッグすると AllDay ブロック (1日) が作成されること
- Timed カードを別セル (同曜日/別曜日) にドラッグすると `StartAt` が更新されること
- AllDay カードを別日にドラッグすると `StartDate` / `EndDate` が同じだけシフトすること
- 同日に重なる AllDay ブロックが複数ある場合、All-day lane が段組で高さが広がること
- Unschedule すると左ペインに戻ること
- Asana モードとローカルモードの両方でタスク識別キーが衝突しないこと
- タスク完了後にグレーアウト表示になること
- タイトルを `tasks.md` で編集した後、再ロードで新タイトルが反映されること

Phase 3:
- 時刻グリッドを縦ドラッグして 13:00〜15:00 を範囲選択し、ポップオーバーでタスク選択すると Timed ブロック (DurationSlots=4) が作成されること
- All-day lane を横ドラッグして月〜水を範囲選択すると AllDay ブロック (3日間) が作成されること
- Timed ブロックの下端ドラッグで長さが変わること、上端ドラッグで開始時刻が変わること
- AllDay ブロックの右端ドラッグで終了日が延びること、左端ドラッグで開始日が前倒しされること
- 現在時刻インジケータが1分ごとに移動すること
- Editor への導線が既存のコールバック経由で動作すること

## オープンな論点

- [ ] 週の開始曜日: 月曜固定 vs 設定化 (初期は月曜固定で良い)
- [ ] カードに色を付ける場合の配色戦略 (プロジェクト名ハッシュ → 固定パレット / 手動指定 / 両対応)
- [ ] Timed ブロックの競合検知 (同じスロットに2枚置くのを許すか、並列配置レーンを作るか) → 初期は重ね表示 OK, Outlook 風の並列レーンは Phase 4+
- [ ] AllDay ブロックが週境界をまたぐ場合の継続マーカー (◀ / ▶) の実装優先度
- [ ] 時刻境界を 15分/10分に細かくするオプション (初期は 30分固定)
- [ ] 終日ブロックでも開始・終了時刻を記録したい要件 (例: 会議 4/15 10:00 - 4/17 17:00) が出た場合は AllDay ではなく「複数日 Timed」として扱う。初期仕様では対象外
- [ ] カレンダー予定 (Outlook/Google) との連携は対象外 (本機能はタスクブロッキングに限定)
- [ ] チーム共有: 本機能は個人用。将来チーム化する場合は `schedule.json` の保存先を共有ドライブに差し替え可能な構造にしておく (既存の ConfigService の仕組みで吸収可能)
