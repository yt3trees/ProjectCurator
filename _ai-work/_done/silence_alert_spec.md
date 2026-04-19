# Silence Alert (沈黙アラート) - 仕様書

並行管理しているプロジェクトの中から「忘れられている可能性が高い」案件を LLM に検知させ、Dashboard 上部にバナー表示する機能。

閾値ルールベースでは取りこぼす「古い × 期限近い × 最近言及あり」のような複合条件を、LLM に横断判断させることで抜け漏れを抑える。

## コンセプト

```
  既存サービス群 (ProjectDiscoveryService / TodayQueueService / DecisionLogService ...)
        │ 1プロジェクト1行の特徴量テーブルに集約
        ▼
  SilenceDetectorService
  (特徴量を LLM に投げ Top N を抽出)
        │
        ▼
  SilenceAlert キャッシュ (JSON, 24h TTL)
        │
        ▼
  DashboardViewModel.SilenceAlerts
        │
        ▼
  DashboardPage 上部バナー
  (最大3件まで。クリックでプロジェクトを開く / Dismiss / Snooze)
```

- 判定は 1 日 1 回 (起動時 + 毎朝 6:00 以降の最初の発火) だけ実行する。過剰呼び出しを避ける
- AiEnabled=false の場合は機能自体を無効化する (フォールバック無し)
- 結果は `%USERPROFILE%\.curia\silence_alerts.json` にキャッシュし、TTL 内は再呼び出ししない
- Dismiss / Snooze した案件はユーザー操作の記録として保持し、次回検出時の抑制に使う

## 用語

| 用語 | 意味 |
|---|---|
| SilenceAlert | 「忘れられているかも」と判定された 1 件。ProjectId, Severity, Reason を持つ |
| Feature Row | 1 プロジェクト分の特徴量 1 行。LLM にはテーブル形式で渡す |
| Severity | high / medium / low の 3 段階。バナー色と並び順に使う |
| Dismiss | ユーザーがアラートを消す操作。当該プロジェクトは次回判定から 7 日間除外 |
| Snooze | ユーザーが先送りする操作。次回判定から 1 日除外 |

---

## Phase 0: 前提条件・制約

- AiEnabled=true かつ LlmClientService が設定済みであること
- ProjectDiscoveryService のスキャン結果を利用するため、少なくとも 1 回プロジェクト一覧が取れていること
- 候補プロジェクト数が 0 件 / 50 件超の場合は呼び出しをスキップする (コスト / 誤検知対策)
- hidden_projects.json でユーザーが非表示にしたプロジェクトは検知対象外
- 起動直後のみ即時実行、その後は 1 時間おきに 6:00 以降判定を試す (StandupGeneratorService と同じ Timer パターン)

---

## Phase 1: データモデルと保存

### 新規モデル

`Models/SilenceAlertModels.cs` を新設。

```csharp
public enum SilenceSeverity { Low, Medium, High }

public class SilenceAlert
{
    public string ProjectId { get; set; } = "";        // ProjectInfo.Id と一致
    public string ProjectDisplayName { get; set; } = "";
    public SilenceSeverity Severity { get; set; }
    public string Reason { get; set; } = "";           // LLM が生成した 1 行
    public DateTime DetectedAt { get; set; }
}

public class SilenceAlertState
{
    public DateTime LastRunAt { get; set; }
    public List<SilenceAlert> Alerts { get; set; } = [];
    public Dictionary<string, DateTime> DismissedUntil { get; set; } = [];
    public Dictionary<string, DateTime> SnoozedUntil { get; set; } = [];
}
```

### 保存場所

- `%USERPROFILE%\.curia\silence_alerts.json` (ConfigService.GetConfigDir() を流用)
- 読み込み / 保存は SilenceDetectorService 内に集約

### TTL 判定

- `LastRunAt` から 20 時間以内 → キャッシュをそのまま使う
- それ以上経過 → 再判定を実行する (ユーザー手動リフレッシュも可能)

---

## Phase 2: 特徴量抽出

### SilenceDetectorService の責務

1 プロジェクト 1 行の Feature Row をメモリ上に組み立てる。新規 I/O は原則発生させず、既存サービスから取れる値だけを使う。

### Feature Row カラム

| カラム | 型 | 由来 | 備考 |
|---|---|---|---|
| ProjectId | string | ProjectInfo.Id | LLM 回答の突き合わせキー |
| DisplayName | string | ProjectInfo.DisplayName | LLM のプロンプト可読性向上用 |
| LastEditDays | int? | `_ai-context/` 配下の最新 mtime | ProjectDiscoveryService が既取得。null は未検出 |
| LastFocusUpdateDays | int? | current_focus.md の mtime | 〃 |
| OpenTaskCount | int | TodayQueueService | tasks.md 由来の未完タスク総数 |
| OverdueCount | int | TodayQueueService | DueBucket=="overdue" の件数 |
| NextDueDays | int? | TodayQueueService | 最も近い未来の DueDate までの日数 |
| UncommittedFiles | int | ProjectDiscoveryService | git 未コミット変更数 |
| LastDecisionDays | int? | DecisionLogService | 直近の decision_log 更新日 |
| RecentMentionCount | int | meeting_notes + standup grep | 直近 7 日の本文内にプロジェクト名 / 短縮名ヒット数 |
| IsPinned | bool | pinned_folders.json | ユーザーが固定表示しているか |
| Tier | string | ProjectInfo.Tier | full / mini。LLM の重みづけに使える |

NextDueDays と LastEditDays の組合せが一番効く信号。

### プロンプト入力サイズ

- 20 プロジェクト × 約 100 バイト / 行 = 2KB 程度。LLM コンテキストに収まる

---

## Phase 3: LLM 呼び出し

### プロンプト構造

LlmClientService.ChatCompletionAsync を 1 回だけ呼ぶ。

```
system:
あなたはプロジェクト忘却検知 AI です。以下のテーブルから「ユーザーが
忘れかけている可能性が高い」案件を最大 3 件選び、JSON で返してください。

判定の重み:
- 単に古いだけの案件は選ばない
- OverdueCount > 0 または NextDueDays <= 3 は最優先
- LastEditDays が大きいのに RecentMentionCount > 0 は要注意 (周りは動いてる)
- UncommittedFiles > 0 かつ LastEditDays > 7 は中断された作業の疑い
- IsPinned=true の案件は見逃しが致命的、閾値を緩めに

出力は次の JSON スキーマに厳密に従う:
[{ "project_id": "...", "severity": "high|medium|low", "reason": "..." }]
reason は 60 文字以内の日本語 1 行。複数信号を根拠として言及すること。

user:
# User Profile
{LlmUserProfile}

# Projects
<pipe区切りテーブル>
```

- reasoning_effort / temperature は LlmParameters に従う。temperature 未設定時は 0.2 を推奨
- max_output_tokens はアラート 3 件 × 100 字程度なので 512 で十分

### レスポンスパース

- `[ { ... }, ... ]` のみを期待。前後の ```json ブロックは除去してからパース
- パース失敗時はログに残してキャッシュ更新せず、前回結果を維持

---

## Phase 4: UI 統合

### Dashboard 上部バナー

DashboardPage の Today Queue の上に、新規の ItemsControl を差し込む。

- 各アラートは 1 行 Card: `⚠ [severity icon] プロジェクト名   理由                [開く] [Snooze] [×]`
- Severity によって左ボーダー色: high=赤, medium=オレンジ, low=グレー
- アラート 0 件のときは ItemsControl ごと Collapsed

### 操作

| ボタン | 挙動 |
|---|---|
| 開く | ProjectInfo を指定して既存のプロジェクトオープン動作を呼ぶ (DashboardViewModel の既存メソッド流用) |
| Snooze | SnoozedUntil[ProjectId] = 明日の 6:00 を保存。バナーから消す |
| × (Dismiss) | DismissedUntil[ProjectId] = 7 日後を保存。バナーから消す |

### リフレッシュ

- Dashboard の既存リフレッシュボタンから「Refresh silence alerts (Force)」メニューで手動再判定可能にする
- 通常はアプリ起動時と 6:00 以降の最初の Timer 発火で自動

---

## Phase 5: スケジューリング

### Timer

StandupGeneratorService と同じパターンを踏襲:

```csharp
public void StartScheduler()
{
    // 起動直後の重い初期化と被らないよう 30 秒遅延してから初回実行。
    // 以降は 1 時間おきに TryRunAsync を発火。
    _timer = new Timer(_ => _ = TryRunAsync(),
                       null, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));
}

public async Task TryRunAsync()
{
    if (DateTime.Now.Hour < 6) return;
    var state = Load();
    if ((DateTime.Now - state.LastRunAt).TotalHours < 20) return;
    await RunDetectionAsync();
}
```

### DI 登録

App.xaml.cs で Singleton 登録。MainWindow 起動時 `StartScheduler()` を呼ぶ。

---

## Phase 6: テスト観点

自動テストは無いため、手動検証項目を記録する。

- AiEnabled=false でバナーが出ないこと (呼び出し自体しない)
- キャッシュ TTL 内は LLM 呼び出しが発生しないこと (ログ確認)
- Dismiss → 7 日後まで同プロジェクトが出ないこと
- Snooze → 翌朝まで同プロジェクトが出ないこと
- プロジェクト 0 件 / hidden のみ / 50 件超で例外なくスキップされること
- LLM レスポンスが不正 JSON のときに例外で落ちずキャッシュが保持されること
- ネットワーク切断時に Dashboard が起動を妨げないこと

---

## Phase 7: 非対象 (今回やらない)

- 部下のプロジェクト検知 (チーム向け展開は別フェーズ)
- トレイ通知 / OS トースト (バナーのみに留める)
- Outlook / メールの未返信検知 (信号源追加は別タスク)
- ユーザー教示学習 (Dismiss 履歴から重みを自動調整する等)

---

## オープンクエスチョン

- Top N は固定 3 件でよいか、Severity high のみフィルタにするか
- 「周りは動いてる」を判定する RecentMentionCount の検索対象は meeting_notes + standup だけで十分か (Capture 履歴も含めるか)
- LlmUserProfile に「部長案件」のような個別重みを書く運用を前提とするか、UI で明示的に Priority Project を指定させるか
