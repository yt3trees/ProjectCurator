# asana-tasks.md 日本語→英語化 実装計画書

作成日: 2026-04-04

## 目的

asana-tasks.md のセクション名・タグ等の日本語文字列を英語に統一する。
このファイルは複数のサービスで読み書き・パース・表示に使用されているため、
変更はすべてのファイルを同時に整合させる必要がある。

---

## 変換マッピング (日本語 → 英語)

| 種別 | 変更前 (日本語) | 変更後 (英語) |
|------|----------------|--------------|
| 役割タグ | `[担当]` | `[Owner]` |
| 役割タグ | `[コラボ]` | `[Collab]` |
| 役割タグ | `[他]` | `[Other]` |
| 優先度タグ | `[高]` | `[High]` (すでに `[High]` 併用あり → `[高]` を廃止) |
| セクション | `## 進行中` | `## In Progress` |
| セクション | `## 完了 (直近)` | `## Completed (recent)` |
| セクション | `### 進行中` | `### In Progress` |
| セクション | `### 完了 (直近)` | `### Completed (recent)` |
| セクション | `## 個人 / 未分類` | `## Personal / Uncategorized` |
| テキスト | `個人タスクより` | `From personal tasks` |
| テキスト | `個人 / 未分類` (目次行内) | `Personal / Uncategorized` |
| テキスト | `進行中:` (目次の括弧内) | `In Progress:` |
| フィールド名 | Asana カスタムフィールド `優先度` | `Priority` (※後述の注意参照) |

---

## 変更対象ファイル一覧

### 1. Services/AsanaSyncService.cs (最重要: 書き込み側)

ファイルへの書き込みを担当する。ここが変わらないと以降のパース変更が無意味になる。

変更箇所:
- L471: `ClassifyTaskRole` の判定条件
  - `is "担当" or "コラボ"` → `is "Owner" or "Collab"`
- L476: `return "担当"` → `return "Owner"`
- L481: `return "コラボ"` → `return "Collab"`
- L591: `個人タスクより` → `From personal tasks`
- L600: `個人 / 未分類` → `Personal / Uncategorized`
- L611: `## 進行中` → `## In Progress`
- L624: `## 完了 (直近)` → `## Completed (recent)`
- L659: `## 進行中` → `## In Progress`
- L672: `## 完了 (直近)` → `## Completed (recent)`
- L789: `進行中:` → `In Progress:`
- L792: `個人 / 未分類` → `Personal / Uncategorized`、`進行中:` → `In Progress:`
- L825: `## 個人 / 未分類` → `## Personal / Uncategorized`
- L861: `### 進行中` → `### In Progress`
- L874: `### 完了 (直近)` → `### Completed (recent)`
- L901: Asana カスタムフィールド名 `優先度` → ※注意参照
- L955: ソートキー `"担当" => 0` → `"Owner" => 0`
- L956: ソートキー `"コラボ" => 1` → `"Collab" => 1`

### 2. Services/AsanaTaskParser.cs (パース側)

変更箇所:
- L17: regex `\[コラボ\]` → `\[Collab\]`
- L18: regex `高|High` → `High` (統一後は High のみ)
- L81: `.Replace("[コラボ]", "")` → `.Replace("[Collab]", "")`
- L82: `.Replace("[担当]", "")` → `.Replace("[Owner]", "")`

### 3. Services/TodayQueueService.cs (パース・更新側)

変更箇所:
- L113: `RoleTagRx` = `@"^\[(?:担当|コラボ|他)\]\s*"` → `@"^\[(?:Owner|Collab|Other)\]\s*"`
- L115: `ColaboTagRx` = `@"^\[コラボ\]"` → `@"^\[Collab\]"`
- L117: `OtherTagRx` = `@"^\[他\]"` → `@"^\[Other\]"`
- L119: `InProgressHeadingRx` = `@"^\s*#{2,3}\s*進行中"` → `@"^\s*#{2,3}\s*In\s+Progress\b"`
- L121: `DoneHeadingRx` = `@"^\s*#{2,3}\s*完了"` → `@"^\s*#{2,3}\s*Completed\b"`

### 4. Services/FocusUpdateService.cs (LLMプロンプト生成側)

変更箇所 (プロンプトのヘッダ出力):
- L248: `## Asana tasks: In-progress [担当]` → `## Asana tasks: In-progress [Owner]`
- L266: `## Asana tasks: Completed [担当]` → `## Asana tasks: Completed [Owner]`
- L285: `## Asana tasks: Not started, high priority [担当]` → `## Asana tasks: Not started, high priority [Owner]`
- L304: `## Asana tasks: Not started, other [担当]` → `## Asana tasks: Not started, other [Owner]`
- L325: `## Asana tasks: Urgent collab [コラボ]` → `## Asana tasks: Urgent collab [Collab]`
- L338: `In-progress [担当]` → `In-progress [Owner]`
- L358: `Not started, high priority [担当]` → `Not started, high priority [Owner]`
- L368: `Not started, other [担当]` → `Not started, other [Owner]`

### 5. Views/Pages/DashboardPage.xaml.cs (表示・Regex側)

変更箇所:
- L1815: `InProgressHeadingRx`
  - 現在: `@"^\s*#{2,3}\s*(進行中|In\s*progress)\b"`
  - 変更後: `@"^\s*#{2,3}\s*In\s+Progress\b"`
- L1818: `DoneHeadingRx`
  - 現在: `@"^\s*#{2,3}\s*(完了|Done|Completed)\b"`
  - 変更後: `@"^\s*#{2,3}\s*Completed\b"`
- L2206: `@"^\s*\[(担当|コラボ|他)\]\s*"` → `@"^\s*\[(Owner|Collab|Other)\]\s*"`

---

## Asana カスタムフィールド名 `優先度` について

Asana ワークスペース側でカスタムフィールド名を `Priority` に変更する前提で対応する。

対応方針:
- `AsanaSyncService.cs L901` のフィールド名取得箇所: `"優先度"` → `"Priority"` に変更
- フィールド値 (`高`/`High`/`Medium`/`Low`) も英語値のみになるため、`高` → `High` の変換ロジックが残っていれば削除する
- `AsanaTaskParser.cs L18` の優先度 regex: `高|High` → `High` に統一

---

## 実装順序

1. AsanaSyncService.cs を変更 (書き込み側を最初に修正)
2. AsanaTaskParser.cs を変更 (パース側)
3. TodayQueueService.cs を変更 (パース・更新側)
4. FocusUpdateService.cs を変更 (プロンプト生成側)
5. DashboardPage.xaml.cs を変更 (表示側)
6. ビルド検証: `dotnet publish -p:PublishProfile=SingleFile`

---

## 既存ファイルの移行について

変更後は、既存の `asana-tasks.md` ファイル (日本語フォーマット) はそのまま読み取れなくなる。
次回 Asana Sync を実行すれば新フォーマットで上書き生成されるため、
ユーザーに「変更後に Asana Sync を一度実行してください」と案内する。

---

## 変更しないもの

- ログ文字列 (L123, L148 のコメント付きログ) — 読み取り動作に影響しない
- コード内コメント — 動作に影響しない
- `_config/*.example` ファイル内のサンプルテキスト — 必要に応じて別途更新可

---

## チェックリスト

- [ ] AsanaSyncService.cs 変更
- [ ] AsanaTaskParser.cs 変更
- [ ] TodayQueueService.cs 変更
- [ ] FocusUpdateService.cs 変更
- [ ] DashboardPage.xaml.cs 変更
- [ ] `優先度` フィールド名の確認・対応
- [ ] ビルド成功確認 (`dotnet publish -p:PublishProfile=SingleFile`)
- [ ] ユーザーへの Asana Sync 再実行案内
