# Workstream 機能 計画書

## 概要

大きめのプロジェクトや _INHOUSE のように、1つのプロジェクト内に複数の仕事の塊(Workstream)が存在するケースに対応する。
ディレクトリ規約ベース(A案)で、既存のプロジェクト構造を壊さずに段階的に導入する。

## 用語定義

- Workstream: プロジェクト内の独立した仕事の塊。独自の current_focus.md と decision_log を持つ
- 親プロジェクト: Workstream を包含する既存の ProjectInfo

## ディレクトリ規約

```
SomeProject/
└── _ai-context/
    └── context/
        ├── current_focus.md            ← プロジェクト全体のフォーカス (従来通り)
        ├── project_summary.md
        ├── decision_log/
        ├── focus_history/
        └── workstreams/
            ├── infra-team-ops/
            │   ├── current_focus.md    ← この Workstream 専用
            │   ├── decision_log/
            │   └── focus_history/
            └── data-migration/
                ├── current_focus.md
                ├── decision_log/
                ├── focus_history/
                └── _closed             ← 空ファイル。存在するだけで完了済みを示す
```

- Workstream ディレクトリ名はケバブケース(英数字とハイフン)を推奨
- `workstreams/` ディレクトリが存在しなければ Workstream なしとして従来通り動作
- 各 Workstream は最低限 `current_focus.md` を持つ。decision_log / focus_history はオプション
- `_closed` ファイルを置くだけで完了済み扱いになる。ディレクトリ名は変えない
- `_closed` を削除すれば即アクティブに戻る。AI エージェントも `touch _closed` / `rm _closed` で操作可能
- 既存アプリの `.git/forCodex` マーカーファイルと同じ規約

## データモデル

### 新規: WorkstreamInfo クラス

```csharp
// Models/WorkstreamInfo.cs
namespace ProjectCurator.Models;

public class WorkstreamInfo
{
    public string Id { get; set; } = "";          // ディレクトリ名 (e.g. "infra-team-ops")
    public string Label { get; set; } = "";       // 表示名 (= Id をタイトルケース化、または workstream.json から)
    public string Path { get; set; } = "";        // フルパス: {AiContextContentPath}/workstreams/{Id}
    public bool IsClosed { get; set; }            // _closed ファイルが存在するか
    public string? FocusFile { get; set; }
    public int? FocusAge { get; set; }
    public int? FocusTokens { get; set; }
    public int DecisionLogCount { get; set; }
    public List<DateTime> FocusHistoryDates { get; set; } = [];
    public List<DateTime> DecisionLogDates { get; set; } = [];
}
```

### 既存変更: ProjectInfo

```csharp
// ProjectInfo.cs に追加
public List<WorkstreamInfo> Workstreams { get; set; } = [];
public bool HasWorkstreams => Workstreams.Count > 0;
```

### オプション: workstream.json (表示名のカスタマイズ用)

```json
// _ai-context/context/workstreams/workstream.json
{
  "infra-team-ops": { "label": "基盤チーム運営" },
  "data-migration": { "label": "データ移行" }
}
```

- このファイルは任意。なければディレクトリ名をそのまま表示名にする
- AI エージェントから見ても単純な JSON なので読み書きしやすい

---

## フェーズ構成

### Phase 1: データモデルとディスカバリ

目的: Workstream をスキャンして ProjectInfo に載せる。UI はまだ変えない。

対象ファイル:
- Models/WorkstreamInfo.cs (新規作成)
- Models/ProjectInfo.cs (Workstreams プロパティ追加)
- Services/ProjectDiscoveryService.cs (スキャンロジック拡張)

変更内容:

- [x] WorkstreamInfo クラスを新規作成
- [x] ProjectInfo に `List<WorkstreamInfo> Workstreams` を追加
- [x] ProjectDiscoveryService の `BuildProjectInfo()` (L481付近) を拡張:
  - [x] `{AiContextContentPath}/workstreams/` が存在するかチェック
  - [x] 存在すれば各サブディレクトリを WorkstreamInfo として構築
  - [x] FocusAge, DecisionLogCount, FocusHistoryDates, DecisionLogDates を個別に収集
  - [x] workstream.json があれば Label を読み込み
  - [x] `_closed` ファイルの存在を確認して `IsClosed` に設定
- [x] App.xaml.cs への DI 登録は不要(モデルクラスのみの追加)

検証:
- [ ] デバッグ出力で Workstream が正しくスキャンされるか確認
- [ ] 既存のプロジェクト(workstreams/ なし)が従来通り動作するか確認

---

### Phase 2: Dashboard カードへの Workstream 表示

目的: プロジェクトカードに Workstream の情報を表示する。

対象ファイル:
- ViewModels/DashboardViewModel.cs (ProjectCardViewModel 拡張)
- Views/Pages/DashboardPage.xaml (カードテンプレート拡張)

変更内容:

- [x] ProjectCardViewModel に Workstream 関連プロパティを追加:
  - [x] `ObservableCollection<WorkstreamCardItem> Workstreams`
  - [x] `bool HasWorkstreams`
  - [x] `bool IsWorkstreamExpanded` (折りたたみ制御)
  - [x] `bool ShowClosedWorkstreams` (完了済みの表示トグル)
  - [x] `bool HasClosedWorkstreams` (トグルボタンの表示制御用)
  - [x] `int ActiveWorkstreamCount` (ヘッダー件数表示用)

- [x] WorkstreamCardItem クラス (DashboardViewModel.cs 内に定義):
  - FocusAgeText プロパティを追加 (計画の ActivityDays は省略、Freshness バッジで代替)

- [x] DashboardPage.xaml のカードテンプレート拡張:
  - アクションボタン行の上(Activity Bar の下)に Workstream セクションを追加
  - ▶/▼ トグルボタンで展開/折りたたみ
  - 各 Workstream は 1行: ● + Label + FocusFreshness バッジ + 📝件数
  - IsClosed=true は Opacity 0.4 で薄く表示
  - "Show closed..." / "Hide closed" トグルを最下部に配置

- [x] ProjectCardViewModel のコンストラクタで WorkstreamInfo → WorkstreamCardItem の変換を追加

検証:
- [ ] workstreams/ ディレクトリを持つプロジェクトでカードに Workstream が表示されるか
- [ ] 折りたたみの開閉が動作するか
- [ ] IsClosed=true の Workstream が薄く表示されるか
- [ ] 「閉じた Workstream を表示/非表示」トグルが動作するか
- [ ] workstreams/ なしのプロジェクトで表示が崩れないか

---

### Phase 3: Editor のファイルツリーに Workstream セクション追加

目的: Workstream 内の .md ファイルを Editor で閲覧・編集できるようにする。

対象ファイル:
- ViewModels/EditorViewModel.cs (BuildFileTree 拡張)
- Views/Pages/EditorPage.xaml.cs (保存ロジック拡張)

変更内容:

- [x] EditorViewModel の `BuildFileTree()` (L203付近) を拡張:
  - [x] 既存セクション(decision_log, focus_history, etc.)の後に Workstream セクションを追加
  - [x] `{AiContextContentPath}/workstreams/` が存在する場合、各サブディレクトリをツリーノードに追加
  - [x] IsClosed=true の Workstream はデフォルト折りたたみ + IsClosedWorkstream フラグで薄い表示
  - [x] 各 ws ノード配下: current_focus.md / decision_log/ / focus_history/

- [x] EditorPage の保存ロジック拡張:
  - [x] 既存の `TakeFocusSnapshotAsync()` がパスの親ディレクトリを使うため変更不要
    (workstreams/{id}/current_focus.md → workstreams/{id}/focus_history/ に自動スナップショット)

- [x] EditorViewModel の `NewDecisionLogAsync()` を拡張:
  - [x] `GetActiveDecisionLogDir()` / `DetectWorkstreamPath()` ヘルパーを追加
  - [x] 現在開いているファイルが Workstream 配下の場合、その ws の decision_log/ に作成

- [x] EditorPage.xaml: ツリーアイテムに IsClosedWorkstream の Opacity 0.4 DataTrigger を追加

検証:
- [ ] Workstream 内のファイルがツリーに表示されるか
- [ ] Workstream 内の current_focus.md を編集・保存して focus_history にスナップショットが作られるか
- [ ] Workstream 内で新しい decision_log を作成できるか
- [ ] IsClosed=true の Workstream がツリーで薄く表示されるか
- [ ] Workstream なしのプロジェクトで従来通り動作するか

---

### Phase 4: Today Queue の Workstream 対応

目的: Asana タスクを Workstream 別にフィルタリングできるようにする。

対象ファイル:
- Services/TodayQueueService.cs (パース拡張)
- ViewModels/DashboardViewModel.cs (フィルタ拡張)

変更内容:

- [x] TodayQueueTask に Workstream 情報を追加:
  ```csharp
  public string? WorkstreamId { get; set; }
  public string? WorkstreamLabel { get; set; }
  ```

- [x] TodayQueueService の `ParseTasksFromProject()` (L115付近) を拡張:
  - [x] 従来の `{AiContextPath}/obsidian_notes/asana-tasks.md` に加えて
  - [x] `{AiContextPath}/obsidian_notes/workstreams/{name}/asana-tasks.md` も検索
  - [x] Workstream 配下のタスクには WorkstreamId/Label を設定
  - [x] IsClosed=true の Workstream のタスクは Today Queue に出さない

- [x] DashboardViewModel の ProjectFilterItems を拡張:
  - [x] 「ProjectName / WorkstreamLabel」の複合フィルタアイテムを追加

注意: Phase 4 は Obsidian 側のディレクトリ構造にも依存するため、運用フローの検討が先に必要。
Asana のセクション/タグと Workstream のマッピングルールを決めてから詳細設計する。

検証:
- [ ] Workstream 配下の asana-tasks.md からタスクが読み込まれるか
- [ ] IsClosed の Workstream のタスクが Today Queue に出ないか
- [ ] フィルタドロップダウンに Workstream 単位のアイテムが追加されているか

---

### Phase 5 (将来): Workstream の作成・管理 UI

目的: SetupPage から Workstream を追加・クローズ・再開できるようにする。

対象ファイル:
- ViewModels/SetupViewModel.cs
- Views/Pages/SetupPage.xaml

概要のみ(詳細は Phase 1-3 完了後に設計):

- [x] SetupPage のプロジェクト選択後に「Workstream 管理」セクションを追加
- [x] Workstream の追加: 以下を同時に作成する(IDは必ず一致させる)
  - [x] `_ai-context/context/workstreams/{id}/` + `current_focus.md` テンプレート
  - [x] `shared/_work/{id}/` フォルダ
- [x] Workstream のクローズ: `_closed` ファイルを作成するボタン(確認ダイアログあり)
- [x] Workstream の再開: `_closed` ファイルを削除するボタン
- [x] workstream.json の Label 編集 UI
- 完全削除は UI から行わない。ファイルシステムで手動操作する方針

---

## 設計判断

### Q1: Workstream のアクティビティは親プロジェクトに集約するか？

回答: 両方表示する。
- 親プロジェクトの ActivityDays には Workstream のアクティビティも含める(全体の活動度が分かる)
- 各 Workstream のカード行にも個別の FocusFreshness を表示する(どの仕事が滞っているか分かる)

### Q2: Editor で Workstream を選ぶ UI は？

回答: ファイルツリー内にセクションとして表示する。ドロップダウンは追加しない。
- 理由: 現在のプロジェクト切替ドロップダウンに Workstream の階層を入れると複雑になる
- ファイルツリーに `workstreams/` セクションがあれば自然にナビゲートできる

### Q3: Workstream ディレクトリの自動作成は？

回答: Phase 1-3 では手動作成(ユーザーまたは AI エージェントがディレクトリを mkdir する)。Phase 5 で UI を作る。
- 理由: まず規約を固めて運用してみてから UI を作る方が手戻りが少ない

### Q4: workstream.json は必須か？

回答: 任意。なければディレクトリ名を Label として使う。
- ケバブケースのディレクトリ名でも十分識別可能
- 日本語ラベルが欲しい場合のみ workstream.json を作成

### Q5: Dashboard カードの Workstream 表示はデフォルト折りたたみか？

回答: デフォルト折りたたみ。
- カードが縦に伸びすぎると一覧性が下がる
- Workstream を持つカードにはインジケータ(Workstream 数)を表示し、展開を促す

### Q6: Workstream の完了/削除はどう管理するか？

回答: `_closed` マーカーファイルで管理する。完全削除は UI から行わない。
- Workstream ディレクトリ内に `_closed` という空ファイルを置くだけで完了済み扱いになる
- ディレクトリ名を変えないのでパス参照が切れない
- AI エージェントも `touch _closed` / `rm _closed` で操作できる
- 既存アプリの `.git/forCodex` マーカーファイルと同じ規約
- Dashboard/Editor では IsClosed=true の Workstream を薄く表示し、非表示トグルを提供する

---

## 影響範囲まとめ

| ファイル | Phase | 変更内容 |
|---------|-------|---------|
| Models/WorkstreamInfo.cs | 1 | 新規作成 |
| Models/ProjectInfo.cs | 1 | Workstreams プロパティ追加 |
| Services/ProjectDiscoveryService.cs | 1 | workstreams/ スキャンロジック追加 |
| ViewModels/DashboardViewModel.cs | 2 | WorkstreamCardItem, カード構築拡張 |
| Views/Pages/DashboardPage.xaml | 2 | カードテンプレートに Workstream セクション追加 |
| ViewModels/EditorViewModel.cs | 3 | BuildFileTree に workstreams/ セクション追加 |
| Views/Pages/EditorPage.xaml.cs | 3 | 保存時の focus_history パス判定拡張 |
| Services/TodayQueueService.cs | 4 | Workstream 配下の asana-tasks.md パース |
| ViewModels/SetupViewModel.cs | 5 | Workstream 作成・削除 UI |
| Views/Pages/SetupPage.xaml | 5 | Workstream 管理セクション追加 |
| App.xaml.cs | - | 変更不要(モデル追加のみ、DI 登録なし) |

## 実装優先度

Phase 1 → Phase 3 → Phase 2 → Phase 4 → Phase 5 の順が効率的。
- Phase 1 (データモデル) は全ての基盤
- Phase 3 (Editor) は Phase 2 (Dashboard) より先に着手した方がよい。Workstream の current_focus.md を編集できないと運用が始まらないため
- Phase 4, 5 は運用結果を見てから

## リスクと注意点

- workstreams/ 配下のディレクトリ数が多すぎるとスキャン時間に影響する。実用上は 1プロジェクトあたり 5-10 個が上限の想定
- 既存プロジェクトとの後方互換性: workstreams/ がなければ完全に従来通り動作する設計にする
- Obsidian 側のノート構造と Workstream の整合性は Phase 4 で検討する。Phase 1-3 では Obsidian 連携には手を入れない
- `_ai-context/context/workstreams/{id}/` と `shared/_work/{id}/` のフォルダ名は必ず一致させる。手動で片方だけ作ると乖離するため、Phase 5 の作成 UI では両方を同時に作成する設計にする。Phase 1-3 の手動運用期間中も同じ ID で作ることをルールとして守る

---

## shared/_work フォルダ構成

`shared/` は Box との junction。`_work/` はそのフォルダ内に置くため Box で同期される。

### Workstream あり: 3階層

```
shared/
└── _work/
    ├── infra-team-ops/        ← Workstream 名 (= _ai-context/workstreams/ の ID と一致させる)
    │   ├── 202601/
    │   │   └── 20260115_planning/
    │   └── 202603/
    │       └── 20260321_monitoring-setup/
    ├── data-migration/
    │   └── 202603/
    │       └── 20260321_schema-design/
    └── _general/              ← Workstream に属さない作業
        └── 2026/              ← _general のみ従来通り year/month/date の3階層
            └── 202603/
                └── 20260310_kickoff/
```

- Workstream フォルダ名は `_ai-context/context/workstreams/` のディレクトリ名と完全一致させる (Phase 5 の UI で同時作成することで保証する)
- YearMonth フォルダ(`202603/`)で月単位にまとめ、フォルダ増殖を防ぐ
- 年をまたいでも同じ Workstream フォルダ配下に `202701/` と続けるだけでよい

### Workstream なし (既存プロジェクト): 従来通り

```
shared/
└── _work/
    └── 2026/
        └── 202603/
            └── 20260321_xxx/
```

- Workstream を導入しないプロジェクトは変更不要

### タスクフォルダの命名規則

```
20260321_short-description/
```

- `YYYYMMDD_` プレフィックスは維持(エクスプローラーで時系列ソートされるため)
- アンダースコア以降は英小文字ケバブケース推奨(日本語も許容)
- 1フォルダ = 1タスク/1成果物 が基本単位

### Workstream クローズ時の _work フォルダ

`_closed` マーカーを置いても `_work/` 配下のフォルダはそのまま残す。Box 同期済みのファイルを動かすリスクを避けるため、アーカイブ操作は行わない。
