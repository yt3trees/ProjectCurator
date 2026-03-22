# Pinned Work Folders 機能 - 実装計画書

## 概要

shared フォルダ配下の work フォルダ (yyyyMMdd_xxxxx 等) を「お気に入り (Pin)」として登録し、
Dashboard から素早くアクセスできるようにする機能。

## 方針: ハイブリッド (案D)

- Dashboard のカードエリアと Today Queue の間に「Pinned Folders」セクションを配置
- プロジェクトカード内の workstream 行から ★ ボタンでフォルダを選択してピン追加
- プロジェクトカードのフォルダボタン右クリックメニューから General work のピンも可能
- `_config/pinned_folders.json` に永続化 (hidden_projects.json と同じパターン)

## UI イメージ

```
┌─────────────────────────────────────────────┐
│  [Project Cards ...]                        │
├─────────────────────────────────────────────┤
│  📌 Pinned Folders (3)              [Clear] │
│  ┌────────────────┐ ┌────────────────┐      │
│  │ ProjA / core   │ │ ProjB          │ ... │
│  │ 0321_auth_impl │ │ 0319_fix_bug   │      │
│  │ [Open] [×]     │ │ [Open] [×]     │      │
│  └────────────────┘ └────────────────┘      │
├─────────────────────────────────────────────┤
│  Today Queue                                │
└─────────────────────────────────────────────┘
```

Workstream 行でのピン追加:
```
  core-feature  [2d] 📝3  [📂] [+] [★]
                                       ↑ クリックで直近フォルダ一覧 Popup
```

## データモデル

```csharp
// Models/PinnedFolder.cs
public class PinnedFolder
{
    public string Project { get; set; } = "";       // プロジェクト名
    public string? Workstream { get; set; }          // workstream ID (null = general)
    public string Folder { get; set; } = "";         // フォルダ名 (例: 20260321_auth_impl)
    public string FullPath { get; set; } = "";       // フルパス (存在確認・Open用)
    public string PinnedAt { get; set; } = "";       // ピン日時 (yyyy-MM-dd)
}
```

```jsonc
// _config/pinned_folders.json
[
  {
    "Project": "ProjectA",
    "Workstream": "core-feature",
    "Folder": "20260321_auth_impl",
    "FullPath": "C:\\Users\\...\\ProjectA\\shared\\_work\\core-feature\\202603\\20260321_auth_impl",
    "PinnedAt": "2026-03-21"
  }
]
```

## 実装タスク

- [x] Task 1: 計画書作成 (本ドキュメント)
- [x] Task 2: PinnedFolder モデル追加 (`Models/PinnedFolder.cs`)
- [x] Task 3: ConfigService に `LoadPinnedFolders` / `SavePinnedFolders` 追加
- [x] Task 4: DashboardViewModel にピン管理ロジック追加
  - `PinnedFolders` ObservableCollection
  - `PinFolder(card, workstream, folderName, fullPath)` メソッド
  - `UnpinFolder(pinnedFolder)` メソッド
  - `GetRecentWorkFoldersAsync(projectPath, workstreamId)` メソッド (直近10件)
  - `OpenPinnedFolder(pinnedFolder)` メソッド
  - `ClearPinnedFolders()` メソッド
- [x] Task 5: Dashboard XAML に Pinned Folders セクション追加
  - Grid.Row を 1つ増やす (カードとTodayQueueの間, Row 2)
  - WrapPanel でチップ表示
  - 各チップ: ProjectLabel + フォルダ名 + Open + × ボタン
  - ピンが 0 件なら Collapsed、存在しないフォルダは半透明
- [x] Task 6: Workstream 行に ★ ピンボタン追加 + フォルダ選択ダイアログ
  - WorkstreamCardItem の Grid に Column 3 追加
  - ★ クリックで直近フォルダ一覧を ListBox ダイアログで表示
  - 選択でピン追加
  - プロジェクトカードのフォルダボタン ContextMenu にも "Pin Work Folder..." を追加
- [x] Task 7: DashboardPage.xaml.cs イベントハンドラ実装
  - `ShowPinFolderPickerDialogAsync()` ダイアログヘルパー
  - GridSplitter 行番号修正 (3→4)
- [x] Task 8: ビルド確認 (警告 0 / エラー 0)

## フォルダスキャン仕様

直近のフォルダ一覧取得ロジック:

```
General work:
  {projectPath}/shared/_work/{year}/{yearMonth}/ 配下のディレクトリ

Workstream work:
  {projectPath}/shared/_work/{workstreamId}/{yearMonth}/ 配下のディレクトリ
```

- 最新の yearMonth フォルダから降順で最大 10 件取得
- フォルダ名が `yyyyMMdd_` で始まるものをフィルタ
- 存在しないフォルダのピンは赤字 or 半透明で表示

## 注意事項

- UIスレッドをブロックしない: フォルダスキャンは `Task.Run` で実行
- Singleton ViewModel: ピン状態はページ遷移しても保持される
- wpf-ui コントロール優先: チップには `ui:Card` 等を使用
- 色は `{DynamicResource AppXxx}` で参照
- テキストは英語
