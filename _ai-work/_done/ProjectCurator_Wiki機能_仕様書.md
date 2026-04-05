# ProjectCurator Wiki 機能 仕様書

> **Version:** 0.1.0 (Draft)
> **Date:** 2026-04-05
> **Base:** Karpathy's LLM Wiki Pattern
> **Target:** ProjectCurator v0.1.0+

---

## 1. 概要

### 1.1 背景

ProjectCurator は現在、プロジェクトのコンテキスト管理（current_focus.md、decision_log、tasks.md）に特化している。しかし、プロジェクトを進める中で蓄積される知識（設計判断の理由、技術的な調査結果、障害対応のノウハウ、要件の変遷）は、これらのコンテキストファイルに収まりきらない。

Karpathy の「LLM Wiki」パターンは、RAG（毎回ゼロから検索・再合成）ではなく、LLM が**永続的な Wiki を段階的に構築・保守する**アプローチを提唱している。この考え方を ProjectCurator に「Wiki」タブとして統合し、プロジェクト単位のナレッジベースをアプリ内で運用できるようにする。

### 1.2 コアコンセプト

```
従来の RAG:  ソース → 毎回検索 → 毎回合成 → 使い捨ての回答
LLM Wiki:    ソース → LLM が Wiki に統合 → 知識が蓄積 → 回答は Wiki ベース
```

ProjectCurator における位置づけ：

| レイヤー | Karpathy 定義 | ProjectCurator 実装 |
|---------|--------------|-------------------|
| Raw Sources | 不変のソース文書群 | `wiki/raw/` ディレクトリ |
| Wiki | LLM が生成・保守する Markdown 群 | `wiki/pages/` ディレクトリ |
| Schema | LLM への運用指示書 | `wiki/wiki-schema.md`（CLAUDE.md / AGENTS.md 連携） |

### 1.3 ユーザーストーリー

- プロジェクトの「Wiki」タブを開くと、そのプロジェクトの Wiki ページ一覧と検索バーが表示される
- 新しいソース（議事録 / 設計書 / 調査メモ）をドロップすると、LLM が読み取り → 要約ページ作成 → 関連ページ更新を自動で行う
- 質問を入力すると、Wiki を参照して回答が生成され、有用な回答は新しい Wiki ページとして保存できる
- 定期的に「Lint」を実行して、矛盾検出・陳腐化チェック・孤立ページ検出ができる

---

## 2. UI 設計

### 2.1 ナビゲーション

既存のサイドバーに「Wiki」アイコン（📚 / SymbolRegular.Book24）を追加する。配置は Timeline と Git Repos の間。

```
Dashboard
Editor
Timeline
📚 Wiki        ← 新規追加
Git Repos
Asana Sync
Agent Hub
Setup
Settings
```

### 2.2 Wiki ページ構成

Wiki タブは3つのサブビューで構成する。タブ上部に切替用のセグメントコントロールを配置。

#### (A) Pages ビュー（デフォルト）

```
┌─────────────────────────────────────────────────┐
│ [Pages]  [Query]  [Lint]           🔍 検索バー   │
├──────────────┬──────────────────────────────────┤
│ ページツリー   │ Markdown プレビュー               │
│              │                                  │
│ 📄 index     │  # GRANDIT カスタマイズ仕様        │
│ 📄 log       │                                  │
│ ─────────── │  ## 概要                          │
│ 📁 sources   │  本プロジェクトでは受注管理モジュー  │
│  📄 mtg-0401 │  ルに対して以下のカスタマイズを...   │
│  📄 mtg-0328 │                                  │
│ 📁 entities  │  ## 関連ページ                     │
│  📄 顧客マスタ │  - [[受注ワークフロー]]            │
│  📄 受注伝票  │  - [[承認フロー設計]]              │
│ 📁 concepts  │                                  │
│  📄 承認フロー │                                  │
│ 📁 analysis  │                                  │
│              │                                  │
├──────────────┴──────────────────────────────────┤
│ [+ ソース追加]  [Ingest 実行]  ステータスバー      │
└─────────────────────────────────────────────────┘
```

- 左ペイン：カテゴリ別のページツリー（index.md / log.md は固定表示）
- 右ペイン：選択ページの Markdown プレビュー（AvalonEdit ベース、既存 Editor と同じレンダリング）
- `[[wikilink]]` 形式のリンクをクリックで対応ページにジャンプ
- 右ペインのツールバーに「編集」「ソースで開く（Obsidian）」ボタン

#### (B) Query ビュー

```
┌─────────────────────────────────────────────────┐
│ [Pages]  [Query]  [Lint]                         │
├─────────────────────────────────────────────────┤
│                                                  │
│  質問を入力してください:                           │
│  ┌─────────────────────────────────────────────┐ │
│  │ この案件で受注伝票の承認フローはどう設計した？   │ │
│  └─────────────────────────────────────────────┘ │
│  [送信]  [Wiki ページとして保存]                   │
│                                                  │
│  ── 回答 ──────────────────────────────────────  │
│  受注伝票の承認フローは以下の通り設計されています。  │
│  ...                                             │
│  📎 参照ページ: 承認フロー設計, 受注伝票           │
│                                                  │
│  ── 過去の Q&A ─────────────────────────────────  │
│  [2026-04-03] テスト環境の DB 接続先は？           │
│  [2026-04-01] バッチ処理の実行順序は？             │
│                                                  │
└─────────────────────────────────────────────────┘
```

- LLM が `index.md` → 関連ページを読んで回答を合成
- 「Wiki ページとして保存」で `analysis/` に新規ページ作成
- 過去の Q&A 履歴を時系列で表示

#### (C) Lint ビュー

```
┌─────────────────────────────────────────────────┐
│ [Pages]  [Query]  [Lint]                         │
├─────────────────────────────────────────────────┤
│                                                  │
│  [🔍 Lint 実行]        最終実行: 2026-04-04 18:30 │
│                                                  │
│  ⚠️ 矛盾検出 (2件)                               │
│    承認フロー設計 vs 受注ワークフロー               │
│    → 承認レベル数の記述が不一致                     │
│                                                  │
│  🔗 孤立ページ (1件)                              │
│    旧マスタ定義（インバウンドリンクなし）            │
│                                                  │
│  📅 陳腐化の可能性 (3件)                           │
│    テスト環境構成（最終更新: 45日前）               │
│                                                  │
│  💡 ページ作成の提案 (2件)                         │
│    「バッチ処理」が5ページで言及されているが         │
│    専用ページが存在しない                          │
│                                                  │
│  [一括修正を提案]  [レポート出力]                   │
└─────────────────────────────────────────────────┘
```

---

## 3. データモデル

### 3.1 フォルダ構造

各プロジェクトの `_ai-context/context/` 配下に `wiki/` ディレクトリを作成する。

```
_ai-context/
└── context/
    ├── current_focus.md       # 既存
    ├── decision_log/          # 既存
    ├── tasks.md               # 既存
    └── wiki/                  # 新規
        ├── wiki-schema.md     # Schema 層: LLM への運用指示
        ├── index.md           # ページカタログ（LLM が自動更新）
        ├── log.md             # 操作ログ（append-only）
        ├── raw/               # 不変のソース文書
        │   ├── mtg-20260401-kickoff.md
        │   ├── design-spec-v2.pdf
        │   └── assets/        # ソース内の画像等
        ├── pages/             # LLM 生成・保守の Wiki ページ
        │   ├── sources/       # ソース要約ページ
        │   ├── entities/      # エンティティページ（テーブル、画面、帳票等）
        │   ├── concepts/      # 概念・トピックページ
        │   └── analysis/      # Query から保存した分析ページ
        └── .wiki-meta.json    # Wiki メタデータ（設定、統計）
```

### 3.2 wiki-schema.md（Schema 層）

Wiki 作成時に自動生成されるテンプレート。ユーザーと LLM が共同で進化させる。

```markdown
# Wiki Schema

## プロジェクト情報
- プロジェクト名: {{PROJECT_NAME}}
- ドメイン: {{DOMAIN}}（例: ERP / Web / インフラ）

## ページ規約
- ファイル名: kebab-case（日本語タイトルの場合はローマ字または英訳）
- フロントマター: title, created, updated, sources, tags
- Wikilink: [[ページ名]] 形式
- 各ページ冒頭に TLDR（3行以内の要約）を記載

## カテゴリ定義
- sources/: ソース文書の要約。1ソース = 1ページ
- entities/: 具体的な対象物（テーブル、画面、API、帳票、ユーザーロール等）
- concepts/: 設計思想、業務ルール、ワークフロー、技術方針
- analysis/: 比較分析、質問への回答、調査結果

## Obsidian との役割分担
- この Wiki は LLM が書き、人間は読む。人間の日常メモは Obsidian 側に書く。
- Obsidian の meetings/ や notes/ が Wiki の「原料」になる。
- Ingest 対象は「プロジェクト知識として蓄積する価値があるもの」に限定する。
- 個人的な所感、一時的なリンク集、ブレストの断片は Ingest しない。
- ソースの引用元（Obsidian パスまたはファイル名）を各ページのフロントマターに記録する。

## Ingest ワークフロー
1. ソースを読み、主要な知見を抽出
2. sources/ に要約ページを作成
3. index.md を更新
4. 関連する entities/ と concepts/ ページを更新（なければ新規作成）
5. log.md にエントリを追記

## Lint ルール
- 矛盾: 同じ事実について異なる記述があればフラグ
- 陳腐化: 新しいソースが古い記述を上書きしていればフラグ
- 孤立: インバウンドリンクが 0 のページをフラグ
- 欠落: 3ページ以上で言及されているがページがないトピックを提案
```

### 3.3 index.md

```markdown
# Wiki Index

## Sources (5)
| ページ | 要約 | ソース日 | タグ |
|--------|------|---------|------|
| [[mtg-20260401-kickoff]] | キックオフ議事録 | 2026-04-01 | #meeting |
| ... | | | |

## Entities (8)
| ページ | 要約 | ソース数 | タグ |
|--------|------|---------|------|
| [[customer-master]] | 顧客マスタテーブル定義と運用 | 3 | #table #master |
| ... | | | |

## Concepts (4)
| ページ | 要約 | ソース数 | タグ |
|--------|------|---------|------|
| [[approval-flow]] | 承認フロー設計方針 | 2 | #workflow #design |
| ... | | | |

## Analysis (2)
| ページ | 要約 | 作成日 |
|--------|------|-------|
| [[batch-vs-realtime]] | バッチ vs リアルタイム処理の比較 | 2026-04-03 |
| ... | | |
```

### 3.4 log.md

```markdown
# Wiki Log

## [2026-04-03] query | バッチ処理の実行順序
- 回答を analysis/batch-execution-order として保存
- 更新: entities/batch-job-master

## [2026-04-01] ingest | キックオフ議事録
- 作成: sources/mtg-20260401-kickoff
- 作成: entities/customer-master, entities/order-slip
- 作成: concepts/approval-flow
- 更新: index.md（+4 ページ）
```

### 3.5 .wiki-meta.json

```json
{
  "version": "1.0",
  "created": "2026-04-01T09:00:00+09:00",
  "schemaVersion": "1.0",
  "stats": {
    "totalPages": 19,
    "totalSources": 5,
    "lastIngest": "2026-04-03T14:30:00+09:00",
    "lastLint": "2026-04-04T18:30:00+09:00",
    "lastQuery": "2026-04-04T10:15:00+09:00"
  },
  "settings": {
    "autoUpdateIndex": true,
    "autoAppendLog": true,
    "maxPagesBeforeSearchRequired": 100
  }
}
```

---

## 4. 操作仕様

### 4.1 Ingest（ソース取り込み）

#### トリガー
- Pages ビュー下部の「ソース追加」ボタン → ファイル選択ダイアログ
- Wiki タブへのファイルドラッグ＆ドロップ
- Editor からの「Wiki に送る」コンテキストメニュー（decision_log エントリや meeting notes）

#### 対応ファイル形式
- Markdown (.md)
- テキスト (.txt)
- PDF (.pdf) — テキスト抽出後に処理
- Word (.docx) — テキスト抽出後に処理

#### 処理フロー

```
[ソースファイル投入]
       │
       ▼
[raw/ にコピー（不変保存）]
       │
       ▼
[LLM がソースを読み取り]
       │
       ├──→ [sources/ に要約ページ作成]
       │
       ├──→ [entities/ の該当ページを更新 or 新規作成]
       │
       ├──→ [concepts/ の該当ページを更新 or 新規作成]
       │
       ├──→ [index.md を更新]
       │
       └──→ [log.md にエントリ追記]
       │
       ▼
[UI に差分サマリーを表示]
  「3ページ作成、2ページ更新」
```

#### LLM プロンプト構成

```
System:
  あなはた ProjectCurator の Wiki メンテナーです。
  以下の wiki-schema.md に従って操作してください。
  {wiki-schema.md の内容}

User:
  以下のソースを Ingest してください。

  ## 現在の index.md
  {index.md の内容}

  ## ソース内容
  {ソースファイルの内容}

  ## 指示
  1. sources/ の要約ページ（Markdown）を生成してください
  2. 更新が必要な既存ページがあれば、ページ名と更新内容を列挙してください
  3. 新規作成が必要なページがあれば、カテゴリとページ名と内容を生成してください
  4. index.md の更新差分を生成してください
  5. log.md のエントリを生成してください

  JSON 形式で返してください:
  {
    "summary": "...",
    "newPages": [{ "path": "...", "content": "..." }],
    "updatedPages": [{ "path": "...", "diff": "..." }],
    "indexUpdate": "...",
    "logEntry": "..."
  }
```

#### UI フィードバック
- 処理中はプログレスバー + 「LLM がソースを分析中...」メッセージ
- 完了後にサマリーダイアログ：作成/更新されたページ一覧、クリックで該当ページにジャンプ
- エラー時はログに記録 + トースト通知

### 4.2 Query（Wiki への質問）

#### 処理フロー

```
[ユーザーが質問を入力]
       │
       ▼
[LLM が index.md を読み、関連ページを特定]
       │
       ▼
[該当ページの内容を読み込み]
       │
       ▼
[回答を合成（参照ページを明示）]
       │
       ▼
[UI に回答を表示]
       │
       ├──→ [「Wiki ページとして保存」→ analysis/ に保存]
       └──→ [log.md にクエリ記録]
```

#### 検索戦略

Wiki のスケールに応じて2段階の検索戦略を採用する。

| スケール | ページ数 | 検索方式 |
|---------|---------|---------|
| Small | ~100 | index.md の全文を LLM に渡して関連ページを判断 |
| Medium+ | 100~ | ローカル全文検索（後述の SearchService）で候補を絞り → LLM に渡す |

### 4.3 Lint（ヘルスチェック）

#### チェック項目

| カテゴリ | 検出内容 | 重要度 |
|---------|---------|-------|
| 矛盾 | 同一事実に対する異なる記述 | High |
| 陳腐化 | 新しいソースにより上書きされた古い記述 | Medium |
| 孤立ページ | インバウンドリンクが 0 | Low |
| 欠落ページ | 複数ページで言及されているがページ未作成 | Medium |
| リンク切れ | 存在しないページへの wikilink | High |
| ソース欠落 | ソースファイルが raw/ に存在しない参照 | High |

#### 処理フロー

```
[Lint 実行ボタン]
       │
       ▼
[静的チェック（リンク整合性、孤立ページ）]  ← C# 側で実行、LLM 不要
       │
       ▼
[LLM チェック（矛盾検出、陳腐化、欠落提案）] ← LLM に全ページ要約を渡す
       │
       ▼
[結果を Lint ビューに表示]
       │
       └──→ [log.md に lint 結果を記録]
```

---

## 5. 実装設計

### 5.1 MVVM 構成（既存パターンに準拠）

```
Views/
  WikiPage.xaml                    # Wiki タブのメインページ
  WikiPagesView.xaml               # Pages サブビュー
  WikiQueryView.xaml               # Query サブビュー
  WikiLintView.xaml                # Lint サブビュー

ViewModels/
  WikiPageViewModel.cs             # メイン VM（サブビュー切替）
  WikiPagesViewModel.cs            # ページツリー + プレビュー
  WikiQueryViewModel.cs            # 質問 + 回答 + 履歴
  WikiLintViewModel.cs             # Lint 結果表示

Models/
  WikiProject.cs                   # プロジェクト単位の Wiki 状態
  WikiPage.cs                      # 個別ページモデル
  WikiSource.cs                    # ソースファイルモデル
  WikiLintResult.cs                # Lint 結果モデル
  WikiMeta.cs                      # .wiki-meta.json のデシリアライズ

Services/
  WikiService.cs                   # Wiki CRUD 操作（ファイル I/O）
  WikiIngestService.cs             # Ingest パイプライン
  WikiQueryService.cs              # Query パイプライン
  WikiLintService.cs               # Lint パイプライン
  WikiSearchService.cs             # ローカル全文検索（Medium+ スケール用）
  WikiLinkParser.cs                # [[wikilink]] の解析・解決
```

### 5.2 WikiService（ファイル I/O 層）

```csharp
public class WikiService
{
    // Wiki 初期化（Setup から呼び出し）
    public async Task InitializeWiki(string projectPath, string projectName, string domain);

    // ページ CRUD
    public IReadOnlyList<WikiPage> GetAllPages(string wikiRoot);
    public WikiPage? GetPage(string wikiRoot, string relativePath);
    public async Task SavePage(string wikiRoot, string relativePath, string content);
    public async Task DeletePage(string wikiRoot, string relativePath);

    // ソース管理
    public async Task<string> AddSource(string wikiRoot, string sourceFilePath);
    public IReadOnlyList<WikiSource> GetAllSources(string wikiRoot);

    // メタデータ
    public WikiMeta GetMeta(string wikiRoot);
    public async Task UpdateMeta(string wikiRoot, Action<WikiMeta> update);

    // Index / Log
    public string GetIndex(string wikiRoot);
    public async Task UpdateIndex(string wikiRoot, string newContent);
    public async Task AppendLog(string wikiRoot, string entry);
}
```

### 5.3 WikiIngestService（LLM 連携）

```csharp
public class WikiIngestService
{
    private readonly LlmService _llm;           // 既存の LLM サービス
    private readonly WikiService _wiki;

    public async Task<IngestResult> IngestSource(
        string wikiRoot,
        string sourceFilePath,
        IProgress<string>? progress = null)
    {
        // 1. raw/ にコピー
        // 2. ソース内容を読み取り（PDF/DOCX の場合はテキスト抽出）
        // 3. wiki-schema.md + index.md + ソース内容で LLM プロンプト構築
        // 4. LLM 呼び出し → JSON レスポンスをパース
        // 5. ページ作成・更新をファイルシステムに反映
        // 6. index.md 更新、log.md 追記
        // 7. .wiki-meta.json 更新
        // 8. IngestResult（作成/更新ページ一覧）を返却
    }
}
```

### 5.4 既存コードとの統合ポイント

| 統合先 | 内容 |
|-------|------|
| MainWindow.xaml | NavigationView に Wiki アイテム追加 |
| MainWindow.xaml.cs | Wiki ページへのナビゲーション登録 |
| SetupPageViewModel | プロジェクト作成時に `wiki/` ディレクトリ初期化を追加 |
| LlmService（既存） | Wiki 用プロンプトを既存 LLM インフラで処理 |
| EditorPageViewModel | 「Wiki に送る」コンテキストメニューの追加 |
| DashboardCardViewModel | Wiki ページ数・最終更新日をカードに表示（任意） |

### 5.5 Setup ページへの統合

プロジェクト作成時（`Setup Project` ボタン）に、既存のフォルダ構造作成処理に以下を追加：

```csharp
// 既存: current_focus.md, decision_log/, tasks.md の作成
// 追加:
await wikiService.InitializeWiki(projectContextPath, projectName, domain);
```

初期化で作成されるファイル：
- `wiki/wiki-schema.md`（テンプレートから生成）
- `wiki/index.md`（空のカタログ）
- `wiki/log.md`（「Wiki 作成」の初期エントリ）
- `wiki/raw/`（空ディレクトリ）
- `wiki/pages/sources/`、`pages/entities/`、`pages/concepts/`、`pages/analysis/`（空ディレクトリ）
- `wiki/.wiki-meta.json`（初期メタデータ）

---

## 6. Obsidian と Wiki の役割分担

### 6.1 基本原則：「誰が書くか」で分ける

Obsidian と Wiki の最大の違いは**書き手**である。この原則を明確にすることで、両者の責務が被ることを防ぐ。

| | Obsidian（人間の層） | Wiki（LLM の層） |
|---|---|---|
| **書き手** | 人間 | LLM |
| **性質** | 思考のインボックス。雑で OK | 構造化されたナレッジベース |
| **編集頻度** | 毎日。リアルタイムにメモ | Ingest / Lint 時にまとめて更新 |
| **構造化の度合い** | 低い（自由記述） | 高い（カテゴリ・相互リンク・要約） |
| **情報の流れ** | **上流**（原料を供給する側） | **下流**（原料を構造化する側） |

```
┌─────────────────────────────────────────────────────────────────┐
│                     情報の流れ（一方向が基本）                     │
│                                                                  │
│  ┌──────────────────────┐         ┌──────────────────────┐      │
│  │  Obsidian（人間が書く）│  Ingest │  Wiki（LLM が書く）   │      │
│  │                      │ ──────→ │                      │      │
│  │  ・議事録の走り書き    │         │  ・構造化された要約    │      │
│  │  ・Box リンク集       │         │  ・エンティティページ  │      │
│  │  ・ブレスト・疑問点    │         │  ・概念・設計方針     │      │
│  │  ・個人的な所感       │         │  ・相互リンク・矛盾検出│      │
│  │  ・調査メモ          │         │  ・分析・比較         │      │
│  └──────────────────────┘         └──────────────────────┘      │
│         ↑ 人間が自由に書く               ↑ 人間は読むだけ         │
│                                         （直接編集しない）        │
└─────────────────────────────────────────────────────────────────┘
```

### 6.2 フォルダ構造による分離

Obsidian Vault 内でプロジェクトごとに「人間の領域」と「LLM の領域」を物理的に分離する。

```
Obsidian Vault/
└── Projects/
    └── MyProject/
        │
        │  ── 人間が書く領域 ──────────────────────────
        ├── notes/              # 日常メモ、思いつき、調査途中のメモ
        ├── meetings/           # 議事録の走り書き、参加者メモ
        ├── links/              # Box リンク集、SharePoint URL、参考サイト
        ├── ideas/              # ブレスト、設計案、疑問点
        │
        │  ── 人間 + AI 協働（既存）──────────────────
        └── ai-context/
            ├── current_focus.md    # 今やっていること
            ├── decision_log/       # 意思決定の記録
            ├── tasks.md            # タスク一覧
            │
            │  ── LLM が書く領域 ─────────────────────
            └── wiki/
                ├── raw/            # Ingest 元のソース（不変コピー）
                └── pages/          # LLM が生成・保守するページ群
```

### 6.3 具体的な運用フロー

#### パターン A：議事録からの知識蓄積

```
1. 会議中    → Obsidian の meetings/ に走り書き
2. 会議後    → ProjectCurator Wiki タブで「ソース追加」→ meetings/ のメモを選択
3. Ingest   → LLM が議事録を読み取り:
               - sources/ に議事録要約ページを作成
               - entities/ に「顧客マスタの仕様変更」を反映
               - concepts/ に「承認フロー」の設計方針を更新
4. 確認      → Wiki タブ or Obsidian のグラフビューで結果を確認
```

#### パターン B：調査メモからの知識蓄積

```
1. 調査中    → Obsidian の notes/ に技術調査メモ（雑でOK）
2. 一段落    → メモを Wiki に Ingest
3. Ingest   → LLM が調査結果を構造化:
               - concepts/ に「バッチ処理設計」ページを作成 or 更新
               - 既存の entities/ ページに技術制約を追記
4. 次の調査  → Wiki の Query で「バッチ処理の制約は？」→ 構造化された回答
```

#### パターン C：Box リンク集の活用

```
1. 日常      → Obsidian の links/ に Box URL + 一行メモを蓄積
2. 必要時    → リンク集自体は Wiki に Ingest しない（URL は変わりうる）
             → ただし、Box 上のドキュメントをダウンロードして Ingest するのは OK
```

### 6.4 書き分けガイドライン

| こういうとき | 書く場所 | 理由 |
|------------|---------|------|
| 会議中にメモを取る | Obsidian `meetings/` | 速度重視、雑でいい |
| 「あとで調べる」を書き留める | Obsidian `ideas/` | 未整理のインボックス |
| Box や SharePoint の URL をまとめる | Obsidian `links/` | リンクは変わりうるので Wiki 向きでない |
| 個人的な所感・不満・気づき | Obsidian `notes/` | 主観的な内容は Wiki に入れない |
| 議事録を構造化して残したい | Wiki に Ingest | LLM が要約・相互リンクしてくれる |
| 設計判断の経緯を残したい | decision_log + Wiki Ingest | decision_log は即時記録、Wiki は蓄積 |
| 「この案件の全体像は？」と聞きたい | Wiki の Query | 蓄積された知識から合成 |
| 矛盾や陳腐化を検出したい | Wiki の Lint | LLM が全ページを横断チェック |

### 6.5 Obsidian 側の操作マッピング

| 操作 | ProjectCurator Wiki タブ | Obsidian |
|------|-------------------------|---------|
| ソース Ingest（LLM パイプライン） | ◎ | ✗ |
| Wiki ページ閲覧 | ◎（Markdown プレビュー） | ◎（リッチプレビュー） |
| Wiki 全体の構造把握 | △（ツリービュー） | ◎（グラフビュー） |
| Query（Wiki への質問） | ◎（LLM 統合） | ✗ |
| Wiki ページの手動編集 | ○（AvalonEdit） | ◎（ネイティブエディタ）※原則非推奨 |
| Lint（ヘルスチェック） | ◎（LLM + 静的チェック） | ✗ |
| 日常メモ・走り書き | ✗ | ◎ |
| リンク集管理 | ✗ | ◎ |
| グラフビューで関係性可視化 | ✗ | ◎ |

### 6.6 既存ジャンクションの活用

ProjectCurator の既存アーキテクチャでは、`_ai-context/context/` が Obsidian Vault へのジャンクション経由で接続されている。wiki/ ディレクトリもこのパスに含まれるため、追加のジャンクション設定なしで Obsidian から Wiki ページを閲覧できる。

Obsidian のグラフビューでは Wiki ページ間の `[[wikilink]]` が可視化され、知識の全体構造を俯瞰できる。これは ProjectCurator のツリービューでは得られない価値であり、両ツールの明確な補完関係となる。

### 6.7 アンチパターン（やってはいけないこと）

- **Obsidian から Wiki ページを直接編集する** — LLM が保守する領域なので、次の Ingest / Lint で上書きされる可能性がある。修正が必要な場合は ProjectCurator の Query で指示するか、ソースを修正して再 Ingest する。
- **Wiki の raw/ に Obsidian のメモをリアルタイム同期する** — raw/ は不変のスナップショット。Obsidian のメモは随時更新されるため、Ingest 時点のコピーを raw/ に保存する。
- **すべてのメモを Wiki に Ingest する** — 個人的な所感、一時的なリンク集、ブレストの断片は Wiki の精度を下げる。Ingest するのは「プロジェクト知識として蓄積する価値があるもの」に限定する。

---

## 7. AI Agent 連携（Claude Code / Codex CLI）

### 7.1 CLAUDE.md / AGENTS.md への記載

プロジェクトの CLAUDE.md に Wiki の存在と利用方法を記載することで、Claude Code セッション中に Wiki を参照・更新させることが可能。

```markdown
## Wiki ナレッジベース

このプロジェクトには LLM Wiki があります。

- 場所: `_ai-context/context/wiki/`
- Wiki の運用規約: `wiki/wiki-schema.md` を参照
- ページ一覧: `wiki/index.md` を参照
- 操作ログ: `wiki/log.md` を参照

### エージェントへの指示
- コード変更が設計判断に関わる場合、wiki/ の関連ページを確認してください
- 新しい設計判断を行った場合、wiki/ の該当ページを更新してください
- 更新時は必ず index.md と log.md も更新してください
```

### 7.2 Agent Hub との統合

既存の Agent Hub 機能を使い、Wiki 操作用のサブエージェント / スキルを登録可能にする：

- `wiki-ingest` スキル：ソース取り込み手順のプロンプトテンプレート
- `wiki-query` スキル：Wiki ベースの質問回答テンプレート
- `wiki-lint` スキル：ヘルスチェック実行テンプレート

---

## 8. 段階的リリース計画

### Phase 1: 基盤（MVP）
- Wiki フォルダ構造の自動作成（Setup 連携）
- Wiki タブ + Pages ビュー（ツリー + Markdown プレビュー）
- ソースファイルの手動追加（raw/ へのコピーのみ）
- index.md / log.md の手動編集

### Phase 2: LLM 統合
- Ingest パイプライン（LLM によるソース分析 → ページ自動生成）
- Query ビュー（Wiki ベースの質問回答）
- log.md の自動追記

### Phase 3: 保守機能
- Lint ビュー（静的チェック + LLM チェック）
- Dashboard カードへの Wiki ステータス表示
- Editor からの「Wiki に送る」連携

### Phase 4: 拡張
- WikiSearchService（中規模以上の Wiki 向け全文検索）
- Obsidian の「Wiki で開く」ボタン
- 複数プロジェクト横断の「共通 Wiki」対応（トピック単位 Wiki）
- Agent Hub への Wiki スキル追加

---

## 9. 設定項目

Settings ページに以下を追加：

| 項目 | 型 | デフォルト | 説明 |
|------|---|---------|------|
| Enable Wiki | bool | true | Wiki 機能の有効/無効 |
| Auto Update Index | bool | true | Ingest 時に index.md を自動更新 |
| Auto Append Log | bool | true | 操作時に log.md を自動追記 |
| Max Pages Before Search | int | 100 | この数を超えたら全文検索を推奨 |
| Wiki Schema Template | string | (default) | wiki-schema.md の初期テンプレート選択 |

---

## 10. 制約事項・注意点

- **LLM のコスト**: Ingest は1ソースあたり数千〜数万トークンを消費する。大量ソースのバッチ Ingest にはコスト意識が必要。Settings に概算トークン表示を検討。
- **真実性の保証**: LLM が生成した Wiki ページの正確性はユーザーの確認が必要。Karpathy の Gist コメントでも指摘されている通り、ソースへの参照（citation）を各ページに明示し、事実と推論を分離する規約を wiki-schema.md に含める。
- **同時編集**: ProjectCurator と Obsidian の両方から同じファイルを編集した場合の競合。ファイル変更の監視（FileSystemWatcher）で UI をリフレッシュする対応で十分と想定。
- **エンコーディング**: SJIS 環境のソースファイル（既存 ERP ドキュメント等）は Ingest 時に UTF-8 変換を行う。
