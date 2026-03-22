# Smart Daily Standup - LLM 強化 実装計画

既存の StandupGeneratorService のテンプレート生成を LLM で強化し、複数プロジェクトの文脈を理解した自然言語の standup を生成する。

## 現状の課題

現在の standup は箇条書きの羅列:
- Yesterday: focus_history のプレビュー (先頭80文字) + decision_log のトピック名 + 完了タスク名
- Today / This Week: TodayQueue のタスク名 + 期日ラベル

課題:
- focus の「何をやったか」が80文字の切り取りで文脈が伝わらない
- 複数プロジェクト間の優先順位や注力バランスが見えない
- 「今日何に集中すべきか」の判断が読み手任せ
- decision_log はトピック名だけで中身が分からない
- Workstream レベルの情報が含まれない

## 方針

- 既存のテンプレート生成 (ルールベース) はデータ収集ステップとして残す
- LLM は収集したデータを「読みやすい文章」に変換するステップとして追加
- AI 無効時は従来のテンプレート出力をそのまま使用 (フォールバック)
- AI 有効時は LLM で強化した standup を生成
- 出力先・ファイル命名規則は変更なし
- 既存のスケジューラ (起動時 + 1時間ごと) はそのまま維持

## 強化ポイント

### 1. Yesterday セクションの充実

現状: `- [ProjectName] <80文字プレビュー> (Focus)`
強化後:
- focus_history の差分 (前日 vs 前々日) から「何が変わったか」を要約
- decision_log の中身 (Context + Chosen) を要約
- 完了タスクを文脈付きで記述
- プロジェクトごとに1-2文のナラティブにまとめる

### 2. Today セクションの知的な優先順位付け

現状: TodayQueue のタスクを期日順に羅列
強化後:
- current_focus.md の「今やること」セクションと Today Queue を突合
- 「今日集中すべきこと」を優先順位付きで提案
- 期限超過タスクがあれば警告トーンで強調
- プロジェクト間のバランスに言及 ("ProjectX に集中、ProjectY は明日以降")

### 3. Awareness セクション (新規)

以下のシグナルを検出して注意喚起:
- focus が N日以上未更新のプロジェクト
- tensions.md に未解決項目があるプロジェクト
- 未コミット変更があるリポジトリ
- 期限超過タスクの件数

### 4. Workstream 対応

- Workstream 単位の focus_history / decision_log も収集
- プロジェクト配下の Workstream ごとに状況を記述

## 出力フォーマット例

```markdown
# Daily Standup: 2026-03-22

## Yesterday

### ProjectAlpha
Auth module の実装を進め、JWT トークンのリフレッシュロジックを完成させた。
DB スキーマについて PostgreSQL を採用する決定を記録 (SQLite との比較検討の結果、マルチユーザー対応が決め手)。
Asana: "JWT refresh endpoint" と "Token expiry handling" を完了。

### ProjectBeta [design-system]
カラーパレットの見直しを実施。ダークモードのコントラスト比を WCAG AA 基準に合わせた。

## Today

1. **ProjectAlpha**: Auth module のテスト作成が最優先 (Today)。API ドキュメント更新も期限当日。
2. **ProjectBeta**: デザインレビューの準備 (In 1d)。急ぎではないが明日のレビューまでに完了したい。

Overdue: ProjectAlpha の "Migration script review" が 2日超過 - 対応が必要。

## This Week

- ProjectAlpha: E2E テスト環境のセットアップ (In 3d)
- ProjectBeta: コンポーネントライブラリの公開準備 (In 5d)

## Awareness

- ProjectGamma: focus が 8日未更新 - 状況確認を推奨
- ProjectAlpha: development/source に未コミット変更あり (3 repos)
```

## アーキテクチャ

```
StandupGeneratorService (既存: 拡張)
  ├── TryGenerateTodayAsync()          既存: ガードチェック + 生成呼び出し
  ├── GenerateAndSaveAsync()           既存: テンプレート生成 (データ収集として残す)
  ├── CollectStandupDataAsync()        新規: 構造化データの収集
  ├── GenerateSmartStandupAsync()      新規: LLM で自然言語 standup を生成
  └── BuildYesterdayLines() 等         既存: テンプレート用ヘルパー (フォールバック)
        │
        ▼
LlmClientService (既存: そのまま利用)
```

変更は既存サービスの拡張のみ。新規サービスは不要。

## データ収集の拡充

### 現在収集しているもの

| データ | ソース | 用途 |
|---|---|---|
| focus プレビュー (80文字) | focus_history/{yesterday}.md | Yesterday |
| decision_log トピック名 | decision_log/{yesterday}_*.md | Yesterday |
| 完了タスク名 | asana-tasks.md (`<!-- completed: -->`) | Yesterday |
| Today Queue タスク | TodayQueueService | Today / This Week |

### 追加で収集するもの

| データ | ソース | 用途 |
|---|---|---|
| focus_history 全文 (前日) | focus_history/{yesterday}.md | Yesterday の文脈 |
| focus_history 差分 | {day-before-yesterday}.md vs {yesterday}.md | 変化の検出 |
| decision_log 本文 (Context + Chosen) | decision_log/{yesterday}_*.md | Yesterday の決定詳細 |
| current_focus.md 全文 | current_focus.md | Today の優先順位判断 |
| tensions.md | tensions.md | Awareness |
| FocusAge | ProjectInfo.FocusAge | Awareness |
| HasUncommittedChanges | ProjectInfo.HasUncommittedChanges | Awareness |
| UncommittedRepoPaths | ProjectInfo.UncommittedRepoPaths | Awareness |
| Workstream focus/decisions | workstreams/{id}/focus_history, decision_log | Yesterday (workstream) |

## 実装タスク

### Phase 1: データ収集の拡充

- [ ] 1-1. `StandupProjectData` モデルを新規作成
  - プロジェクト名、Tier、Category
  - Yesterday: focus_history 全文、focus 差分、decision_log 本文リスト、完了タスクリスト
  - Today: current_focus.md 全文、Today Queue タスクリスト
  - Awareness: FocusAge、HasUncommittedChanges、未コミットリポ数、tensions 有無
  - Workstream 別データ (上記のサブセット)
  - ファイル: `Models/StandupModels.cs` (新規)

- [ ] 1-2. `CollectStandupDataAsync()` を実装
  - 全プロジェクトを走査し、StandupProjectData のリストを構築
  - focus_history は前日分の全文読み込み + 前々日分との差分計算
  - decision_log は前日分の本文 (Context + Chosen セクション) を抽出
  - 現在の BuildYesterdayLines / BuildTodayAndWeekLines のロジックを再利用/リファクタ
  - ファイル: `Services/StandupGeneratorService.cs`

- [ ] 1-3. トークン予算の管理
  - プロジェクト数 x コンテキストファイル数でトークンが膨れる可能性
  - 各ファイルの読み込みにトークン上限を設定 (例: focus_history は 500 トークン相当)
  - プロジェクトの優先順位付け: FocusAge が新しい順、Today Queue にタスクがある順
  - 上限を超えるプロジェクトは箇条書きフォールバック
  - ファイル: `Services/StandupGeneratorService.cs`

### Phase 2: LLM による standup 生成

- [ ] 2-1. `GenerateSmartStandupAsync()` を実装
  - CollectStandupDataAsync() で収集したデータを LLM に渡す
  - 出力は完成した Markdown standup 全文
  - ファイル: `Services/StandupGeneratorService.cs`

- [ ] 2-2. System Prompt の設計
  - ロール: "daily standup generator for a multi-project manager"
  - 出力ルール: Markdown、セクション構造、プロジェクト名プレフィックス
  - トーン: 簡潔、行動指向、優先順位が明確
  - ファイル: `Services/StandupGeneratorService.cs` 内に定数定義

- [ ] 2-3. User Prompt の組み立て
  - プロジェクトごとのデータブロック (focus, decisions, tasks, awareness signals)
  - Today Queue の全タスク (期日・優先度付き)
  - 今日の日付、曜日
  - ファイル: `Services/StandupGeneratorService.cs`

- [ ] 2-4. GenerateAndSaveAsync() の分岐ロジック
  - AI 有効時: CollectStandupDataAsync → GenerateSmartStandupAsync → 保存
  - AI 無効時: 既存のテンプレート生成 → 保存 (変更なし)
  - AI 呼び出し失敗時: テンプレート生成にフォールバック (エラーをログに記録、ユーザーには見せない)
  - ファイル: `Services/StandupGeneratorService.cs`

### Phase 3: スケジューラとトリガーの調整

- [ ] 3-1. スケジューラの LLM 対応
  - 自動生成 (タイマー) 時も AI 有効なら Smart Standup を生成
  - API 呼び出しのタイムアウトを考慮 (通常のテンプレート生成は瞬時、LLM は数秒かかる)
  - ファイル: `Services/StandupGeneratorService.cs`

- [ ] 3-2. Command Palette の standup コマンド拡張
  - 既存: ファイルが存在すればそのまま開く
  - 追加: Shift+Enter or 別コマンドで「再生成」(既存ファイルを上書き)
  - 用途: 午前中にテンプレート版で生成された後、AI 設定してから再生成したい場合
  - ファイル: `ViewModels/CommandPaletteViewModel.cs`

- [ ] 3-3. Command Palette に "standup-regen" コマンドを追加
  - 既存の standup ファイルを削除してから TryGenerateTodayAsync を呼ぶ
  - 「AI 有効にしたから再生成したい」ユースケース
  - ファイル: `ViewModels/CommandPaletteViewModel.cs`

### Phase 4: 出力の品質向上

- [ ] 4-1. Workstream 対応
  - Workstream を持つプロジェクトは Workstream 単位で Yesterday / Today を記述
  - focus_history, decision_log を Workstream パスからも収集
  - ファイル: `Services/StandupGeneratorService.cs`

- [ ] 4-2. Awareness セクションの実装
  - FocusAge > N日 (設定可能、デフォルト7日) のプロジェクトを検出
  - 未コミット変更のあるプロジェクトを検出
  - tensions.md に未解決項目があるプロジェクトを検出
  - AI 無効時もテンプレート版に Awareness セクションを追加 (箇条書き)
  - ファイル: `Services/StandupGeneratorService.cs`

- [ ] 4-3. 曜日による出力調整
  - 月曜日: Yesterday → "Last Friday" + 週末の変更をまとめる (金土日の3日分)
  - 金曜日: This Week → "Next Week Preview" (来週の期限タスクを含める)
  - ファイル: `Services/StandupGeneratorService.cs` (プロンプト内で指示)

### Phase 5: エラーハンドリング

- [ ] 5-1. LLM API 失敗時のフォールバック
  - API エラー → 既存テンプレート生成にフォールバック
  - フォールバックした旨をファイル末尾にコメントとして記録
  - `<!-- Generated: template (LLM unavailable) -->`
  - ファイル: `Services/StandupGeneratorService.cs`

- [ ] 5-2. トークン上限超過の処理
  - プロジェクト数が多い場合、LLM のコンテキスト上限に達する可能性
  - 優先順位の低いプロジェクト (FocusAge が古い、Today Queue にタスクなし) を段階的に除外
  - 除外したプロジェクトはテンプレート形式の箇条書きとして末尾に追記
  - ファイル: `Services/StandupGeneratorService.cs`

- [ ] 5-3. 空データの処理
  - Yesterday のデータがゼロ (focus_history なし、decision_log なし、完了タスクなし) の場合
  - LLM に渡さず「No activity recorded yesterday.」とだけ出力
  - ファイル: `Services/StandupGeneratorService.cs`

## プロンプト設計 (詳細)

### System Prompt

```
You are a daily standup generator for a professional managing multiple parallel projects.

## Output rules
- Output ONLY the standup Markdown. No preamble, no explanation.
- Use the exact section structure: ## Yesterday, ## Today, ## This Week, ## Awareness
- Under Yesterday: group by project, write 1-3 sentences per project summarizing what was done.
- Under Today: numbered list, ordered by priority. Bold the project name. Include due labels.
- Under This Week: bullet list of upcoming tasks with due labels.
- Under Awareness: bullet list of items needing attention (stale focus, uncommitted changes, unresolved tensions). Omit this section if nothing to report.
- Keep each project summary concise (max 3 sentences).
- Use an action-oriented tone. Focus on outcomes, not process.
- If a project had no activity yesterday, omit it from Yesterday (do not write "no activity").
- Highlight overdue tasks with urgency.
- When Monday, aggregate Friday through Sunday under Yesterday.
- Decision log entries: summarize the decision and its reasoning in one sentence.
- Preserve project names exactly as given.
- Write in the same language as the context files (typically Japanese for Japanese users, English otherwise). If mixed, prefer the language used in current_focus.md.
```

### User Prompt 構造

```
## Date
{today: YYYY-MM-DD (Day of week)}

## Project Data

### {ProjectName} (Tier: {tier}, Category: {category})

#### Yesterday - Focus changes
Previous focus ({day-before-yesterday}):
{focus_history content or "(none)"}

Yesterday focus ({yesterday}):
{focus_history content or "(none)"}

#### Yesterday - Decisions
{For each decision_log file:}
File: {filename}
Context: {context section}
Chosen: {chosen section}

{Or "(none)"}

#### Yesterday - Completed tasks
{task list or "(none)"}

#### Current focus (for today's priorities)
{current_focus.md content, truncated to budget}

#### Today Queue
{tasks with due labels, sorted by priority}

#### Awareness signals
- Focus age: {N} days
- Uncommitted changes: {yes/no, repo count}
- Open tensions: {yes/no}

{Repeat for each project}

## Instruction
Generate the daily standup following the output rules.
Prioritize projects with overdue tasks or today-due items.
```

## ファイル追加/変更一覧

### 新規ファイル

| ファイル | 説明 |
|---|---|
| `Models/StandupModels.cs` | StandupProjectData 等の構造化モデル |

### 変更ファイル

| ファイル | 変更内容 |
|---|---|
| `Services/StandupGeneratorService.cs` | CollectStandupDataAsync, GenerateSmartStandupAsync 追加、GenerateAndSaveAsync に分岐ロジック |
| `ViewModels/CommandPaletteViewModel.cs` | "standup-regen" コマンド追加 |

## 実装順序

Phase 1 (データ収集) → Phase 2 (LLM 生成) → Phase 3 (トリガー調整) → Phase 4 (品質向上) → Phase 5 (エラー処理)

Phase 1-2 で最小限動作する Smart Standup が完成。
Phase 3 以降は改善。

## AI Decision Log 計画との関係

| 観点 | Smart Standup | AI Decision Log |
|---|---|---|
| LlmClientService | 共通利用 | 共通利用 |
| 入力データ | 全プロジェクト横断 | 単一プロジェクト |
| 出力先 | standup/{date}_standup.md | decision_log/{date}_{topic}.md |
| Refine フロー | なし (一発生成) | あり (反復修正) |
| フォールバック | テンプレート生成 | 空テンプレート |
| トリガー | 自動 (スケジューラ) + 手動 | 手動のみ |
| Workstream | 集約表示 | 個別選択 |

両機能は独立して実装可能。共通の依存は LlmClientService のみ。
