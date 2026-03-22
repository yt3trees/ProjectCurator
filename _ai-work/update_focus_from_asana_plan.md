# Update Focus from Asana - アプリ内実装計画

アプリ (ProjectCurator) 内から OpenAI / Azure OpenAI API を使い、Asana タスクに基づく `current_focus.md` の更新提案を生成・適用する機能。

SKILL.md の全フローをアプリ内で再現し、Codex CLI / Claude Code を不要にする。

## 方針

- Asana タスクの解析 (進行中/完了の分類) はルールベースで実装
- LLM は更新提案の文章生成 (差分テキスト作成) に使用
- OpenAI API と Azure OpenAI (Microsoft Foundry) の両方をサポート
- API キーは既存の `_config/settings.json` に追加
- general / shared_work (workstream) モードの両方に対応

## UI 設計

### エントリーポイント

1. Editor ツールバーにボタン追加 (プロジェクト選択済みの時のみ有効)
2. Command Palette (Ctrl+K) にコマンド追加

### ダイアログフロー

```
[Editor ツールバー "Update Focus" ボタン]
  ↓
[Workstream 選択ダイアログ] (候補が複数ある場合のみ)
  ↓
[バックアップ処理] (自動・通知のみ)
  ↓
[更新提案ダイアログ]
  - 左: 現在の current_focus.md
  - 右: 提案内容 (diff 形式)
  - 下部: [適用] [修正して適用] [スキップ] ボタン
  ↓
[Editor で current_focus.md を自動オープン]
```

ダイアログは DashboardPage.xaml.cs の既存ポップアップパターン (ダークモード Window) を踏襲する。

## 実装タスク

### Phase 1: 設定と API クライアント基盤

- [ ] 1-1. `AppSettings` に LLM API 設定を追加
  - `LlmProvider`: `"openai"` | `"azure_openai"` (デフォルト: `"openai"`)
  - `LlmApiKey`: API キー
  - `LlmModel`: モデル名 (デフォルト: `"gpt-4o"`)
  - `LlmEndpoint`: Azure OpenAI の場合のエンドポイント URL
  - `LlmApiVersion`: Azure OpenAI の API バージョン (デフォルト: `"2024-12-01-preview"`)
  - ファイル: `Models/AppConfig.cs`

- [ ] 1-2. `_config/settings.json.example` に LLM 設定のサンプルを追加
  - ファイル: `_config/settings.json.example`

- [ ] 1-3. `LlmClientService` を新規作成
  - OpenAI / Azure OpenAI の両方に対応する Chat Completion クライアント
  - `HttpClient` ベースで SDK 依存なし (NuGet 追加を最小化)
  - メソッド: `Task<string> ChatCompletionAsync(string systemPrompt, string userPrompt, CancellationToken ct)`
  - provider 判定は `AppSettings.LlmProvider` から
  - ファイル: `Services/LlmClientService.cs`

- [ ] 1-4. `App.xaml.cs` に `LlmClientService` をシングルトン登録
  - ファイル: `App.xaml.cs`

### Phase 2: Asana タスク解析 (ルールベース)

- [ ] 2-1. `AsanaTaskParser` を新規作成
  - `asana-tasks.md` を読み込みタスクリストを返す
  - 各タスクの属性: タイトル、ID、ステータス (進行中/完了/未着手)、優先度、担当区分 ([担当]/[コラボ])、期日
  - 進行中判定: `🔄` マーク、または `- [ ]` で優先度 High/最高
  - 完了判定: `✅` マーク、または `- [x]`
  - [コラボ] タスクのフィルタリングルール実装
  - ファイル: `Services/AsanaTaskParser.cs`

- [ ] 2-2. `AsanaTaskParser` のモデルクラスを作成
  - `ParsedAsanaTask`: タスク1件の解析結果
  - `AsanaTaskParseResult`: 解析結果全体 (進行中リスト、完了リスト、コラボリスト)
  - ファイル: `Models/AsanaTaskModels.cs`

### Phase 3: Focus 更新ロジック (コアサービス)

- [ ] 3-1. `FocusUpdateService` を新規作成
  - SKILL.md の Step 1-6 のオーケストレーション
  - メソッド: `Task<FocusUpdateResult> GenerateProposalAsync(ProjectInfo project, string? workstreamId, CancellationToken ct)`
  - 内部処理:
    1. パス解決 (general / workstream)
    2. ファイル存在チェック
    3. バックアップ作成
    4. `AsanaTaskParser` で Asana タスク解析
    5. `current_focus.md` 読み込み
    6. LLM に更新提案を生成させる
  - ファイル: `Services/FocusUpdateService.cs`

- [ ] 3-2. `FocusUpdateResult` モデルを作成
  - `CurrentContent`: 現在の current_focus.md 内容
  - `ProposedContent`: 提案された更新内容
  - `BackupPath`: バックアップファイルのパス
  - `BackupStatus`: 新規作成 / 既存スキップ
  - `TargetFocusPath`: 更新対象の current_focus.md パス
  - `WorkMode`: general / shared_work
  - `Summary`: 変更サマリ (ダイアログ表示用)
  - ファイル: `Models/FocusUpdateModels.cs`

- [ ] 3-3. LLM プロンプトの設計
  - System Prompt: 更新ルール (既存行の保持、完了タスクは [完了] マーク提案のみ、等)
  - User Prompt: 現在の `current_focus.md` + 解析済み Asana タスクリスト
  - 出力フォーマット: 更新後の `current_focus.md` 全文
  - プロンプトテンプレートは埋め込みリソースまたは定数として管理
  - ファイル: `Services/FocusUpdateService.cs` 内に定義

- [ ] 3-4. `App.xaml.cs` に `FocusUpdateService` をシングルトン登録
  - ファイル: `App.xaml.cs`

### Phase 4: UI - Editor ツールバーボタン

- [ ] 4-1. Editor ツールバーに "Update Focus" ボタン追加
  - アイコン: wpf-ui の `SymbolRegular` から適切なものを選択 (例: `ArrowSync24`)
  - プロジェクト未選択時は無効化 (IsEnabled バインディング)
  - ToolTip: "Update Focus from Asana"
  - ファイル: `Views/Pages/EditorPage.xaml`

- [ ] 4-2. `EditorViewModel` に `UpdateFocusCommand` を追加
  - `[RelayCommand]` で実装
  - `CanExecute`: `SelectedProject != null`
  - 処理: Workstream 判定 → `FocusUpdateService` 呼び出し → ダイアログ表示
  - ファイル: `ViewModels/EditorViewModel.cs`

### Phase 5: UI - 更新提案ダイアログ

- [ ] 5-1. Workstream 選択ダイアログの実装
  - 候補が 0 件: general モードで自動続行
  - 候補が 1 件: その workstream で自動続行
  - 候補が複数: リスト表示して選択 + "General (プロジェクト全体)" オプション
  - DashboardPage.xaml.cs のポップアップパターンを踏襲
  - ファイル: `Views/Pages/EditorPage.xaml.cs` (コードビハインドでダイアログ生成)

- [ ] 5-2. 更新提案ダイアログの実装
  - 左右分割レイアウト (現在 / 提案)
  - 変更サマリ表示 (上部)
  - バックアップステータス表示
  - ボタン: [適用] [修正して適用] [スキップ]
  - ダークモード対応 (既存テーマリソース使用)
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

- [ ] 5-3. "修正して適用" フローの実装
  - 提案内容を Editor に読み込み、ユーザーが編集 → 保存で適用
  - または提案ダイアログ内で直接編集可能な TextBox にする
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

- [ ] 5-4. 適用後に Editor で `current_focus.md` を自動オープン
  - 既存の `NavigateToEditorAndOpenFile` パターンを活用
  - 更新日付 (`更新: YYYY-MM-DD`) の自動更新
  - ファイル: `ViewModels/EditorViewModel.cs`

### Phase 6: UI - Command Palette 統合

- [ ] 6-1. Command Palette に "Update Focus from Asana" コマンドを追加
  - 既存の `CommandItem` モデルを使用
  - 実行時は Editor ページに遷移 → UpdateFocusCommand を発火
  - ファイル: コマンド登録箇所 (要確認: `CommandPaletteOverlay` 関連)

### Phase 7: Settings UI

- [ ] 7-1. Settings ページに LLM 設定セクションを追加
  - Provider 選択 (OpenAI / Azure OpenAI)
  - API Key 入力 (PasswordBox)
  - Model 名入力
  - Endpoint 入力 (Azure の場合のみ表示)
  - API Version 入力 (Azure の場合のみ表示)
  - 接続テストボタン
  - ファイル: `Views/Pages/SettingsPage.xaml`, `ViewModels/SettingsViewModel.cs`

### Phase 8: エラーハンドリングとエッジケース

- [ ] 8-1. API キー未設定時のガイダンス表示
  - ボタン押下時に Settings ページへの誘導メッセージ
  - ファイル: `ViewModels/EditorViewModel.cs`

- [ ] 8-2. `asana-tasks.md` 不在時のエラーメッセージ
  - SKILL.md に準拠したメッセージ表示
  - ファイル: `Services/FocusUpdateService.cs`

- [ ] 8-3. `current_focus.md` 不在時のエラーメッセージ
  - SKILL.md に準拠したメッセージ表示
  - ファイル: `Services/FocusUpdateService.cs`

- [ ] 8-4. LLM API 呼び出し失敗時のリトライ/エラー表示
  - タイムアウト、レート制限、認証エラーの個別ハンドリング
  - ファイル: `Services/LlmClientService.cs`

- [ ] 8-5. 処理中のローディング表示
  - Editor ページの既存ローディングスピナーを活用
  - ファイル: `Views/Pages/EditorPage.xaml.cs`

## ファイル追加/変更一覧

### 新規ファイル
| ファイル | 説明 |
|---|---|
| `Services/LlmClientService.cs` | OpenAI / Azure OpenAI API クライアント |
| `Services/AsanaTaskParser.cs` | asana-tasks.md のルールベース解析 |
| `Services/FocusUpdateService.cs` | Focus 更新オーケストレーション |
| `Models/AsanaTaskModels.cs` | Asana タスク解析のデータモデル |
| `Models/FocusUpdateModels.cs` | Focus 更新のデータモデル |

### 変更ファイル
| ファイル | 変更内容 |
|---|---|
| `Models/AppConfig.cs` | `AppSettings` に LLM 設定プロパティ追加 |
| `App.xaml.cs` | DI 登録追加 |
| `Views/Pages/EditorPage.xaml` | ツールバーにボタン追加 |
| `Views/Pages/EditorPage.xaml.cs` | ダイアログ実装 |
| `ViewModels/EditorViewModel.cs` | `UpdateFocusCommand` 追加 |
| `Views/Pages/SettingsPage.xaml` | LLM 設定セクション追加 |
| `ViewModels/SettingsViewModel.cs` | LLM 設定バインディング追加 |
| `_config/settings.json.example` | LLM 設定サンプル追加 |
| Command Palette 関連 | コマンド追加 |

## 技術的な決定事項

- NuGet 追加なし: `HttpClient` で OpenAI REST API を直接呼ぶ (既存パターンと統一)
- プロンプトは C# 定数として管理 (外部ファイル不要)
- LLM のレスポンスは `current_focus.md` の全文を返す形式 (部分パッチではなく全文置換)

## 実装順序の推奨

Phase 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8

Phase 1-3 がバックエンド、Phase 4-7 がフロントエンド。
Phase 8 は各フェーズと並行して進めてもよい。
