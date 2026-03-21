---
name: context-decision-log
description: AI behavioral guideline for detecting and recording architectural/design decisions during work sessions. The AI autonomously detects implicit decisions in conversation and proposes structured logging without requiring explicit invocation.
---

# Context Decision Log

AIが作業中に意思決定を自律的に検出し、構造化して記録する行動規範。

## 基本方針

ユーザーが明示的に「記録して」と言わなくても、AIは会話中の意思決定パターンを常に監視し、検出したら軽く提案する。提案は作業の流れを止めない1行に留める。

## 2つの記録パターン

### パターンA: AIとの作業中に決まったこと (自動検出)

セッション中の会話から意思決定パターンを検出し、その場で提案する。

### パターンB: 人間が別の場所で決めたこと (事後報告)

「会議で○○が決まった」等の事後報告。AIが質問して情報を補完:

```
記録します。いくつか教えてください:
- 他にどんな選択肢がありましたか？
- なぜそれに決めましたか？
- 見直す条件はありますか？
（わからない項目はスキップOK）
```

## 自動検出パターン

以下のパターンを会話中に検出したら、1行で記録を提案する:

### 検出するもの

| パターン | 例 |
|---------|---|
| 「○○にする」「○○を使う」 | 「DBはPostgreSQLにする」 |
| 「○○で行こう」「○○で進める」 | 「この設計で行こう」 |
| 「○○はやめる」「○○は不採用」 | 「Redisはやめておこう」 |
| 比較検討後の結論 | 「AよりBの方がいいね」 |
| 「○○にしておく」（暫定） | 「ひとまずSQLiteにしておく」→ 🔄仮決定 |

### 検出しないもの

- 変数名やインデント幅など軽微な判断
- 仮定の話（「もし○○なら」）
- 既知の事実の確認

### 提案のルール

```
💡 Decision Logに記録しますか？ → {決定の要約}
```

- 1セッション最大3回まで
- 断られたら引き下がる
- 作業の流れを止めない（1行で済ませる）

## ファイル名規則

`_ai-context/context/decision_log/YYYY-MM-DD_{topic}.md`

- topic: 英語snake_case（例: `db_schema_choice`, `api_framework_selection`）
- 同日複数: `_a`, `_b` をつける

## ドラフト生成

TEMPLATE.md に従って生成:

```markdown
# Decision: {タイトル}

> 日付: YYYY-MM-DD
> ステータス: ✅ 確定 / 🔄 仮決定
> 経緯: AI作業中 / 会議 / 単独判断

## Context（背景）

{2-3文}

## Options（選択肢）

### Option A: {名前}
- メリット:
- デメリット:

### Option B: {名前}
- メリット:
- デメリット:

## Chosen（選択）

→ Option {X}: {名前}

## Why（理由）

{2-4文}

## Risk（リスク）

-

## Revisit Trigger（再検討条件）

-
```

## 手順

1. 決定内容の把握（会話から自動抽出 or ユーザーに質問）
2. ドラフト生成してユーザーに提示
3. ステータス（確定/仮決定）と内容を確認
4. 承認後にファイル保存
5. project_summary.md の決定事項テーブルへの追記を提案
6. tensions.md に記録された未解決課題がこの決定で解決した場合、該当項目の削除を提案:
   「tensions.md の「{項目名}」はこれで解決しましたか？解決していればその行を削除します。」

## 品質基準

- Options は最低2つ（1つだけなら決定ではなく事実の記録）
- Why は「AIが推薦したから」ではなく具体的根拠
- Revisit Trigger は測定可能な条件
- 情報不足の場合（特にパターンB）は「不明」と書いてOK。後から補完できる
