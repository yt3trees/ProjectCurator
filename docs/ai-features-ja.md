# AI機能

[< READMEに戻る](../README-ja.md)

すべての AI 機能は `Settings > LLM API` で `Enable AI Features` をオンにする必要があります。対応プロバイダー: OpenAI / Azure OpenAI。

<a id="ai-features-overview-ja"></a>
## AI機能の全体像

ProjectCuratorのAI機能は、「どんなシチュエーションで使うか（把握・更新・メモ）」によって大きく3つに分類されています。

```mermaid
flowchart LR
    %% Base
    Profile>👤 User Profile<br>回答のトーンや前提を設定]

    %% 分類
    subgraph Dashboard ["📊 把握・計画 (Dashboard)"]
        direction TB
        WN["💡 What's Next<br>次やるべき事の提案"]
        CB["📋 Context Briefing<br>再開前のおさらい"]
        TP["📅 Today's Plan<br>1日の計画立て"]
    end

    subgraph Editor ["📝 更新・記録 (Editor)"]
        direction TB
        UF["🎯 Update Focus<br>Asana情報から方針更新"]
        DL["⚖️ AI Decision Log<br>決定事項の構造化・記録"]
        IM["👥 Import Meeting Notes<br>打合せメモの自動仕分け"]
    end

    subgraph Global ["⚡ 随時メモ (Global)"]
        direction TB
        QC["🪟 Quick Capture<br>Ctrl+Shift+C<br>AIによる自動ルーティング"]
    end

    subgraph Wiki ["📚 ナレッジベース (Wiki)"]
        direction TB
        WI["📥 Import<br>ソースを取り込み要約ページ生成"]
        WQ["💬 Query<br>Wikiへの質問・回答"]
        WL["🔍 Lint<br>矛盾・孤立ページの検出"]
    end

    Profile -.-> Dashboard
    Profile -.-> Editor
    Profile -.-> Global
    Profile -.-> Wiki
```

<a id="setup-ja"></a>
## 初期設定

1. `Settings > LLM API` を開く
2. プロバイダーを選択し、API Key と Model を入力 (Azure の場合は Endpoint / API Version も)
3. `Test Connection` をクリック
4. テスト成功後、`Enable AI Features` をオンにして保存

<a id="user-profile-ja"></a>
## ユーザープロフィール

`Settings > LLM API > User Profile` に自分の役割・優先軸・文体などを自由記述で入力します。ここで設定したテキストは、すべての LLM 呼び出しのシステムプロンプト先頭に `## User Profile` セクションとして自動付与されます。毎回プロンプトに書かなくても、モデルがあなたの文脈を把握した状態で回答します。

記入例:

```
役割: エンジニアリングマネージャー。3～4件のプロジェクトを並行管理。
箇条書きで簡潔に。タスクを詰め込むより過負荷の日をフラグしてほしい。
current_focus.md の更新は既存のトーンを維持すること。
```

<a id="global-ja"></a>
## Global

<a id="quick-capture-global-hotkey-ja"></a>
### Quick Capture (グローバルホットキー)

デスクトップのどこからでも `Ctrl+Shift+C` を押すと、軽量キャプチャウィンドウが起動します。フリーテキストを入力して Enter を押すと、AI Features が有効な場合は LLM が内容を分類して自動でルーティングします。

| カテゴリ | 振り分け先 |
|---|---|
| `task` | Asana API でタスクを直接起票 (送信前に確認ステップあり) |
| `tension` | プロジェクトの `open_issues.md` に追記 |
| `focus_update` | Editor を開き、入力内容をコンテキストとして Update Focus from Asana フローを起動 |
| `decision` | Editor を開き、AI Decision Log フローを起動 |
| `memo` | `_config/capture_log.md` にタイムスタンプ付きで追記 |

AI Features が無効の場合は、カテゴリとプロジェクトを手動で選択することで引き続き利用できます。

<a id="dashboard-ja"></a>
## Dashboard

<a id="whats-next-dashboard-ja"></a>
### What's Next

Dashboard ツールバーの lightbulb アイコンをクリックすると、全プロジェクト横断で優先度順の 3～5 件のアクション提案を取得できます。期限超過タスク・focus ファイルの鮮度・未コミット変更・未記録の決定事項などを LLM が分析し、緊急度順にランキングします。各提案の [Open] ボタンで該当ファイルへ直接移動できます。

<img src="../_assets/ai-feature/WhatsNext.png" width="70%" alt="What's Next ダイアログ" />

<a id="context-briefing-dashboard-card-ja"></a>
### Context Briefing (Dashboardカード)

Dashboard の各プロジェクトカードにある lightbulb アイコンをクリックすると、対象プロジェクト専用の再開ブリーフィングを生成します。モデルは `current_focus.md`、直近の `decision_log`、`open_issues.md`、Asana の進行中/完了タスク、未コミット変更をまとめて読み取り、次を表示します。

- `Where you left off` (現状の要約)
- `Suggested next steps` (優先度付きアクション)
- `Key context` (再開時に必要な事実メモ)

ダイアログには `Copy`、`Open in Editor`、`View Debug` (プロンプト/レスポンス確認) が用意されています。

<img src="../_assets/ai-feature/ContextBriefing.png" width="70%" alt="Context Briefing ダイアログ" />

<a id="todays-plan-dashboard-ja"></a>
### Today's Plan

Today's Plan ダイアログ(AI)では、1日の提案を時間帯別(例: Morning / Afternoon)に表示し、`Open` / `Copy` / `Save` / `View Debug` が利用できます。

<img src="../_assets/ai-feature/TodaysPlan.png" width="70%" alt="Today's Plan ダイアログ" />

<a id="editor-ja"></a>
## Editor

<a id="update-focus-from-asana-editor-ja"></a>
### Update Focus from Asana

Editor ツールバーの `Update Focus from Asana` ボタンをクリックすると、開いている `current_focus.md` の差分ベース更新提案を生成します。モデルは Asana タスクデータと既存ファイルを読み込み、見出し構造と文体を保持しながら変更案を提示します。バックアップは `focus_history/` に自動保存。Workstream 絞り込み・自然言語による再指示・デバッグ表示に対応しています。

<img src="../_assets/ai-feature/UpdateFocusFromAsana.png" width="70%" alt="Update Focus from Asana ダイアログ" />

<a id="ai-decision-log-editor-ja"></a>
### AI Decision Log

Editor ツールバーの `Dec Log` ボタン (AI モード) で意思決定ログ作成ダイアログを開きます。決定内容を記述すると、モデルが Options / Why / Risk / Revisit Trigger を含む構造化ドラフトを生成します。自然言語による再指示に対応し、`open_issues.md` の解決済み項目の削除も可能。`decision_log/YYYY-MM-DD_{topic}.md` として保存されます。

<img src="../_assets/ai-feature/AI-DecisionLog_1.png" width="70%" alt="AI Decision Log ダイアログ 1" />
<img src="../_assets/ai-feature/AI-DecisionLog_2.png" width="70%" alt="AI Decision Log ダイアログ 2" />

<a id="import-meeting-notes-editor-ja"></a>
### Import Meeting Notes

Editor ツールバーの `Import Meeting Notes` ボタンをクリック (または会議メモ入力ダイアログで `Ctrl+Enter`) すると、会議メモを貼り付けて LLM に1回で分析させることができます。プレビューダイアログは4つのタブで構成されます。

- Decisions タブ: 検出された意思決定をチェックボックスで一覧表示。「Show draft」で構造化された `decision_log` ドラフトをプレビュー。不要な項目はチェックを外して除外可能
- Focus タブ: LLM が `current_focus.md` 全文を再生成した提案を差分ビューで表示。既存の見出し構造・文体を保持しつつ新しい項目を統合
- Tensions タブ: `open_issues.md` に追記する内容のプレビュー(技術的疑問・トレードオフ・懸念)
- Asana Tasks タブ: 会議から抽出されたアクション項目の一覧。タスクごとに以下を設定可能:
  - Project: `asana_global.json` の `personal_project_gids` 先頭を初期選択。workstream に対応プロジェクトが設定されていればそちらを優先
  - Section: `asana_config.json` の `anken_aliases` とセクション名を照合して自動選択
  - Due Date: 任意で期限日を設定
  - Set time: チェックを入れると Hour / Minute セレクターが表示され、ローカルタイムゾーン付きの `due_at` として起票
  - チェックを入れたタスクのみ起票・追記

適用する項目を選択して `Apply Selected` をクリック。ダイアログ左下の `View Debug` ボタンで送信プロンプトと LLM レスポンスを確認できます。Decision log は `YYYY-MM-DD_{topic}.md` として保存。`current_focus.md` は更新前に `focus_history/` へ自動バックアップ。起票済みの Asana タスクは GID と期限付きで `tasks.md` へ追記されます。

<img src="../_assets/ai-feature/ImportMeetingNotes_1.png" width="70%" alt="Import Meeting Notes ダイアログ 1" />
<img src="../_assets/ai-feature/ImportMeetingNotes_2.png" width="70%" alt="Import Meeting Notes ダイアログ 2" />

<a id="wiki-ja"></a>
## Wiki

Wiki の詳細は、読みやすさのため [Wiki機能ドキュメント](wiki-features-ja.md) に分離しました。

