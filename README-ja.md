# ProjectCurator

![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet)
![wpf-ui](https://img.shields.io/badge/wpf--ui-3.x-0078D4)
![Platform](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows)
![License](https://img.shields.io/badge/license-MIT-green)

複数のプロジェクトを横断管理する、Windows向けデスクトップアプリです。

## このアプリで何が便利になるか

ProjectCurator は、次の「面倒な行き来」を減らすためのツールです。

- プロジェクトの状態確認: フォルダを開き回らなくても、Dashboardで全体の鮮度とタスクを一望
- コンテキスト編集: `current_focus.md` や `decision_log` を専用Editorで素早く更新
- Asana連携: タスクをMarkdownへ同期して、プロジェクト状況を追いやすく管理

「複数案件を同時に進めると、どこを見るべきか迷う」を減らし、今やることに集中できます。

## こんな人向け
- 複数プロジェクトを並行して進めている
- Asanaタスクをプロジェクト文脈(Markdown)で管理したい

## 機能マップ

```mermaid
flowchart TD
    U["👤 あなた"] -->|"状況を確認"| DB["📊 Dashboard"]
    DB -->|"開く"| ED["📝 Editor"]
    ED -->|"更新"| CTX["🧠 current_focus.md / decision_log"]

    U -->|"タスク同期(任意)"| AS["🔄 Asana Sync"]
    AS -->|"書き出す"| TASKS["✅ asana-tasks.md"]
    DB -->|"読み取る"| TASKS

    U -->|"フォルダ管理"| SU["🧰 Setup"]
    U -->|"変更履歴を見る"| TL["🕒 Timeline"]
    U -->|"リポジトリを探索"| GR["🌿 Git Repos"]
    U -->|"パス・ホットキー・テーマ設定"| ST["⚙️ Settings"]

    GR -->|"未コミット変更を表示"| DB
    ST -->|"設定を反映"| DB
    ST -->|"設定を反映"| AS
```

## 5分で使い始める

### 1. GitHub Releases からアプリをダウンロード

- [最新の GitHub Release](https://github.com/yt3trees/ProjectCurator/releases) を開く
- `.zip` ファイルをダウンロード
- 任意のフォルダに展開(例: `C:\Tools\ProjectCurator\`)

### 2. `ProjectCurator.exe` を起動

- `ProjectCurator.exe` をダブルクリック
- Windows SmartScreen が出る場合は `詳細情報` -> `実行`

### 3. 最初に設定する場所

`Settings` で以下を設定して保存します。

- `Local Projects Root`
- `Box Projects Root`
- `Obsidian Vault Root`

保存時に必要な設定ファイルは自動生成されます。

### 4. Asana連携の初期設定(任意)

<details>
<summary>Asana設定手順を表示</summary>

- Asanaトークンは Developer Console(`https://app.asana.com/0/my-apps`)で作成・確認
- `Settings` を開いて Asana のグローバル値を入力
  - `asana_token`
  - `workspace_gid`
  - `user_gid`
- `Asana Sync` を開く
- 必要ならスケジュールを有効化して保存
- 手動同期を1回実行してタスクファイルを作成/更新

</details>

### 5. まず使うページ

- `Dashboard`: 今日見るべきプロジェクトを把握
- `Editor`: `current_focus.md` を更新
- `Asana Sync` (任意): タスクを同期してToday Queueへ反映

## フォルダ構成(ローカル管理 / BOX同期)

```mermaid
flowchart LR
    L["Local Projects Root(ローカル)"]
    B["Box Projects Root(BOX同期)"]
    O["Obsidian Vault Root(ノート同期)"]

    L --> P["MyProject/development/source(ローカル)"]
    L --> J1["MyProject/shared(ジャンクション)"]
    L --> J2["MyProject/_ai-context/context(ジャンクション)"]
    L --> J3["MyProject/_ai-context/obsidian_notes(ジャンクション)"]

    J1 --> B
    J2 --> O
    J3 --> O
```

```text
Local Projects Root/
└── MyProject/
    ├── development/
    │   └── source/                  # ローカル作業用リポジトリ(BOX外)
    ├── shared/                      # ジャンクション -> Box Projects Root/MyProject/
    │   ├── _work/
    │   │   ├── <workstream-id>/      # Setupタブで作る Workstream ごとの共有作業ディレクトリ
    │   │   └── 2026/
    │   │       └── 202603/
    │   │           └── 20260321_fix-login-bug/
    │   │                                 # Command Palette の resume で作る日付管理ディレクトリ
    │   ├── docs/                    # 共有ドキュメント(例)
    │   └── assets/                  # 共有素材(例)
    └── _ai-context/
        ├── context/                 # ジャンクション -> Obsidian Vault Root/Projects/MyProject/ai-context/
        └── obsidian_notes/          # ジャンクション -> Obsidian Vault Root/Projects/MyProject/
```

要点:
- `development/source/` はローカル作業領域です。
- `shared/` は BOX 側のパスにリンクして管理します。
- `_ai-context/` 配下は Obsidian 側パスにリンクして扱います。
- `shared/_work/<workstream-id>/` は Workstream 単位の共有作業に使います。
- 日付管理の作業フォルダ例: `shared/_work/2026/202603/20260321_fix-login-bug/`

## 日々のおすすめ運用フロー

1. `Dashboard` を開く
2. 気になるプロジェクト/Workstreamをクリックして `current_focus.md` を開く
3. `Editor` で更新して `Ctrl+S` で保存
4. 必要なら `decision_log` を1件追加
5. Asanaを使う場合は `Asana Sync` を実行してToday Queueを更新

```mermaid
flowchart TD
    A["Dashboardを開く"] --> B["案件/Workstreamを選ぶ"]
    B --> C["Editorでcurrent_focus.mdを開く"]
    C --> D["文脈を更新して保存"]
    D --> E["decision_log追加(任意)"]
    E --> F["Asana Sync実行(任意)"]
```

## Daily Standup 自動生成

ProjectCurator には、standup の自動生成機能があります。

- アプリ起動時に開始し、その後は1時間ごとにチェック
- 当日ファイルが未作成の場合のみ生成(冪等)
- 出力先: `{ObsidianVaultRoot}\standup\YYYY-MM-DD_standup.md`
- Command Palette の `standup` コマンドで手動生成/オープンも可能

生成内容は次の3セクションです。
- `Yesterday` (focus history / decision log / 完了済みAsanaタスク)
- `Today` (優先度の高いToday Queue項目)
- `This Week` (今週対応予定のQueue項目)

## AIエージェント協業 (Claude Code / Codex CLI)

ProjectCurator は Claude Code や Codex CLI などの AI コーディングエージェントとの協業を前提に設計されています。

### 仕組み

ProjectCurator で管理されるプロジェクトには、プロジェクトルートに `AGENTS.md` と `.claude/skills/`(および `.codex/skills/`)にスキル定義が配置されます。日付管理の作業フォルダ内でターミナルを開くと:

```
shared/_work/2026/202603/20260321_fix-login-bug/
```

Claude Code や Codex CLI は上位ディレクトリの `AGENTS.md` とスキル定義を自動的に読み込みます。これにより、エージェントは以下を把握した状態で作業を開始します:

- プロジェクト構成と主要パス
- AIコンテキストファイル(`current_focus.md`、`decision_log`、`tensions.md`)
- Obsidian Knowledge Layer のノート
- Asana タスク(同期済みの場合)

### エージェントの自律的な動作

組み込みスキルにより、明示的なコマンドなしで以下が自動的に行われます:

| スキル | 動作 |
|---|---|
| context-session-end | 作業の区切りを検知し、`current_focus.md` への追記を `[AI]` プレフィックス付きで提案 |
| context-decision-log | 会話中の暗黙の意思決定を検出し、`decision_log/` への構造化記録を提案 |
| obsidian-knowledge | セッション要約、技術メモ、会議記録などの Obsidian への記録を提案 |
| update-focus-from-asana | Asana タスク状況を `current_focus.md` に反映するスラッシュコマンド |

すべての提案はユーザーの承認後に書き込まれます。既存の人間が書いた内容は変更しません。

### エージェントのセッションフロー

```mermaid
flowchart TD
    A["作業フォルダでターミナルを開く"] --> B["エージェントが AGENTS.md + スキルを読み込む"]
    B --> C["current_focus.md, project_summary.md を読む"]
    C --> D["一緒に作業(コード、設計、デバッグ)"]
    D --> E["セッション終了を検知"]
    E --> F["current_focus.md 更新を提案"]
    E --> G["decision_log 記録を提案(決定があれば)"]
    E --> H["Obsidian ノート作成を提案(残す価値があれば)"]
```

### スキルの配置

ProjectCurator は Setup ページでプロジェクト作成・チェック時にスキルファイルを自動配置します:

- `.claude/skills/` (Claude Code 用)
- `.codex/skills/` (Codex CLI 用)

スキルはアプリ内蔵の `Assets/ContextCompressionLayer/skills/` から配置され、ジャンクション経由で共有フォルダと同期されます。

## 主要機能

| ページ | 何ができるか |
|---|---|
| Dashboard | プロジェクトヘルス、Today Queue、Workstreamごとの状況確認 |
| Editor | コンテキスト用Markdown編集、検索、リンクジャンプ、decision_log追加 |
| Timeline | 最近の変更履歴を時系列で確認 |
| Git Repos | ワークスペース内のGitリポジトリを再帰スキャン |
| Asana Sync | Asanaタスクをプロジェクト別/Workstream別Markdownに同期 |
| Setup | プロジェクト作成、構成チェック、Tier変換、Workstream管理 |
| Settings | テーマ、ホットキー、パス、自動更新設定 |

## 画面イメージ

### Dashboard

全プロジェクトのヘルス状態、更新鮮度、Today Queueを一画面で確認できます。

![](_assets/Dashboard.png)

<details>
<summary>Dashboard の詳細</summary>

- プロジェクトカードがグリッド表示される
- 各カードにはプロジェクト名、Tier バッジ(FULL / MINI)、カテゴリバッジ(DOMAIN)、ヘルス表示(緑 / 黄 / 赤)、更新鮮度(Since / Summary の日数)、未コミット変更数が表示される
- カードごとに Workstream を展開できる
- カード下部のアクションアイコンからフォルダを開いたり Editor へジャンプできる
- 中段の Pinned Folders でよく使う作業フォルダへ素早くアクセスできる
- 下段の Today Queue に優先度順のタスクが期限付きで一覧表示される
- ツールバーで自動更新の ON/OFF と間隔(例: 10 min)、手動リフレッシュ、ソートが可能
- フィルタバーでプロジェクトや Workstream を絞り込める

</details>

### Editor

AI コンテキストファイル(`current_focus.md`、`decision_log` など)をツリーから選び、シンタックスハイライト付きで編集できます。

![](_assets/Editor.png)

<details>
<summary>Editor の詳細</summary>

- 左上のプロジェクト選択ドロップダウンでプロジェクトを切り替え
- 左側のツリーに AI コンテキストファイルを表示: `current_focus.md`、`file_map.md`、`project_summary.md`、`tensions.md`、`decision_log/`、`focus_history/`、`obsidian_notes/`、`workstreams/`、`CLAUDE.md`、`AGENTS.md`
- 右側にシンタックスハイライト付きの Markdown エディタ(セクション単位で色分け)
- ツールバーボタン: Refresh、Dec Log(decision log 簡易追加)、P(フォルダ Pin)、Save
- ヘッダーバーにファイルのフルパスを表示
- ステータスバーに現在のプロジェクト名とファイル名を表示

</details>

### Timeline

プロジェクトや期間でフィルタして、変更履歴を時系列で確認できます。

![](_assets/Timeline.png)

<details>
<summary>Timeline の詳細</summary>

- Project ドロップダウンでプロジェクトを絞り込み(例: `GenAi [Domain]`)
- Period ドロップダウンで表示期間を設定(例: 30 days)
- Graph scope で単一プロジェクトか全プロジェクトを選択
- Entries タブに日付(曜日付き)と Focus ラベル付きのエントリ一覧を表示
- Graph タブで選択期間のアクティビティ推移をグラフ表示

</details>

### Git Repos

ワークスペース内のリポジトリを一覧表示し、リモートURL・ブランチ・最終コミット日を確認できます。

![](_assets/GitRepos.png)

<details>
<summary>Git Repos の詳細</summary>

- Project ドロップダウンでプロジェクト単位にリポジトリを絞り込み
- Scan ボタンでワークスペースルート配下を再帰的にスキャン
- Save to BOX / Load from BOX でクローン情報のバックアップ・復元
- Copy Clone Script で一覧のリポジトリを再クローンするシェルスクリプトを生成
- テーブル列: Project、Repository、Remote URL、Branch、Last Commit

</details>

### Asana Sync

プロジェクトごとにAsana同期のスケジュール、Workstreamマッピング、セクションフィルタを設定できます。

![](_assets/AsanaSync.png)

<details>
<summary>Asana Sync の詳細と設定手順</summary>

Asanaを使う場合のみ設定します。

左パネル(同期コントロール):

- Auto Sync チェックボックスと同期間隔(時間単位)の設定
- Save Schedule でスケジュールを保存
- Run Sync Now で即座に1回同期を実行
- Clear ボタンで同期状態をリセット
- Last sync に前回の同期日時を表示

右パネル(プロジェクト別設定):

- プロジェクト選択ドロップダウン(例: `GenAi [Domain]`)と Load ボタン
- Asana Project GIDs: 同期対象の Asana プロジェクト GID を1行ずつ入力
- Workstream Map: `gid` と `workstream-id` の対応を設定し、タスクを適切な Workstream フォルダに振り分け
- Workstream Field: Workstream を識別する Asana カスタムフィールド名
- Hidden Aliases: 同期出力から除外するエイリアス(1行ずつ)
- Save ボタンでプロジェクト別 `asana_config.json` を保存

設定手順:

1. `Settings` で Asana 連携を有効にし、必要項目を保存する
2. `Asana Sync` タブを開き、同期対象プロジェクトを選ぶ
3. まず `Run Sync` を1回実行する
   - 成功すると、次のファイルが更新されます
   - `_ai-context/obsidian_notes/asana-tasks.md`
   - 必要に応じて `_ai-context/obsidian_notes/workstreams/<id>/asana-tasks.md`
4. `Dashboard` に戻り、Today Queue を確認する
   - Today Queue は上記 `asana-tasks.md` を読み取って表示します
5. 定期同期したい場合だけ `Enable Schedule` を ON にする
6. 同期間隔を選び、`Save Schedule` を押す

うまく表示されないとき:
- `Run Sync` 実行後に `asana-tasks.md` が更新されているか確認
- `Dashboard` を再読み込みして Today Queue を更新

補足(通常は直接編集不要):
- Asana の設定値は `Documents\Projects\_config\asana_global.json` に保存されます
- プロジェクト単位の詳細設定は `{BoxProject}\asana_config.json` に保存されます

</details>

### Setup - New Project

プロジェクトの新規作成、構成チェック、アーカイブ、Tier 変換をまとめて行えるページです。

![](_assets/Setup-NewProject.png)

<details>
<summary>Setup の詳細 (New Project / Check / Archive / Convert Tier)</summary>

New Project タブ:

- Project Name: 既存プロジェクトを選ぶと Tier/Category を自動補完し、ExternalSharePath の追加や AI Context Setup の実行が可能
- Tier: `full (standard)` または `mini`
- Category: `project (time-bound)` または `domain`
- ExternalSharePath(任意): 共有データ用のカスタムパス
- Also run AI Context Setup: チェックすると `_ai-context/context/` と `_ai-context/obsidian_notes/` のジャンクションも自動作成
- Overwrite existing skills (-Force): `.claude/skills/` と `.codex/skills/` を既存でも再配置
- Setup Project ボタンでフォルダ構成、ジャンクション、スキルファイルを作成
- Output エリアに実行ログを表示

Check タブ:

- 既存プロジェクトのフォルダ構成、ジャンクション、スキルファイルを検証
- 不足や破損があれば報告

Archive タブ:

- プロジェクトをアーカイブ先に移動し、ジャンクションをクリーンアップ

Convert Tier タブ:

- `full` と `mini` の Tier 間でプロジェクトを変換し、フォルダ構成を調整

</details>

### Setup - Workstreams

プロジェクト内の Workstream を作成・ラベル編集・クローズ/再開できます。

![](_assets/Setup-Workstreams.png)

<details>
<summary>Workstreams の詳細</summary>

- プロジェクト選択ドロップダウンと Reload ボタン
- Add Workstream: Workstream ID(kebab-case)、ラベル(任意)、表示ラベル(任意)を入力し、Create Workstream で作成
- Existing Workstreams に各 Workstream の ID、ラベル、ステータス(Active / Closed)を一覧表示
- Close ボタンで Closed に変更、Reopen で Active に復帰
- Save Labels でラベルの変更を保存
- Output エリアに実行ログを表示

</details>

## キーボードショートカット(よく使うもの)

| Shortcut | Action |
|---|---|
| `Ctrl+K` | Command Paletteを開く |
| `Ctrl+1` - `Ctrl+7` | 各ページへ移動 |
| `Ctrl+S` | Editorで保存 |
| `Ctrl+F` | Editor検索 |
| `Ctrl+Shift+P` | アプリ表示/非表示(既定) |

## 設定ファイル

`ConfigService` は次のフォルダを利用します。

```text
%USERPROFILE%\Documents\Projects\_config\
├── settings.json
├── hidden_projects.json
├── asana_global.json
└── pinned_folders.json
```

`settings.json` / `asana_global.json` は `.gitignore` 対象です。

## 前提環境

- Windows
- .NET 9 Runtime(ソースビルドする場合はSDK)
- Git
- PowerShell 7+
- Python 3.10+(Asana同期を使う場合)

## 技術スタック

- .NET 9 + WPF
- wpf-ui 3.x
- AvalonEdit
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection

## 補足

- アプリはシステムトレイ常駐が基本です。
- 通常の閉じる操作は最小化(終了しません)。
- `Shift` を押しながら閉じると完全終了します。
