# ProjectCurator Avalonia UI 移行仕様書

作成日: 2026-03-28

## 概要

ProjectCuratorをWPF (.NET 9, Windows専用) からAvalonia UIへ移行し、macOSクロスプラットフォーム対応を実現する。

## プロジェクト構成

```
ProjectCurator.sln
  +-- ProjectCurator.Core/         (net9.0, UI非依存の共有ライブラリ)
  +-- ProjectCurator.Desktop/      (net9.0, Avalonia UIホスト)
  +-- ProjectCurator.WPF/          (既存WPFアプリ、参照用に保持)
```

## NuGetパッケージ対応表

| 現在 (WPF) | Avalonia置換 | 備考 |
|---|---|---|
| WPF-UI 3.x | FluentAvalonia 2.x | Fluent Designコントロール |
| AvalonEdit 6.x | AvaloniaEdit 11.x | AvalonEditの公式Avalonia移植版 |
| DiffPlex 1.x | DiffPlex 1.x | UI非依存のため変更不要 |
| CommunityToolkit.Mvvm 8.x | CommunityToolkit.Mvvm 8.x | Avalonia対応済み |
| Microsoft.Extensions.DI 9.x | Microsoft.Extensions.DI 9.x | フレームワーク非依存 |
| System.Windows.Forms (NotifyIcon) | Avalonia組み込みTrayIcon | macOSはNSStatusItemにマップ |
| (新規追加) | SharpHook | クロスプラットフォームグローバルホットキー |

---

## Phase 0: 基盤構築 (Coreライブラリ分離・インターフェース定義)

### プラットフォーム抽象化インターフェース定義

- [x] `IDispatcherService` - UIスレッドディスパッチ抽象化 (Post, Invoke, InvokeAsync)
- [x] `IShellService` - OS操作抽象化 (OpenFolder, OpenFile, OpenTerminal, CreateSymlink, IsSymlink, ResolveSymlinkTarget, RunShellScriptAsync, SetStartupEnabled, IsStartupEnabled)
- [x] `IDialogService` - ダイアログ抽象化 (ShowMessageAsync, ShowConfirmAsync)
- [x] `ITrayService` - トレイアイコン抽象化 (Initialize, UpdateHotkeyDisplay, ShowNotification)
- [x] `IHotkeyService` - ホットキー抽象化 (既存インターフェースをCoreへ移動)

### Core プロジェクト作成

- [x] `ProjectCurator.Core.csproj` 作成 (net9.0, UI参照なし)
- [x] `ProjectCurator.sln` にCoreプロジェクト追加

### Models移動 (15ファイル、変更不要)

- [x] AppConfig.cs
- [x] AsanaTaskModels.cs
- [x] CaptureModels.cs
- [x] CommandItem.cs
- [x] CuratorStateSnapshot.cs
- [x] DecisionLogModels.cs
- [x] EditorState.cs
- [x] FileUpdateProposal.cs
- [x] FocusUpdateModels.cs
- [x] MeetingNotesModels.cs
- [x] Messages.cs
- [x] PinnedFolder.cs
- [x] ProjectCheckResult.cs
- [x] ProjectInfo.cs
- [x] WorkstreamInfo.cs

### Helpers移動

- [x] EncodingDetector.cs (変更不要)

### Services移動 - プラットフォーム非依存 (そのまま移動)

- [x] LlmClientService.cs
- [x] AsanaSyncService.cs
- [x] AsanaTaskParser.cs
- [x] TodayQueueService.cs
- [x] StandupGeneratorService.cs
- [x] FocusUpdateService.cs
- [x] DecisionLogGeneratorService.cs
- [x] MeetingNotesService.cs
- [x] StateSnapshotService.cs
- [x] FileEncodingService.cs
- [x] ConfigService.cs
- [x] CaptureService.cs

### Services移動 - 要リファクタリング

- [x] ScriptRunnerService.cs - Dispatcher呼び出しを`IDispatcherService`へ、`powershell.exe`を`IShellService`へ置換
- [x] ContextCompressionLayerService.cs - Junction作成を`IShellService.CreateSymlink`へ置換
- [x] ProjectDiscoveryService.cs - Junction検出を`IShellService.IsSymlink`へ置換
- [ ] PageService.cs - ナビゲーション抽象化の検討

### ViewModels移動 - 要リファクタリング

- [x] MainWindowViewModel.cs (変更なし)
- [x] DashboardViewModel.cs - `explorer.exe`を`IShellService.OpenFolder`へ、`Dispatcher`を`IDispatcherService`へ、`MessageBox`を`IDialogService`へ
- [x] EditorViewModel.cs - `MessageBox`呼び出しを`IDialogService`へ
- [x] TimelineViewModel.cs (軽微な変更)
- [x] GitReposViewModel.cs - `Process.Start`呼び出しを`IShellService`へ
- [x] AsanaSyncViewModel.cs - `Dispatcher.Invoke`を`IDispatcherService`へ
- [x] SetupViewModel.cs - `MessageBox`を`IDialogService`へ
- [x] SettingsViewModel.cs - Startup shortcutを`IShellService.SetStartupEnabled`へ、`WScript.Shell` COM除去
- [x] CommandPaletteViewModel.cs - `MessageBox`・Process起動を抽象化

### Phase 0 検証

- [x] `ProjectCurator.Core`が単体でビルド成功 (WPF参照なし)
- [x] 既存WPFプロジェクトがCoreを参照して正常動作

---

## Phase 1: Avaloniaシェル構築

### プロジェクト作成

- [x] `ProjectCurator.Desktop.csproj` 作成 (net9.0, Avalonia)
- [x] NuGet追加: Avalonia 11.x, Avalonia.Desktop 11.x, FluentAvalonia 2.x, SharpHook
- [x] Core プロジェクト参照追加

### アプリケーション基盤

- [x] `App.axaml` - FluentAvaloniaTheme適用 (ダークモード強制)、TrayIcon定義
- [x] `App.axaml.cs` - DI コンテナ設定 (既存App.xaml.csから移植)、シングルインスタンスMutex
- [ ] テーマリソース: `GitHubDark.axaml` (既存GitHubDark.xamlから変換)
- [ ] テーマリソース: `CatppuccinMocha.axaml` (既存CatppuccinMocha.xamlから変換)

### MainWindow

- [x] `MainWindow.axaml` - FluentAvalonia NavigationView、ステータスバー
- [x] `MainWindow.axaml.cs` - ウィンドウ表示/非表示トグル、キーボードショートカット (Ctrl+1-7, Ctrl+K, Escape)
- [x] DWMフラッシュ回避ハック不要 (Avaloniaのレンダリングでは発生しない見込み)

### プラットフォームサービス実装 (Windows)

- [x] `AvaloniaDispatcherService.cs` - `Avalonia.Threading.Dispatcher.UIThread`使用
- [x] `AvaloniaDialogService.cs` - FluentAvalonia ContentDialog使用
- [x] `WindowsHotkeyService.cs` - 既存Win32Interop再利用、`TryGetPlatformHandle()`でHWND取得
- [x] `WindowsShellService.cs` (スタブ、Phase 3で完成)
- [x] `WindowsTrayService.cs` (Avalonia TrayIcon使用)

### プラットフォームサービス実装 (macOS スタブ)

- [x] `MacOSHotkeyService.cs` (No-opスタブ)
- [x] `MacOSShellService.cs` (スタブ)
- [x] `MacOSTrayService.cs` (Avalonia TrayIcon使用)

### SettingsPage (概念実証)

- [x] `SettingsPage.axaml` - WPF版から変換 (フォーム系のみ、最もシンプル)
- [x] `SettingsPage.axaml.cs` - データバインディング・テーマ検証

### Phase 1 検証

- [ ] Avaloniaアプリ起動、MainWindowレンダリング
- [ ] NavigationViewサイドバー動作
- [ ] SettingsPage表示・設定保存
- [ ] トレイアイコン表示
- [ ] Ctrl+Shift+Pホットキー動作 (Windows)

---

## Phase 2: ページ移行 (難易度順)

### 2-1. AsanaSyncPage (最も単純: 304 XAML行, 29 CS行)

- [x] `AsanaSyncPage.axaml` - XAML変換 (xmlns, Visibility->IsVisible, コントロール置換)
- [x] `AsanaSyncPage.axaml.cs` - コードビハインド移植
- [ ] 動作確認: Asana同期実行、ステータス表示

### 2-2. GitReposPage (176 XAML行, 174 CS行)

- [x] `GitReposPage.axaml` - テーブル/リスト表示、ボタン
- [x] `GitReposPage.axaml.cs` - `explorer.exe`/PowerShell呼び出しを`IShellService`経由に
- [ ] 動作確認: リポジトリ一覧、フォルダ開く、ターミナル起動

### 2-3. SetupPage (490 XAML行, 42 CS行)

- [x] `SetupPage.axaml` - フォーム・出力ログ
- [x] `SetupPage.axaml.cs` - コードビハインド移植
- [ ] 動作確認: プロジェクトセットアップ、バリデーション

### 2-4. TimelinePage (452 XAML行, 109 CS行)

- [x] `TimelinePage.axaml` - リスト・ヒートマップ
- [x] `HeatmapIntensityToBrushConverter` Avalonia版作成
- [x] `TimelinePage.axaml.cs` - コードビハインド移植
- [ ] 動作確認: タイムライン表示、ヒートマップレンダリング

### 2-5. DashboardPage (最大・最複雑: 970 XAML行, 4417 CS行)

- [x] `DashboardPage.axaml` - メインレイアウト変換
- [ ] プログラマティックUI構築のAvalonia対応:
  - [ ] コンテキストメニュー (System.Windows.Controls.ContextMenu -> Avalonia.Controls.ContextMenu)
  - [ ] 動的MenuItem作成
  - [ ] ポップアップダイアログ群 (AI Briefing, フォルダ作成 etc.) をAXAMLベースに書き直し
- [x] `DashboardPage.axaml.cs` - コードビハインド移植 (基本実装)
- [ ] 動作確認: プロジェクト一覧、Today Queue、ピン留めフォルダ、全ポップアップ

### 2-6. EditorPage (297 XAML行, 2923 CS行)

- [x] `EditorPage.axaml` - AvaloniaEdit TextEditorへ置換
- [x] AvaloniaEdit統合:
  - [x] Markdown.xshd 構文ハイライト適用 (AvaloniaEditは.xshd互換)
  - [x] `DiffLineBackgroundRenderer` Avalonia IBackgroundRenderer版に移植
  - [ ] SearchPanel統合
- [x] `EditorPage.axaml.cs` - コードビハインド移植
- [ ] `ProposalReviewDialog` Avalonia版作成
- [ ] 動作確認: ファイル読み込み・編集・保存、Diff表示、構文ハイライト

### 2-7. CommandPaletteOverlay (81 XAML行)

- [x] `CommandPaletteOverlay.axaml` - オーバーレイコントロール
- [x] `CommandPaletteOverlay.axaml.cs` - キーボード操作
- [ ] 動作確認: Ctrl+K起動、コマンド検索・実行

### 2-8. CaptureWindow (1488 CS行、XAMLなし -> AXAML新規作成)

- [x] `CaptureWindow.axaml` 新規作成 (コードビハインドUI構築をAXAMLへ)
- [x] 画面状態 (Input, Loading, Confirm, TaskApproval, Complete) をVisibility切替パネルで実装
- [x] `CaptureWindow.axaml.cs` - コードビハインド
- [ ] 動作確認: クイックキャプチャ全フロー

### Converters (Avalonia版)

- [x] InverseBoolConverter
- [x] DirtyConverter
- [x] ShowAllLabelConverter
- [ ] DirIconConverter (FluentAvalonia Symbol対応)
- [x] HeatmapIntensityToBrushConverter (Avalonia IBrush/Color対応)
- [x] その他既存Converterの移植

### Phase 2 検証

- [ ] 全7ページ+2ウィンドウ+1オーバーレイがナビゲーション・表示正常
- [ ] 全データバインディング動作
- [ ] ダークテーマ適用
- [ ] キーボードショートカット (ページ固有+グローバル)

---

## Phase 3: 統合・全機能接続

### WindowsShellService 完成

- [x] `OpenFolder` - `Process.Start("explorer.exe", path)`
- [x] `OpenFile` - `UseShellExecute = true`
- [x] `OpenTerminal` - `wt.exe` / `pwsh.exe` フォールバック
- [x] `CreateSymlink` - PowerShell `New-Item -ItemType Junction`
- [x] `IsSymlink` / `ResolveSymlinkTarget` - `FileAttributes.ReparsePoint`
- [x] `RunShellScriptAsync` - PowerShell/Python スクリプト実行
- [x] `SetStartupEnabled` / `IsStartupEnabled` - `.lnk` ショートカット作成 (WScript.Shell)

### MacOSShellService 完成

- [x] `OpenFolder` - `Process.Start("open", path)`
- [x] `OpenFile` - `Process.Start("open", path)`
- [x] `OpenTerminal` - `Process.Start("open", "-a Terminal path")`
- [x] `CreateSymlink` - `Directory.CreateSymbolicLink()` (.NET 7+ API)
- [x] `IsSymlink` / `ResolveSymlinkTarget` - `FileInfo.LinkTarget`
- [x] `RunShellScriptAsync` - `/bin/bash -c` or `/bin/zsh -c`
- [x] `SetStartupEnabled` / `IsStartupEnabled` - LaunchAgent plist (`~/Library/LaunchAgents/`)

### MacOSHotkeyService 完成

- [x] SharpHook によるグローバルキーボードフック実装
- [x] Cmd+Shift+P / Cmd+Shift+C (macOS慣習に合わせCtrl->Cmd)
- [x] Accessibilityパーミッション要求ダイアログ

### トレイアイコン統合

- [ ] ダイヤモンドアイコン生成 (Avalonia DrawingContext / RenderTargetBitmap)
- [x] コンテキストメニュー: Show, Quick Capture, ホットキー表示, Exit
- [ ] macOS NSStatusItemへの正常マッピング確認

### ウィンドウ管理

- [x] 表示/非表示トグル (トレイ・ホットキー両方)
- [x] ウィンドウ位置保存/復元 (ConfigService)
- [x] シングルインスタンス強制

### キーボードショートカット全体確認

- [x] Ctrl+1-7 ページナビゲーション
- [x] Ctrl+K コマンドパレット
- [x] Escape ウィンドウ非表示
- [x] Ctrl+S エディタ保存
- [x] グローバルホットキー (Ctrl+Shift+P, Ctrl+Shift+C)

### Phase 3 検証

- [ ] Windows上でエンドツーエンド全機能動作確認
- [ ] Junction/シンボリックリンク作成
- [ ] ファイルエクスプローラー起動
- [ ] ターミナル起動
- [ ] AI機能 (LLM呼び出し)
- [ ] Asana同期
- [ ] コマンドパレット全コマンド

---

## Phase 4: macOSテスト・調整

### ビルド・起動

- [ ] macOS上でビルド成功
- [ ] アプリ正常起動

### プラットフォーム固有修正

- [ ] パスセパレータ問題修正 (必要に応じて)
- [ ] シンボリックリンク作成/検出 (APFSシンボリックリンク)
- [ ] ターミナル起動 (`open -a Terminal`)
- [ ] SharpHookグローバルホットキー動作 (Accessibilityパーミッション)
- [ ] トレイアイコン (NSStatusItem) レンダリング
- [ ] Cmd+キー変換 (macOSユーザーはCmd使用を期待)
- [ ] macOSパス慣習 (`~/Documents/Projects/`)
- [ ] `Environment.ExpandEnvironmentVariables`で`~`が展開されない問題対応
- [ ] フォント対応 (Consolasなし -> `"Menlo, Consolas, 'Courier New', monospace"` フォールバック)
- [ ] LaunchAgentスタートアップ登録テスト

### 全ページ・ダイアログmacOS動作確認

- [ ] DashboardPage
- [ ] EditorPage
- [ ] TimelinePage
- [ ] GitReposPage
- [ ] AsanaSyncPage
- [ ] SetupPage
- [ ] SettingsPage
- [ ] CaptureWindow
- [ ] CommandPaletteOverlay
- [ ] ProposalReviewDialog

### Phase 4 検証

- [ ] macOSで全コア機能動作
- [ ] トレイアイコン、ホットキー、シンボリックリンク、ターミナル起動すべて動作

---

## Phase 5: WPF廃止・クリーンアップ

### プロジェクト整理

- [ ] `ProjectCurator.WPF`をソリューションから除外 (git履歴に保持)
- [ ] `GlobalUsings.cs` 削除 (WPF/WinForms競合解決が不要に)
- [x] `Helpers/Win32Interop.cs` をCoreからWindowsプラットフォーム実装へ移動

### パブリッシュ設定

- [x] `publish.cmd` 更新: `dotnet publish -r win-x64`
- [x] macOSパブリッシュスクリプト: `dotnet publish -r osx-arm64` (Apple Silicon)
- [x] macOSパブリッシュスクリプト: `dotnet publish -r osx-x64` (Intel)
- [ ] macOS `.app` バンドル構成

### ドキュメント更新

- [ ] README更新 (クロスプラットフォームインストール手順)
- [ ] AGENTS.md更新 (Avaloniaアーキテクチャ反映)

### Phase 5 検証

- [x] クリーンビルド成功 (WPF参照なし)
- [ ] Windowsパブリッシュ成功・動作確認
- [ ] macOSパブリッシュ成功・動作確認

---

## リスク一覧

| リスク | 影響度 | 対象ファイル | 緩和策 |
|---|---|---|---|
| DashboardPage.xaml.cs (4417行) のUI構築 | 高 | DashboardPage.xaml.cs | ダイアログをAXAMLベースに分割再設計 |
| DiffLineBackgroundRenderer移植 | 中 | EditorPage.xaml.cs | AvaloniaEditのIBackgroundRenderer APIに合わせて書き直し |
| macOSグローバルホットキー | 中 | MacOSHotkeyService.cs | SharpHook使用 + Accessibility権限プロンプト + フォールバック(トレイクリック) |
| CaptureWindow (1488行, XAMLなし) | 中 | CaptureWindow.cs | AXAMLベースで再設計 (改善機会) |
| NTFS Junction vs macOS symlink差異 | 低-中 | ContextCompressionLayerService.cs | .NET 7+ `Directory.CreateSymbolicLink()` + Windows側はJunction維持 |
| フォント非互換 (Consolas) | 低 | 全ページ | フォールバックチェーン: `"Menlo, Consolas, 'Courier New', monospace"` |
