# Command Palette ウィンドウ化 & フォルダ開く機能 仕様書

作成日: 2026-04-10
最終更新: 2026-04-10（実装完了に伴い内容を実態に合わせて更新）

## 概要

Ctrl+K Command Palette を MainWindow 内の UserControl オーバーレイから独立した
Window に作り直し、グローバルホットキー（Ctrl+Shift+K）からもどこからでも起動できる
ようにした。合わせて、各プロジェクトのサブフォルダをエクスプローラーで素早く開く
コマンドを追加した。


## 1. 解決した問題

- MainWindow がバックグラウンドにある状態では Command Palette が使えなかった
- Ctrl+K は MainWindow フォーカス時のみ動作していた
- CaptureWindow（独立 Window）と実装パターンが統一されていなかった


## 2. 実装済みファイル一覧

### 新規作成

- `Views/CommandPaletteWindow.cs` — 独立ウィンドウ実装（code-behind のみ、XAML なし）

### 変更

| ファイル | 変更内容 |
|---|---|
| `Helpers/Win32Interop.cs` | `COMMAND_PALETTE_HOTKEY_ID = 9002` を追加 |
| `Services/HotkeyService.cs` | `OnCommandPaletteActivated`、`CommandPaletteHotkeyRegistered` を追加；WndProc に分岐追加；`Register()` で3本目のホットキーを登録 |
| `Models/AppConfig.cs` | `CommandPaletteHotkey` プロパティ（`HotkeyConfig` 型）を追加。デフォルト: `Ctrl+Shift+K` |
| `MainWindow.xaml.cs` | `_activeCommandPaletteWindow` フィールド追加；`ShowCommandPaletteWindow()` 追加；`BringToFront()` 追加；Ctrl+K ハンドラを変更；`OnCommandPaletteActivated` 設定 |
| `MainWindow.xaml` | CommandPaletteOverlay の埋め込みを削除 |
| `ViewModels/CommandPaletteViewModel.cs` | `IsVisible`/`Show()`/`Hide()` を廃止；`Prepare()` + `ExecuteSelected()` に変更；フォルダコマンド追加；`/` プレフィクスフィルタ追加；不要コマンド削除 |
| `Views/Controls/CommandPaletteOverlay.xaml` | 空スタブに変更（互換性維持） |
| `Views/Controls/CommandPaletteOverlay.xaml.cs` | 空スタブに変更（互換性維持） |


## 3. CommandPaletteWindow 設計

### ウィンドウプロパティ

```
Width = 640
MaxHeight = 520
SizeToContent = SizeToContent.Height
MinHeight = 0
WindowStyle = WindowStyle.None
ResizeMode = ResizeMode.NoResize   ← CanResize から変更（DWM 白枠が出ないため WindowChrome 不要）
Topmost = true
ShowInTaskbar = false
WindowStartupLocation = WindowStartupLocation.Manual
```

WindowChrome は適用しない（AGENTS.md: NoResize ダイアログには不要）。

### 位置計算

検索ボックス行（高さ約 40px）の中心がモニター垂直中央に来るよう配置する。

```csharp
var workArea = SystemParameters.WorkArea;
Left = workArea.Left + (workArea.Width - Width) / 2;
Top  = workArea.Top  + (workArea.Height / 2) - 20;
```

### フォーカス管理

```csharp
// Activated イベントで設定（グローバルホットキー経由でも確実に届く）
Activated += (_, _) =>
{
    _searchBox.Focus();
    Keyboard.Focus(_searchBox);
};
```

`Loaded` ではなく `Activated` を使う理由: グローバルホットキー経由では他アプリから
フォーカスを奪うため、`Loaded` 時点ではまだウィンドウがアクティブになっていない
ケースがある。

### フォーカス喪失時のクローズ

```csharp
// ContentRendered 後にのみ Deactivated でクローズ（Show() 中の一時的な非活性化を誤検知しない）
// BeginInvoke で次フレームに遅延（非活性化メッセージ処理中の再入フリーズを防止）
ContentRendered += (_, _) => _canCloseOnDeactivate = true;
Deactivated += (_, _) =>
{
    if (_canCloseOnDeactivate)
        Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Normal);
};
```

### キー処理

- Window.PreviewKeyDown で Escape を捕捉 → `_canCloseOnDeactivate = false` してから `Close()`
- TextBox.PreviewKeyDown で Enter / Down / Up を処理
- ダブルクリックでも実行

### ExecuteAndClose パターン

```csharp
private void ExecuteAndClose()
{
    _canCloseOnDeactivate = false; // Deactivated による二重クローズを防ぐ
    _viewModel.ExecuteSelected(cmd =>
    {
        Close();
        cmd.Action?.Invoke(_mainWindow);
    });
}
```

### UI 構造

```
Grid (root)
  Row 0: Border[AppBackground]  ← 検索ボックス行（padding 8px）
    TextBox (FontSize=15, BorderThickness=0)
  Row 1: Border[AppSurface1]    ← セパレータ (Height=1)
  Row 2: ListBox[AppSurface0]   ← コマンドリスト (MaxHeight=400, padding 4px上下)
  [全行スパン] Border (IsHitTestVisible=false, AppSurface2 1px枠)
Window.Background = AppSurface0
```

### ListBox ItemContainerStyle

名前付き要素参照（"Bd"）は code-behind の ControlTemplate では動作しないため、
`TemplateBinding` で Border.Background を Control.Background に連結し、
トリガーは Style レベルで `ListBoxItem.Background` を変更するパターンを使う。

```csharp
// ControlTemplate: Border → TemplateBinding(Control.Background)
// Style triggers:
//   IsSelected=true  → Background = AppSurface1
//   IsMouseOver=true → Background = AppSurface2
```


## 4. CommandPaletteViewModel の変更点

### 廃止

- `IsVisible` プロパティ（Window の Show/Close で管理するため不要）
- `Show()` / `Hide()` メソッド
- `ExecuteCommand(MainWindow window)` [RelayCommand]

### 追加

```csharp
public void Prepare()          // BuildCommands() + SearchText リセット + フィルタ更新
public void ExecuteSelected(Action<CommandItem> executor)  // Window 側からコールバック実行
```

### コマンド一覧（実装済み）

**[Tab] タブ切り替え**（プレフィクスなし）
- 実行時に `MainWindow.BringToFront()` を呼んでアプリを前面に出す

**[>] プロジェクトコマンド**（`>` で絞り込み）
- `check {name}` — Setup の Check タブを開く
- `edit {name}` — Editor でプロジェクトを開く
- `term {name}` — ターミナルをプロジェクトルートで開く
- `timeline {name}` — Timeline でプロジェクトを開く
- `resume {name}` — 作業フォルダを作成してエクスプローラー＋ターミナルを開く

非表示にしたコマンド（削除済み）: `update focus` / `briefing` / `meeting` / `standup`

**[@] エディタショートカット**（`@` で絞り込み）
- `{name}` — Editor でプロジェクトを開く

**[dir] フォルダを開く**（`/` で絞り込み）
- `dir {name}` — プロジェクトルート
- `dir docs {name}` — `shared/docs/`（存在する場合のみ登録）
- `dir work {name}` — `shared/_work/`（存在する場合のみ登録）
- `dir develop {name}` — `development/source/`（存在する場合のみ登録）
- `dir shared {name}` — `shared/`（存在する場合のみ登録）
- `dir ai {name}` — `_ai-context/`（存在する場合のみ登録）

検索は AND トークンマッチ。例: `dir doc mrnf` → `dir docs 202504_MRNF` にマッチ。


## 5. MainWindow の追加メソッド

```csharp
// グローバルホットキー・Ctrl+K 共通のエントリポイント
public void ShowCommandPaletteWindow()

// タブ切り替えコマンド実行後にアプリを前面に出す
public void BringToFront()
// - 非表示なら MoveOnScreen()
// - 表示中なら Activate() / Focus()
```


## 6. 既知のハマりポイント（実装時の学び）

- `ControlTemplate` のトリガーで名前付き要素 ("Bd") を参照する `Setter` は
  code-behind では動作しない（`InvalidOperationException`）。Style レベルトリガーを使う。
- `WindowChrome` を `ResizeMode.NoResize` ウィンドウに適用すると `SizeToContent` が
  正しく機能しない（AGENTS.md 記載済み）。
- `Deactivated` で直接 `Close()` を呼ぶと、`Show()` 中の活性化シーケンスで
  `InvalidOperationException` が発生する。`ContentRendered` フラグ + `BeginInvoke` で回避。
- `Deactivated` で `Close()` を `BeginInvoke` しても、Escape など別経路でも
  `Close()` が走ると二重クローズになる。`_canCloseOnDeactivate = false` で防ぐ。
- グローバルホットキー経由では `Loaded` 時点でキーボードフォーカスが届かないことがある。
  `Activated` イベント + `Keyboard.Focus()` の組み合わせで解決。


## 7. 今後の拡張（未実装）

- Settings ページで `CommandPaletteHotkey` を UI から変更できるようにする
- `dir recent {name}` → 直近の `_work` サブフォルダを開く
- Command Palette からファイル検索（ファジーマッチ）
- 非表示コマンド（update focus / briefing / meeting / standup）を設定で表示/非表示切り替え
