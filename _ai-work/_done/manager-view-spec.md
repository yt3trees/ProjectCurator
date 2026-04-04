# Manager View 仕様書 (Refined)

- 作成日: 2026-04-04
- 更新日: 2026-04-04 (レビュー反映後)
- ステータス: Draft

## 概要

プロダクト開発チームのマネージャーとして、自分が参加している Asana Project に紐づくチームメンバー全員のタスクを俯瞰できる「Manager View」機能を追加する。
既存の個人タスク管理フロー (TodayQueue / asana-tasks.md) への影響を最小限に抑えつつ、チーム全体の進捗可視化を実現する。

## 背景

- ユーザーが 2026年5月以降にプロダクト開発チームの管理担当になる
- チームには複数の Asana Project がある (機能整理、課題管理、開発Todo など)
- マネージャーとして担当者別のタスク状況を朝会等で素早く確認したい
- 自分がコラボレーター (membership) に入っていない Asana Project は管理対象外とする

## 設計方針

### 1. asana_config.json のスキーマ拡張 (team_view セクション)

既存の `asana_project_gids` (自分用) とは別に、チーム全体をスキャンするための `team_view` セクションを導入する。

```json
{
  "asana_project_gids": ["gid_personal_1"],
  "team_view": {
    "enabled": true,
    "project_gids": ["gid_team_a", "gid_team_b"],
    "workstream_project_gids": {
      "infra-ops": ["gid_team_c"]
    }
  },
  "workstream_project_map": { ... },
  "workstream_field_name": "workstream-id",
  "anken_aliases": []
}
```

- `team_view.enabled: true` のとき、対象の Project GIDs から全メンバーのタスクを取得する
- `project_gids` (全体) と `workstream_project_gids` (WS単位) の合算を取得対象とする

### 2. コラボレーター (Membership) チェック

自分がメンバーに入っている Project のみ同期対象とする。

- Asana API: `GET /projects/{gid}/memberships` で自分の `user_gid` が含まれるかチェック
- 呼出し負荷軽減のため、結果を `team_membership_cache.json` (TTL: 24h) に保存する

### 3. タスク取得と出力ファイル

| ファイル名 | 取得対象 | 出力条件 | 用途 |
|---|---|---|---|
| `asana-tasks.md` | 自分のタスクのみ (Owner/Collab) | 常に更新 | 既存の TodayQueue 用 |
| `team-tasks.md` | `team_view` 対象 Project の全未完了タスク | `enabled: true` 時のみ | プロジェクト個別のチームビュー |

- **Manager View 用取得条件**: `completed_since = "now"` (未完了のみ)
- **ソート順**: 担当者ごとのセクション内で「期限昇順 (期限なしは末尾)」

### 4. team-tasks.md の出力フォーマット

既存の `asana-tasks.md` と整合性を保つ。

```markdown
# Team Tasks: {ProjectCuratorName}
Last Sync: 2026-04-04 09:00:00

## 田中太郎
- [ ] ログイン設計 (Due: 2026-04-08) [機能整理] [[Asana](https://app.asana.com/0/0/{gid})]
- [ ] API仕様書 (Due: 2026-04-10) [機能整理] [[Asana](https://app.asana.com/0/0/{gid})]

## 鈴木花子
- [ ] #142 決済バグ (Due: 2026-04-02) ⚠ [課題管理] [[Asana](https://app.asana.com/0/0/{gid})]

## Unassigned
- [ ] #140 調査中 [課題管理] [[Asana](https://app.asana.com/0/0/{gid})]
```

- `⚠`: 期限超過タスクに付与

## UI

### 1. Asana Sync ページ (設定)

`Setup` ダイアログではなく、`Asana Sync` ページの `Asana Config Editor` 内に Team View 設定を追加する。

- Team View 有効化トグル
- 全体対象 Project GIDs 入力
- Workstream 単位の Project GIDs マッピング入力

### 2. Dashboard プロジェクトカード

`team_view.enabled` なプロジェクトに `[Team]` ボタンを表示。

```
┌─────────────────────────────────────┐
│  プロダクト開発              [●] Full│
│  development/source                  │
│                                      │
│  📝 12  ⚡ 3未コミット               │
│                                      │
│  [Editor] [Timeline] [Team]          │
└─────────────────────────────────────┘
```

`team_view.enabled: false` のカードは [Team] ボタンなし (既存と同じ)。

### 3. Team View ポップアップ

`[Team]` ボタン押下で表示。`team-tasks.md` をパースして担当者カードを並べる。

```
┌──────────────────────────────────────────────────────┐
│  Team View - プロダクト開発              [Sync] [×]  │
│ ────────────────────────────────────────────────────  │
│                                                        │
│  ┌────────────────────┐  ┌────────────────────┐      │
│  │ 田中 (5)            │  │ 鈴木 (3)            │      │
│  │────────────────────│  │────────────────────│      │
│  │ ●機 □ ログイン設計  │  │ ●課 □ #142 決済    │      │
│  │      Due: 04/08    │  │      Due: 04/07 ⚠  │      │
│  │ ●機 □ API仕様      │  │ ●課 □ #138 表示    │      │
│  │      Due: 04/10    │  │      Due: 04/09    │      │
│  │ ●開 □ unit test    │  │ ●開 □ DB migration │      │
│  │      Due: 04/12    │  │      Due: 04/11    │      │
│  └────────────────────┘  └────────────────────┘      │
│                                                        │
│  ┌────────────────────┐  ┌────────────────────┐      │
│  │ 佐藤 (2)            │  │ Unassigned (1)      │      │
│  │────────────────────│  │────────────────────│      │
│  │ ●機 □ 画面設計      │  │ ●課 □ #140 調査中  │      │
│  │      Due: 04/15    │  │      Due: -        │      │
│  └────────────────────┘  └────────────────────┘      │
│                                                        │
│  ●機=機能整理  ●課=課題管理  ●開=開発Todo  ⚠=期限超過│
│  Last Sync: 2026-04-04 08:45                          │
└──────────────────────────────────────────────────────┘
```

- **担当者カード**: タスク数が多い人を優先して並べる
- **タスク一覧**: チェックボックス (表示のみ)、タスク名、Due、Projectタグ
- **アクション**:
  - [Sync]: `AsanaSyncService.RunTeamSyncAsync` を即時実行して再描画
  - [×]: ポップアップを閉じる
- **期限超過**: Due が今日以前の場合は ⚠ アイコンで強調

### 4. Asana Sync ページ (Team View 設定)

Asana Sync ページの Config Editor 内に Team View セクションを追加する。
Asana Project の紐づけは Project 全体レベルと workstream レベルの両方で設定できる。

```
┌──────────────────────────────────────────────────────┐
│  Asana Sync                                          │
│ ────────────────────────────────────────────────────  │
│  ... 既存の設定項目 ...                                │
│                                                        │
│  Team View  [ OFF / ON ]                             │
│                                                        │
│  (ON のとき追加表示)                                   │
│                                                        │
│  Asana Projects (Project 全体)                       │
│  ┌──────────────────────────────────────┐            │
│  │ 機能整理   gid: 111...          [-]  │            │
│  │ 課題管理   gid: 222...          [-]  │            │
│  └──────────────────────────────────────┘            │
│  [+ Add]                                             │
│                                                        │
│  Asana Projects (workstream 単位)                    │
│  ┌──────────────────────────────────────┐            │
│  │ workstream: 総合テスト               │            │
│  │   開発Todo   gid: 333...        [-]  │            │
│  │   [+ Add to this workstream]         │            │
│  │──────────────────────────────────────│            │
│  │ workstream: 結合テスト               │            │
│  │   (未設定)                           │            │
│  │   [+ Add to this workstream]         │            │
│  └──────────────────────────────────────┘            │
│                                                        │
│                                       [Save]          │
└──────────────────────────────────────────────────────┘
```

- Project 全体の Asana Projects → 全 workstream に共通して適用
- workstream 単位の Asana Projects → その workstream のタスクのみに適用
- 両方設定されている場合は合算して取得する

## 内部実装

### 共通モデルの統合
- `Models/AsanaProjectConfig.cs` を新設し、Service と ViewModel で重複している定義を一本化する。

### Service 設計
- `AsanaSyncService.RunTeamSyncAsync`: チームタスク専用の同期メソッドを追加。
- `Services/TeamTaskParser.cs`: `team-tasks.md` を UI 表示用にパースするクラスを新設。

### キャッシュ
- `team_membership_cache.json` による Membership チェックのスキップ実装。

## 変更対象ファイル

- `Models/AsanaProjectConfig.cs` [NEW]
- `Models/TeamTaskModels.cs` [NEW]
- `Services/AsanaSyncService.cs` [MODIFY]
- `Services/TeamTaskParser.cs` [NEW]
- `ViewModels/AsanaSyncViewModel.cs` [MODIFY]
- `ViewModels/DashboardViewModel.cs` [MODIFY]
- `Views/Pages/AsanaSyncPage.xaml` [MODIFY]
- `Views/Pages/DashboardPage.xaml` [MODIFY]
- `Views/TeamViewDialog.xaml` [NEW]

## 未決事項 (解決済案)

- **出力パス**: プロジェクトディレクトリ内の `team-tasks.md` とする。
- **API負荷**: Membership のキャッシュと `completed_since=now` による取得件数削減で対応。
- **UIデータソース**: 堅牢性とオフライン対応のため `team-tasks.md` パースを基本とする。

## ロードマップ (Phase)

1. **Phase 1**: Config 統合、Membership キャッシュ、`RunTeamSyncAsync` (ファイル出力まで)
2. **Phase 2**: Asana Sync ページの設定 UI 実装、`TeamTaskParser` 作成
3. **Phase 3**: Dashboard [Team] ボタン、`TeamViewDialog` ポップアップ実装
