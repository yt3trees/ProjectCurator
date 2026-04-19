# Ask Curia (横断 QA) - 仕様書

Curia が管理する全プロジェクトの Markdown 資産 (decision_log / focus_history / tasks.md / Wiki / 会議メモ) を横断して、自然文質問に引用付きで回答する機能を追加する。

WikiQueryService と同じ「2 段階 LLM 呼び出し (関連ファイル選定 → 回答生成)」方式を採用し、埋め込み DB やインデックス更新を不要にする。

## コンセプト

```
ユーザー (CommandPalette で "?" プレフィックス入力)
        │
        ▼
  CuriaQueryService
  ├─ Stage 0: 全ソースから候補メタ一覧を構築
  │     {path, sourceType, projectId, title, snippet(先頭500字), lastModified}
  │
  ├─ Stage 1: LLM にメタ一覧 + 質問を渡し、関連ファイルを最大 8 件選ばせる
  │
  ├─ Stage 2: 選ばれたファイルを全文同梱し、引用付き回答を生成
  │
  └─ 結果: AnswerText + CitationList(path, lineHint)
        │
        ▼
  CommandPaletteWindow
  (回答テキスト + 引用クリックで Editor にジャンプ)
```

- 既存の `WikiQueryService` の構造をほぼ流用する (検証済みパターン)
- 対象データを Wiki 単独から「Curia が知る全 Markdown」に拡張する
- AI 機能ガード (`AiEnabled`) に乗せる
- インデックス DB やベクトル検索は使わない

## 用語

| 用語 | 意味 |
|---|---|
| Source | 横断検索対象のデータ種別 (decision_log / focus_history / tasks / wiki / meeting_notes) |
| CandidateMeta | Stage 1 で LLM に渡すファイル 1 件分の軽量メタ情報 |
| Citation | 回答文中で引用された情報源 (ファイルパスと該当行ヒント) |
| Stage 1 / Stage 2 | LLM 呼び出しの 2 段階 (選定 / 回答) |

---

## Phase 0: 前提条件・制約

### 動作要件

- `AiEnabled = true` (Settings の Test Connection 成功後にのみ ON 可能)
- `LlmClientService` がチャット完了 API を返せる状態
- 対象プロジェクトが `ProjectDiscoveryService` で検出されていること

### スコープ外

- ファイルの書き込み・修正 (読み取り専用)
- バイナリファイル / PDF / Office ファイル (Markdown のみ対象)
- Asana API への直接問い合わせ (tasks.md の同期版を読む)
- マルチターン会話 (Phase 1 では単発質問のみ。Phase 2 で検討)

### 制限値 (初期値)

| 項目 | 値 | 理由 |
|---|---|---|
| MaxCandidates | 300 | Stage 1 のプロンプトサイズ上限。各メタ約 300 トークン × 300 = 約 90K トークン |
| MaxSelectedFiles | 8 | Stage 2 で全文同梱するファイル数。GPT-4o の 128K に余裕 |
| SnippetLength | 500 文字 | Stage 1 のメタに含める本文先頭 |
| RecencyWindowDays | 90 | 候補生成のデフォルト期間 (質問に古い日付が含まれれば拡張) |
| MaxFullContentBytes | 200_000 | Stage 2 で 1 ファイルあたり同梱する最大バイト数 (超える場合は先頭から切る) |

---

## Phase 1: データモデル

### 新規モデル (Models/CuriaQueryModels.cs)

```csharp
namespace Curia.Models;

public enum CuriaSourceType
{
    DecisionLog,
    FocusHistory,
    Tasks,
    Wiki,
    MeetingNotes,
}

public class CuriaCandidateMeta
{
    public string Path { get; set; } = "";
    public CuriaSourceType SourceType { get; set; }
    public string ProjectId { get; set; } = "";        // プロジェクト名 (ProjectShortName)
    public string Title { get; set; } = "";            // 見出し or ファイル名
    public string Snippet { get; set; } = "";          // 先頭 500 文字
    public DateTime LastModified { get; set; }
}

public class CuriaCitation
{
    public string Path { get; set; } = "";
    public CuriaSourceType SourceType { get; set; }
    public string ProjectId { get; set; } = "";
    public int? LineHint { get; set; }                 // 任意: 引用元の行番号
    public string? Excerpt { get; set; }               // 任意: 引用箇所 (1〜2 行)
}

public class CuriaAnswer
{
    public string Question { get; set; } = "";
    public string AnswerText { get; set; } = "";
    public List<CuriaCitation> Citations { get; set; } = [];
    public List<string> SelectedPaths { get; set; } = []; // Stage 1 で選ばれた全パス (デバッグ用)
    public DateTime GeneratedAt { get; set; }
}
```

---

## Phase 2: ソースアダプタ

各 Source の候補メタ生成は責務を分離するため、`ICuriaSourceAdapter` を導入する。

### インターフェース

```csharp
public interface ICuriaSourceAdapter
{
    CuriaSourceType SourceType { get; }
    Task<List<CuriaCandidateMeta>> EnumerateCandidatesAsync(
        IEnumerable<ProjectInfo> projects,
        DateTime since,
        CancellationToken ct);
    Task<string> ReadFullContentAsync(string path, CancellationToken ct);
}
```

### アダプタ実装

| アダプタ | データ源 | 流用する既存サービス |
|---|---|---|
| DecisionLogSourceAdapter | `_ai-context/decision_log/` 配下の `.md` | DecisionLogService |
| FocusHistorySourceAdapter | `_ai-context/focus_history/` 配下の `.md` | (なし、glob のみ) |
| TasksSourceAdapter | 各プロジェクトの `tasks.md` | AsanaTaskParser (タスク 1 行 = 1 候補メタとして扱う特殊版) |
| WikiSourceAdapter | Wiki 全ページ | WikiService |
| MeetingNotesSourceAdapter | `_ai-context/meeting_notes/` 配下の `.md` | (なし、glob のみ) |

`TasksSourceAdapter` は「タスク 1 件 = 1 候補メタ」で吐き出す (D2 参照)。`path` はアンカー付き形式 `{tasksFile}#{gid|line}` とし、`ReadFullContentAsync` ではそのタスクのブロックのみを返す。

### 候補絞り込み (Stage 0)

- `LastModified >= since` (デフォルト 90 日)
- 上限 `MaxCandidates` を超えたら `LastModified` 降順で切る
- ProjectDiscoveryService で hidden 扱いのプロジェクトは除外

---

## Phase 3: CuriaQueryService

WikiQueryService.cs を雛形にコピーし、対象を全アダプタに広げる。

### コンストラクタ

```csharp
public CuriaQueryService(
    LlmClientService llm,
    ConfigService configService,
    ProjectDiscoveryService discovery,
    IEnumerable<ICuriaSourceAdapter> adapters)
```

`App.xaml.cs` で各アダプタを singleton 登録 → `IEnumerable<ICuriaSourceAdapter>` で注入。

### 公開 API

```csharp
public async Task<CuriaAnswer> AskAsync(
    string question,
    CuriaQueryOptions? options,
    CancellationToken ct);
```

`CuriaQueryOptions` で「特定 SourceType に限定」「期間を広げる」を指定可能 (UI から切り替え)。

### Stage 1 プロンプト (関連ファイル選定)

```
system:
You are a retrieval assistant for a personal project management tool.
Given a list of candidate documents, pick up to 8 paths most relevant to the user's question.
Output JSON: {"paths": ["...", "..."]}.
Copy paths EXACTLY as listed (left column). Do not invent paths.

user:
Question: {question}

Candidates:
[path] [type] [project] [lastModified]
  title: ...
  snippet: ...
---
[path] [type] [project] [lastModified]
  ...
```

LLM は `paths` 配列のみを返す。パース失敗時は最新順に上位 5 件をフォールバック。

### Stage 2 プロンプト (回答生成)

```
system:
You are Curia's knowledge assistant. Answer the user's question using ONLY the provided documents.
- Cite every claim with the source path in square brackets, e.g. [path/to/file.md].
- If the documents do not contain the answer, say so explicitly.
- Respond in {LlmLanguage}.

user:
Question: {question}

Documents:
=== {path} ({sourceType}, {projectId}) ===
{full content}

=== {path2} ({sourceType2}, {projectId2}) ===
{full content2}

...
```

回答テキストから `[path:L<line>] "excerpt"` パターンを抽出して `Citations` に詰める (D4 参照)。Stage 2 プロンプトで「行番号と引用抜粋を必ず付ける」を明示する。

### キャッシュ (D1 反映)

- Stage 0 のメタ列挙結果は 10 分のメモリキャッシュ
- バックグラウンド `WarmCacheAsync()` がアプリ起動直後とアクティブ復帰時に温める
- ユーザー質問時はキャッシュを即時利用 (空 / 期限切れのみ同期生成へフォールバック)
- Stage 1 / Stage 2 はキャッシュしない (毎回新鮮)

---

## Phase 4: UI 統合 (CommandPalette)

### 起動方法

CommandPalette のクエリ先頭が `?` の場合、自然文質問モードに切り替える。

```
?昨年のAプロジェクトで決めたDB方針なんだっけ
?Bさんに依頼してた件どうなった
```

### UI フロー

1. `?` を入力すると検索結果が消え、「Ask Curia (Enter で送信)」プレースホルダー表示
2. Enter で `CuriaQueryService.AskAsync` を呼ぶ (ローディング表示)
3. 回答パネル展開:
   ```
   ┌─────────────────────────────────────┐
   │ Question: ...                       │
   ├─────────────────────────────────────┤
   │ Answer text with [citation] inline. │
   ├─────────────────────────────────────┤
   │ Sources:                            │
   │  • [Project A] decision_log/db.md   │ ← クリックで Editor 展開
   │  • [Project B] meeting/2026-04.md   │
   └─────────────────────────────────────┘
   ```
4. 引用クリック → 既存の `OnOpenInEditor` コールバックで Editor 起動

### CommandPaletteViewModel への追加

- `[ObservableProperty] bool isAskMode`
- `[ObservableProperty] CuriaAnswer? lastAnswer`
- `[RelayCommand] AskAsync(string question)`
- 引用クリックは既存の `OpenInEditor` コマンドに委譲

### キャンセル

クエリ入力欄をクリア or Escape で `CancellationTokenSource.Cancel()`。

---

## Phase 5: AI 機能ガードと設定

- `CuriaQueryService.AskAsync` の冒頭で `settings.AiEnabled` を確認、`false` なら例外 (ガード違反)
- CommandPalette の `?` モードは `IsAiEnabled` で表示制御 (AI 無効時は通常検索のまま)
- `AiEnabledChangedMessage` を `CommandPaletteViewModel` で購読し `IsAiEnabled` を更新
- Settings 画面に新規設定不要 (既存 LLM 設定を流用)

---

## Phase 6: エラーハンドリング

| ケース | 挙動 |
|---|---|
| AI 無効 | `?` モードに入れない (UI でブロック) |
| LLM API エラー | 回答パネルにエラーメッセージ表示、Citations は空 |
| Stage 1 が JSON を返さない | フォールバックで最新 5 件を選択 |
| 選ばれたファイルが既に削除されている | スキップ、残りで Stage 2 実行 |
| Stage 0 候補ゼロ (新規プロジェクトなど) | 「対象データが見つかりません」と即時応答 |
| キャンセル | `OperationCanceledException` を catch して何もしない |

---

## Phase 7: 段階的リリース

### MVP (Phase 1)

- DecisionLogSourceAdapter のみ
- CommandPalette の `?` プレフィックスで動作
- 引用クリックは未対応 (パスをコピーできるだけ)

これで「決定の検索」が即座に動く。WikiQueryService とほぼ同じため未知リスクなし。

### Phase 2

- FocusHistory / MeetingNotes / Tasks アダプタ追加
- 引用クリックで Editor ジャンプ

### Phase 3

- Wiki アダプタ追加 + WikiQueryService の吸収 (D3 参照、Step 1〜5 を厳守)
- マルチターン会話 (Wiki 同様、`ChatWithHistoryAsync` ベース)
- セッション履歴の永続化 (Wiki 既存形式は維持、横断モードは `%CONFIG%/curia_query_history/{date}.json`)

---

## 想定実装規模

| 項目 | 行数目安 |
|---|---|
| Models/CuriaQueryModels.cs | 80 |
| Services/CuriaQueryService.cs | 400 |
| Services/Adapters/*.cs (5 アダプタ) | 5 × 100 = 500 |
| ViewModels/CommandPaletteViewModel.cs 改修 | 150 |
| Views/CommandPaletteWindow.cs 改修 | 100 |
| App.xaml.cs DI 登録 | 10 |
| 合計 | 約 1,240 |

MVP (DecisionLog のみ) なら 600 行程度で動作可能。

---

## 設計決定事項 (2026-04-19 確定)

### D1. メタ生成は非同期キャッシュ更新

- Stage 0 の候補メタ一覧はバックグラウンドで温める方式を採用
- `CuriaQueryService` 起動時およびアプリアクティブ復帰時に `WarmCacheAsync()` を発火
- ユーザーが `?` を入力してから走るのではなく、既存キャッシュ (60 秒 TTL → 10 分 TTL に拡張) を即時使用
- キャッシュが空 / 期限切れの場合のみ同期生成にフォールバック
- ファイル監視 (`FileSystemWatcher`) は実装しない (10 分 TTL で十分、複雑度を避ける)

### D2. tasks.md はタスク単位で分割

- `TasksSourceAdapter` は 1 タスク = 1 `CuriaCandidateMeta` を生成
- `path` は `{tasksFile}#{asanaTaskGid or lineNumber}` 形式 (アンカー付与)
- `title` はタスクタイトル、`snippet` はタスク本文 + 親タスクタイトル + workstream + 期限
- `ReadFullContentAsync` ではタスクのブロック (タイトル + 本文 + サブタスク) のみを返す
- AsanaTaskParser を流用 (パース仕様の重複を避ける)

### D3. Wiki は CuriaQueryService に統合 (バグ厳禁)

統合方針: `WikiQueryService` を `CuriaQueryService` に吸収し、削除する。

互換性確保のための必須要件:

1. WikiPage 既存 UI の動作は完全に維持する
   - 既存の `WikiQueryService.AskAsync` 相当のメソッドを `CuriaQueryService.AskWikiOnlyAsync(wikiRoot, question, ...)` として残す
   - 内部実装は `AskAsync(question, options: { sourceTypes: [Wiki], wikiRoot: ... })` に委譲
2. 既存セッションファイル形式 (`WikiQueryRecord` + `_currentSessionFilePath`) を破壊しない
   - セッション JSON のスキーマと保存先 (`<wikiRoot>/_query_history/`) はそのまま継続
   - 新スキーマ導入は Phase 3 マルチターン対応時のみ、かつ後方互換ローダー必須
3. WikiViewModel からの呼び出し点 (会話リセット / セッション開始 / 履歴読み込み) は API シグネチャを変えない
4. 既存のページ選定プロンプト (Wiki 専用に最適化されている) は `WikiOnly` モードでは Wiki 用プロンプトを温存
   - 横断モード (`?`) でのみ新しい統合プロンプトを使う
5. WikiService 経由のドメインロック (`WikiDomainLockException`) も継続して尊重

実装手順 (バグ防止のための段階):

- Step 1: `CuriaQueryService` を新規作成、Wiki 以外のアダプタで動作確認
- Step 2: `WikiSourceAdapter` を追加、横断モード (`?`) で Wiki も検索対象に入る状態にする
- Step 3: `WikiQueryService` の API を `CuriaQueryService` の薄いラッパーに置き換え (内部委譲)
- Step 4: 既存 WikiPage の動作確認 (会話リセット / セッション読み込み / 質問 / 引用) を全パス手動検証
- Step 5: `WikiQueryService` クラスを削除し、DI 登録も削除 (削除は Step 4 の検証完了後のみ)

テスト観点 (手動):

- [ ] WikiPage で質問 → 回答取得 (旧と同等品質)
- [ ] WikiPage で会話リセット → 履歴クリア
- [ ] 既存セッション JSON ファイルの読み込み
- [ ] ドメインロック中の挙動 (例外が握りつぶされないこと)

### D4. 引用ジャンプの行番号: LLM 推定 + 後処理 grep フォールバック

二段構えで実装:

1. Stage 2 のプロンプトで「引用時は `[path:L42]` 形式で行番号を含める」よう指示
   - LLM が行番号を返したらそれを採用 (一次)
2. 行番号が無い / 不正 (該当行に該当文字列がない) 場合、後処理 grep:
   - LLM が出した引用テキスト (Excerpt) でファイルを行単位で検索
   - 完全一致 → 部分一致 (40 文字以上) → 最初の見出し行、の優先度で行番号を解決
3. それでも見つからなければ `LineHint = null` で先頭から開く

`CuriaCitation.Excerpt` を活用するため、Stage 2 プロンプトで「各引用に該当箇所を 1〜2 行抜粋して付ける」を強制する。

抜粋例:

```
Citation format: [path:L<line>] "excerpt up to 120 chars"
Example: [decision_log/db.md:L42] "Decided to use PostgreSQL because..."
```

回答パーサー側の正規表現:

```
\[(?<path>[^\]:]+)(?::L(?<line>\d+))?\](?:\s*"(?<excerpt>[^"]{1,200})")?
```

### D5. プロジェクト名曖昧性の解決

採用方針: 内部 ID は安定パス由来、表示は曖昧時のみ補助情報を付ける。

1. 内部 ID (`CuriaCandidateMeta.ProjectId`)
   - `ProjectInfo.RootPath` の絶対パスを正規化したものを SHA1 → 先頭 12 文字で `ProjectStableId` を生成
   - 既存の `ProjectShortName` とは別に `ProjectStableId` を持たせる
2. LLM への提示時 (Stage 1 / Stage 2 のメタとプロンプト)
   - 通常: `[ProjectShortName]`
   - 同名衝突時のみ: `[ProjectShortName | parentDirName]` (例: `[Alpha | Local]` vs `[Alpha | Box]`)
3. UI 表示 (Citations)
   - 同名衝突がある場合のみ親ディレクトリ名を併記
   - 衝突判定は ProjectDiscoveryService の結果を `GroupBy(p => p.ShortName).Where(g => g.Count() > 1)` で算出してキャッシュ
4. 引用クリック → Editor ジャンプは `path` (絶対パス) ベースなので曖昧性の影響を受けない

これにより、内部処理は常に一意 (絶対パス) で、ユーザーが見るプロジェクト名のみ衝突時に補強する形になる。
