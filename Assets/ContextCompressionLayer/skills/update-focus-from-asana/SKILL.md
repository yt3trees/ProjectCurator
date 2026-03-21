---
name: update-focus-from-asana
trigger: slash-command
command: /update-focus-from-asana
description: A Slash Command that fetches Asana tasks and suggests updates for the “What I'm Working On” and “Next Up” sections in current_focus.md. It automatically creates a backup before writing after user confirmation.
---

# Update Focus from Asana

Asana タスクを読んで `current_focus.md` を更新するスラッシュコマンド。

## 呼び出し方

```
/update-focus-from-asana [ProjectName]
```

## 動作フロー

### Step 1: プロジェクトの特定

`$ARGUMENTS` にプロジェクト名があればそれを使用する。
なければ会話コンテキストから推定する。それも不明な場合は `Documents/Projects/` 直下のプロジェクト一覧を提示してユーザーに選択を求める。

プロジェクトルートを確定したら以下のパスを解決する:

- asana-tasks: `<ProjectRoot>/_ai-context/obsidian_notes/asana-tasks.md`
- current_focus:
  - general 作業: `<ProjectRoot>/_ai-context/context/current_focus.md`
  - shared_work で対象 workstream が明確な作業: `<ProjectRoot>/_ai-context/context/workstreams/<workstreamId>/current_focus.md` を優先し、存在しない場合は `context/current_focus.md` にフォールバック
- backup: 選択した `current_focus.md` と同じ親系統の `focus_history/YYYY-MM-DD.md` (今日の日付)

作業モードの判定 (`/update-focus-from-asana` はプロジェクトルート実行を前提):
- `workstreamId` が特定できる場合: shared_work(workstream) モード
- `workstreamId` が特定できない場合: general モード

`workstreamId` の特定手順:
1. `$ARGUMENTS` や会話コンテキストに workstream ID が明示されていれば採用する
2. 明示がない場合、`_ai-context/context/workstreams/` 配下を確認する
3. 候補が1件だけならその ID を採用する
4. 候補が複数ある場合はユーザーに選択を求める
5. 候補が0件の場合は general として扱う

`current_focus.md` の選択ルール:
1. 作業モードが general の場合は `context/current_focus.md` を使う
2. 作業モードが shared_work かつ対象 workstream が明確な場合は `context/workstreams/<workstreamId>/current_focus.md` を優先する
3. 2 で `context/workstreams/<workstreamId>/current_focus.md` がない場合は `context/current_focus.md` にフォールバックする
4. 必要な `current_focus.md` が見つからない場合はエラー終了

### Step 2: ファイル存在チェック

`asana-tasks.md` が存在しない場合:

```
asana-tasks.md が見つかりませんでした: <path>
Asana sync を先に実行してください。終了します。
```

必要な `current_focus.md` が存在しない場合:

```
current_focus.md が見つかりませんでした:
  - general: <ProjectRoot>/_ai-context/context/current_focus.md
  - shared_work(workstream): <ProjectRoot>/_ai-context/context/workstreams/<workstreamId>/current_focus.md
先に Context Compression Layer のセットアップを行ってください。終了します。
```

### Step 3: バックアップチェックと実行

選択した `current_focus.md` に対応する `focus_history/YYYY-MM-DD.md` が存在しない場合:
- `current_focus.md` の内容をそのままコピーしてバックアップを作成する
- 1行通知: `💾 バックアップ作成: focus_history/YYYY-MM-DD.md`

既に存在する場合:
- スキップして1行通知: `💾 本日のバックアップは既に存在します (スキップ)`

バックアップは承認前に実施する (更新失敗でもバックアップは残る)。

### Step 4: Asana タスク解析

`asana-tasks.md` を読み込み、以下を抽出する:

- 進行中タスク: `🔄` マーク、または `- [ ]` で未完了かつ優先度高 (High/最高)
- 完了タスク: `✅` マーク、または `- [x]` 済み

#### [コラボ] タスクの扱い

タスク行の先頭に `[コラボ]` が付いているものは、自分がコラボレーター (関係者) であるが担当者ではないタスクを示す。以下のルールを適用する:

- `[担当]` タスクを優先して「今やってること」「次やること」に反映する
- `[コラボ]` タスクは原則として提案に含めない
- ただし以下の場合は例外として参考情報として触れてよい:
  - `current_focus.md` に既に記載されている場合 (→ 削除提案は不要)
  - 期日が直近 (今日〜翌営業日) で、かつ明らかに自分の作業が必要と読み取れる場合
- `[コラボ]` タスクを提案に含める場合は `[コラボ]` プレフィックスを付けて区別する

### Step 5: 更新提案の生成

`current_focus.md` を読み込み、Asana タスクと照合して更新案を作成する。

提示フォーマット:

```
📋 current_focus.md の更新提案 (プロジェクト: <ProjectName>)

【今やってること】の変更:
  現在: - xxx
  提案: - xxx (Asana: #1234)
  ※ 追加: - yyy (Asana: #5678)

【次やること】の変更:
  ※ 追加: - zzz (Asana: #9012)

【完了タスクへの対応】:
  - [完了提案] "aaa" → Asana #3456 が完了しています。この行に [完了] を付けますか？
```

提案を出力したら、選択形式UIでユーザーに確認する:

- Claude Code: AskUserQuestion ツールを使う
- Codex CLI (Plan モード): 同等の選択形式UIを使う
- どちらも利用できない場合: 以下のテキスト形式で出力してユーザーの入力を待つ

```
current_focus.md に上記の更新を適用しますか？
  1) はい - このまま書き込む
  2) 修正あり - 内容を修正してから書き込む
  3) 不要 - スキップ (バックアップは保持)
```

選択肢 (いずれの形式でも共通):
- "はい - このまま書き込む"
- "修正あり - 内容を修正してから書き込む"
- "不要 - スキップ (バックアップは保持)"

### Step 6: ユーザー確認 → 書き込み

- 「はい」「1」「y」: `current_focus.md` を更新し、末尾の「更新:」日付も更新する
- 「修正あり」「2」: ユーザーの修正内容を確認してから書き込む
- 「不要」「3」「n」: 書き込みをスキップ (バックアップは保持)

## ルール

- バックアップは承認前に実施する
- 既存の人間が書いた行は原則残す
- 完了した Asana タスクに対応する行は [完了] マーク提案のみ行い、自動削除しない
- `asana-tasks.md` を直接編集しない (自動生成ファイルのため)
- 承認なしに `current_focus.md` を書き込まない
- 選択した `current_focus.md` に対応する `focus_history/` ディレクトリが存在しない場合は自動作成する
