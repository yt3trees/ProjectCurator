# Outlook Calendar Integration (COM Interop) - 仕様書

Weekly Schedule ページに Outlook カレンダーの予定を読み取り専用でオーバーレイ表示する機能を追加する。

Microsoft.Office.Interop.Outlook を使用した COM 自動化を採用。Azure 設定不要で、ローカルの Outlook (Exchange / Microsoft 365 / Outlook.com) に接続済みであれば全アカウントに対応する。

## コンセプト

```
Outlook (ローカルインストール)
        │ COM Interop
        ▼
  OutlookCalendarService
  (週範囲の予定を取得 → OutlookEvent リストで返す)
        │
        ▼
  WeeklyScheduleViewModel.LoadWeekAsync()
  (ScheduleBlock と OutlookEvent をマージして WeekGrid に渡す)
        │
        ▼
  WeekGridControl
  (Outlook イベントは薄い背景色 + 斜線で読み取り専用カードとして描画)
```

- Outlook 予定は読み取り専用。Curia から変更・削除は行わない
- Curia の ScheduleBlock とは独立して管理 (JSON 保存しない)
- Outlook が未インストール / 未起動でも Curia 全体の動作に影響しない (try-catch でサイレント無視)
- 表示週が変わるたびに Outlook から再取得する

## 用語

| 用語 | 意味 |
|---|---|
| OutlookEvent | Outlook の AppointmentItem を Curia 用に変換した軽量 DTO |
| Overlay Block | WeekGrid 上に Outlook イベントとして描画される読み取り専用ブロック |
| COM Interop | Windows COM を通じて Outlook プロセスを操作する .NET 機能 |

---

## Phase 0: 前提条件・制約

### 動作要件

- Windows に Outlook (デスクトップ版) がインストールされていること
- Outlook に予定表アカウントが設定されていること (Exchange / M365 / Outlook.com)
- .NET から COM Late Binding または Early Binding でアクセス可能なこと

### 非対応ケース (サイレント無視)

- Outlook 未インストール
- COM 初期化失敗 (COMException)
- Outlook プロセスが応答しない場合

### NuGet パッケージ

```
Microsoft.Office.Interop.Outlook (バージョン 15.x)
```

PIA (Primary Interop Assembly) を使用する Early Binding 方式を採用。
Late Binding (dynamic) より型安全で IntelliSense が効く。

---

## Phase 1: データモデル

### 新規モデル `OutlookEvent` (Models/OutlookEvent.cs)

Outlook の AppointmentItem を Curia 用に変換した読み取り専用 DTO。

```csharp
namespace Curia.Models;

public class OutlookEvent
{
    /// <summary>Outlook の EntryID (重複排除用)。</summary>
    public string EntryId { get; set; } = "";

    /// <summary>予定のタイトル (Subject)。</summary>
    public string Subject { get; set; } = "";

    /// <summary>開始日時 (ローカル時刻)。</summary>
    public DateTime Start { get; set; }

    /// <summary>終了日時 (ローカル時刻)。</summary>
    public DateTime End { get; set; }

    /// <summary>終日予定かどうか。</summary>
    public bool IsAllDay { get; set; }

    /// <summary>場所 (任意)。</summary>
    public string? Location { get; set; }

    /// <summary>カレンダーアカウント名 (表示用)。</summary>
    public string? CalendarName { get; set; }
}
```

---

## Phase 2: OutlookCalendarService

### 新規サービス `OutlookCalendarService` (Services/OutlookCalendarService.cs)

COM Interop で Outlook に接続し、指定週の予定を取得する。

#### 責務

- Outlook Application インスタンスの取得 (既存プロセス優先)
- 全カレンダーフォルダの予定を週範囲でフィルタリングして返す
- Outlook が使用不可の場合は空リストを返す (例外をスローしない)
- シングルトンとして登録 (App.xaml.cs)

#### インタフェース

```csharp
public class OutlookCalendarService
{
    /// <summary>
    /// 指定週 (weekStart の月曜 0:00 〜 +7日) の Outlook 予定を返す。
    /// Outlook が使用不可の場合は空リストを返す。
    /// </summary>
    public Task<IReadOnlyList<OutlookEvent>> GetEventsForWeekAsync(DateTime weekStart);

    /// <summary>Outlook が利用可能かどうかを確認する (設定 UI 用)。</summary>
    public bool IsOutlookAvailable();
}
```

#### 実装のポイント

- `Marshal.GetActiveObject("Outlook.Application")` で既存プロセスを取得、失敗時は `new Application()` で起動
- `GetDefaultFolder(OlDefaultFolders.olFolderCalendar)` でデフォルトカレンダーを取得
- 追加カレンダー (共有カレンダー等) は `Stores` コレクションを走査
- `Items.Restrict` フィルタで取得範囲を絞る (全件取得は避ける)
  ```
  [Start] >= "2025/04/14 00:00" AND [End] <= "2025/04/21 00:00"
  ```
- `Items.IncludeRecurrences = true` を設定し繰り返し予定を展開
- COM オブジェクトは `Marshal.ReleaseComObject` で確実に解放
- バックグラウンドスレッドで実行 (`Task.Run`)、完了後 UI スレッドに結果を返す

---

## Phase 3: 設定

### AppConfig への追加 (Models/AppConfig.cs)

```csharp
/// <summary>Outlook カレンダー連携を有効にするか。</summary>
public bool OutlookCalendarEnabled { get; set; } = false;

/// <summary>Outlook イベントの表示透明度 (0.0〜1.0)。デフォルト 0.4。</summary>
public double OutlookEventOpacity { get; set; } = 0.4;
```

### Settings ページへの追加

- Settings > Schedule セクション (新設)
- "Show Outlook Calendar Events" トグル (OutlookCalendarEnabled)
- Outlook が利用不可の場合はトグルを Disabled にして "Outlook not available" を表示

---

## Phase 4: ViewModel への統合

### WeeklyScheduleViewModel の変更

#### 依存追加

```csharp
private readonly OutlookCalendarService _outlookCalendarService;

public ObservableCollection<OutlookEvent> OutlookEvents { get; } = [];
```

#### LoadWeekAsync の変更

```csharp
// Outlook イベントを並列取得
var settings = _configService.LoadSettings();
IReadOnlyList<OutlookEvent> outlookEvents = [];
if (settings.OutlookCalendarEnabled)
{
    outlookEvents = await _outlookCalendarService.GetEventsForWeekAsync(WeekStart);
}

// UI スレッドで反映
OutlookEvents.Clear();
foreach (var ev in outlookEvents) OutlookEvents.Add(ev);
```

---

## Phase 5: WeekGridControl への描画

### OutlookEvent の描画仕様

Curia ScheduleBlock とは視覚的に明確に区別する。

#### Timed イベント

| 項目 | 仕様 |
|---|---|
| 背景色 | AppSurface2 (グレー系) + Opacity 0.4 |
| 斜線パターン | DrawingBrush で 45度ストライプ |
| 枠線 | AppSubtext0 / 1px |
| タイトル | 斜体 (Italic) / AppSubtext0 |
| 右上アイコン | Outlook アイコン相当の小ラベル "OL" |
| クリック | 無効 (読み取り専用) |
| コンテキストメニュー | なし |

#### All-day イベント

- All-day レーンに Curia AllDay ブロックと同様に帯表示
- 背景色: AppSurface2 + Opacity 0.4 + 斜線
- タイトル: 斜体 / AppSubtext0

### WeekGridControl の変更

- `ViewModel` プロパティ経由で `OutlookEvents` を購読
- `Refresh()` および `LoadWeekAsync` 完了時に `RenderOutlookEvents()` を呼ぶ
- Outlook イベントは専用の Canvas レイヤー `OutlookCanvas` に描画 (ScheduleBlock の下に重ねる)
- ScheduleBlock と OutlookEvent が重なった場合は OutlookEvent を下に表示

---

## Phase 6: DI 登録

### App.xaml.cs への追加

```csharp
services.AddSingleton<OutlookCalendarService>();
```

---

## 未検討事項 / 将来検討

- 複数カレンダーの選択 (どのカレンダーを表示するか選べるように)
- Outlook イベントをクリックして詳細ポップアップ表示
- Outlook イベントから Curia ScheduleBlock への変換 (「この予定にタスクを紐付け」)
- Outlook が起動していない場合の自動起動オプション
- 更新頻度の設定 (現在は週ナビゲーション時のみ再取得)
- Outlook Online (Web 版) のみ使用しているユーザーへの対応 (Graph API フォールバック)
