---
name: obsidian-knowledge
description: AI behavioral guideline for integrating with the Obsidian Knowledge Layer. The AI reads Obsidian notes to enrich context and proposes writing structured notes back to the vault, without requiring explicit commands.
---

# Obsidian Knowledge

Obsidian Vault(Layer 2)をAIが読み書きする行動規範。人間が蓄積したナレッジを文脈補完に活かし、AI作業の成果をVaultに還元する。

## 基本方針

- `obsidian_notes/` は人間も読むナレッジベース。AIは必要な時だけ読み、価値があると判断した時だけ書く
- 書き込む際は `author: ai` で人間のノートと区別する

## 役割と対象

- `obsidian_notes/` は人間も読むナレッジベースであるため、AIは必要な時だけ読み、価値があると判断した時だけ書く
- 作業中に再利用価値のある知見（デバッグ結果、環境設定、設計理由等）を発見したら、積極的に `notes/` への保存を提案する
- 後から検索しやすくするため、AIが生成したノートには必ず `#ai-memory` タグをつける

## 読み取りパターン

### セッション開始時(任意)

- Indexファイル(`00_{ProjectName}-Index.md`)が存在すれば確認して構造把握
- Vault全体をスキャンしない(CCLの原則: 精度が落ちる)

### トピック駆動

会話中のトピックに関連するノートがありそうなとき、該当フォルダをGrep検索:

```bash
# meetingsフォルダでキーワード検索
rg "認証" _ai-context/obsidian_notes/meetings/ -l

# notesフォルダでキーワード検索
rg "Redis" _ai-context/obsidian_notes/notes/ -l
```

### 明示的リクエスト

「会議メモ見て」「前のスペック確認して」等、ユーザーから明示的に参照を求められた場合。

## 書き込みパターン

### daily/ - セッションサマリー

まとまった作業セッション終了時に提案:

```
📓 Obsidianに記録しますか？
→ daily/2026-02-23_ai-session.md
  認証リファクタリングセッション: トークンリフレッシュ実装、decision_log記録
```

### meetings/ - 構造化議事録

ユーザーが会議内容を共有した時に提案:

```
📓 Obsidianに記録しますか？
→ meetings/2026-02-23_authentication-review.md
  認証フロー見直し会議: 主な決定事項、参加者、アクションアイテム
```

### notes/ - 技術的な発見・調査結果

技術的な発見を文脈豊かに記録したい場合:

```
📓 Obsidianに記録しますか？
→ notes/redis-cache-strategy.md
  Redis TTL設計とキー命名規則の調査結果
```

### specs/ - 設計提案・アーキテクチャ検討

設計の意思決定を記録したい場合:

```
📓 Obsidianに記録しますか？
→ specs/auth-token-refresh-design.md
  トークンリフレッシュ処理の設計案
```

### troubleshooting/ - 障害対応・エラー解決策

過去のエラーやバグの原因と解決手順を記録したい場合:

```
📓 Obsidianに記録しますか？
→ troubleshooting/db-timeout-resolution.md
  DB接続タイムアウトエラーの原因と再発防止策
```

## ノートフォーマット

```markdown
---
author: ai
created: YYYY-MM-DD
type: session-summary  # session-summary | meeting | note | spec
tags: [ai-memory, tag1]
---

# {タイトル}

{本文。Obsidian記法を使用。}

## 関連

- [[関連ノート名]]
- [[decision_log/YYYY-MM-DD_topic]]
```

## 提案ルール

- 提案形式: `📓 Obsidianに記録しますか？ → {folder}/{filename}`
- 1セッション最大2回まで提案する
- 断られたら、そのセッションでは以降勧めない

## グローバルナレッジへの振り分け判断

Obsidian に保存する際、以下の基準で保存先を決める:

### プロジェクト固有 → obsidian_notes/ 配下

- そのプロジェクト特有の設計判断、要件、経緯

### プロジェクト横断 → {obsidianVaultRoot}/ai-context/ (_config/paths.json 参照)

- tech-patterns/: 複数プロジェクトで再利用できるコード・設計パターン
  例: PowerShell WPF の日本語対応パターン、SJIS/UTF-8 変換のベストプラクティス
- lessons-learned/: 失敗・ハマりから学んだこと (frontmatter にプロジェクト名タグを付与)
  例: Junction 作成時の落とし穴、ScriptBlock クロージャの挙動

グローバル保存の提案形式:
「📓 グローバルナレッジに記録しますか？
→ ai-context/tech-patterns/{filename}.md
  {内容の1行サマリー}」

## CCL との連携

- session-end と同時: `daily/` への書き込み提案を `current_focus.md` の追記提案と同時に行う
- decision_log と連動: 重要な設計決定を `specs/` にも残す場合、decision_log への記録と同時に提案
