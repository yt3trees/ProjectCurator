# DecisionLog Viewer 仕様書

- 作成日: 2026-04-04
- ステータス: Draft

## 概要

Dashboard上のプロジェクトカードおよびWorkstreamカードに表示されている「DecisionLog件数 (📝 ◯)」アイコンをクリックした際、そのプロジェクト（またはWorkstream）に紐づくDecisionLogの一覧を確認できる専用の画面（ダイアログ）を開く機能を追加する。

## 背景

現状、DashboardにはDecisionLogの「件数」が表示されているのみであり、どのような意思決定が行われたかを確認するためにはエクスプローラー等で該当フォルダを開くか、Editorでファイルを探す必要があり手間がかかっている。
一覧画面を直接開くことで、素早いコンテキストの確認と意思決定の振り返りを可能にする。

## 画面イメージ

```text
+-------------------------------------------------------------+
| 📝 DecisionLogs - [プロジェクト名 / Workstream名]           [X] |
+-------------------------------------------------------------+
|                                                             |
|  [ 日付降順 ▼ ]                                                 |
|                                                             |
|  +-------------------------------------------------------+  |
|  | [2026-04-04] [Confirmed] [Solo decision]              |  |
|  | .NET採用方針の確定                                    |  |
|  |                                                       |  |
|  | Chosen: Option A: .NET（C#/WPF など）を採用して進める |  |
|  | Why: 現時点の作業内容が C# WPF への移行設計と...      |  |
|  |                                                       |  |
|  | [ Editorで開く ]                                      |  |
|  +-------------------------------------------------------+  |
|                                                             |
|  +-------------------------------------------------------+  |
|  | [2026-04-01] [Tentative] [Meeting]                    |  |
|  | ○○機能のUI改修                                        |  |
|  |                                                       |  |
|  | Chosen: Dashboardにボタンを追加する                   |  |
|  | Why: アクセス性を向上させるため。                     |  |
|  |                                                       |  |
|  | [ Editorで開く ]                                      |  |
|  +-------------------------------------------------------+  |
|                                                             |
|  ...                                                        |
+-------------------------------------------------------------+
```

## 機能仕様

### 1. エントリーポイント
- Dashboardのプロジェクトカード内、およびWorkstreamリスト内の `DecisionLogCount` 表示部分（例: `📝 3`）をクリッカブル（`<Button>` 等）に変更する。
- ユーザーがここをクリックした際、対象となるプロジェクトまたはWorkstreamの情報を引数として `DecisionLogViewerDialog` を開く。件数が0件の場合はクリック無効とするか、空リストのダイアログを表示する。

### 2. データ取得
- 対象となる `_ai-context/decision_log` （または該当するパス）ディレクトリを走査し、Markdownファイルをパースする。
- ファイル形式に対応した以下の情報を抽出する:
  - **タイトル**: `# Decision: {Title}`
  - **メタデータ**: `> Date:`, `> Status:`, `> Trigger:`
  - **決定事項**: `## Chosen` の内容
  - **理由など**: `## Why` または `## Context` のプレビュー

### 3. ダイアログのUI / UX
- **リスト表示:** 取得したDecisionLogをリスト形式で表示。日付・ステータス（バッジ）・トリガー・タイトル・決定事項（Chosen）・理由（Whyサマリー）を含める。
- **ソート機能:** デフォルトでDateプロパティの降順とする。
- **Editor連携:** 各アイテムに「Editorで開く」ボタン（またはアイテム全体をクリック）で、ProjectCuratorのEditorPage上で該当ファイルを開けるようにする。
- **テーマ互換性:** `AGENTS.md` の記述に従い、ダイアログはwpf-uiのテーマリソース・ダークモードに対応した作りとする（`AppSurface*`, `AppText` などのDynamicResourceを活用し、OSデフォルトの選択UIなどを上書きする）。

## 変更対象ファイル (予定)

- `Views/Pages/DashboardPage.xaml` / `DashboardPage.xaml.cs`: アイコンのクリッカブル化とダイアログ呼び出し処理。
- `ViewModels/ProjectCardViewModel.cs`: DecisionLog一覧を開くコマンドの追加。
- `Views/DecisionLogViewerDialog.xaml` (新規): 一覧表示用UI。
- `Views/DecisionLogViewerDialog.xaml.cs` (新規): コードビハインド。
- `Services/DecisionLogService.cs` (新規 または既存のContextCompressionLayerService拡張): DecisionLogの抽出とパース。

## 備考
- `AGENTS.md` の `Popup / Dialog Windows` のベストプラクティス（`SizeToContent`, `WindowChrome` の設定等）に準拠してダイアログを実装すること。
