# CLAUDE.md 追記スニペット

プロジェクト個別の CLAUDE.md に以下を追記してください。
ワークスペース全体用は末尾の「ワークスペース用」セクションを参照。

---

## プロジェクト個別

```markdown
## Context Compression Layer

### Session Start Protocol

セッション開始時（最初のユーザーメッセージへの応答前）に実行:

1. current_focus.md は作業モードに応じて読む:
   - general 作業: `_ai-context/context/current_focus.md`
   - shared_work で対象 workstream が明確な作業 (`workstreamId` を特定できる場合): `_ai-context/context/workstreams/<workstreamId>/current_focus.md` を優先（なければ `context/current_focus.md`）
2. `_ai-context/context/project_summary.md` を読む
3. `_ai-context/context/tensions.md` を読む
4. `_ai-context/obsidian_notes/asana-tasks.md` があれば読む
5. 他ファイルはオンデマンドのみ
6. 未完了事項を1〜2行でサマリー提示（Tensionがあれば踏まえる）
7. 更新が3日以上前なら進捗を1回確認する

サイズ超過時は `focus_history/` へのアーカイブを提案する。

### AI行動規範

| 区分 | 対象 | 動作 |
|-----|------|------|
| Auto | current_focus.md 追記 | 承認なし |
| Notify | tensions.md・decision_log (低影響) | 1行報告後に実行 |
| Confirm | project_summary.md・decision_log (高影響) | 確認を取る |

#### 未解決課題
- tensions.md の制約に反する提案は、その Tension に言及する
- トレードオフ検出 → 「⚠️ tensions.md に記録しますか？ → {要約}」(最大2回/セッション)
- 解決時は削除と decision_log への昇格を提案

#### 意思決定
アーキテクチャ・技術選定の決定を検出 → 「💡 Decision Logに記録しますか？ → {要約}」
承認後 `decision_log/YYYY-MM-DD_topic.md` を作成。(最大3回/セッション)

#### セッション終了
「ありがとう」「ここまで」等で current_focus.md への追記を提案。
`[AI]` プレフィックス・3〜5行・既存行は変更しない。
asana-tasks.md の進行中タスクと照合し、関連があれば追記案に含める。
```

---

## ワークスペース用

```markdown
## Context Compression Layer

プロジェクト横断の作業時は `.context/active_projects.md` を確認してください。
各プロジェクト内の指示は、各プロジェクトの CLAUDE.md に記載されています。
```
