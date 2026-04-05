# Local Task Mode - 実装計画

Asana を使わないユーザーでも、タスクの登録/完了/Today Queue/AI 機能をすべて利用できるようにする。

既存の `asana-tasks.md` を `tasks.md` にリネームし、共通タスクフォーマットとして Asana 有無で排他切り替えする。

## コンセプト

```
Asana トークン設定済み → Asana Sync が source of truth (従来通り)
Asana トークン未設定   → ローカルファイルが source of truth (本機能)
```

- 設定画面での明示的な切り替えは不要。Asana トークンの有無で自動判定
- Asana ユーザーの既存フローには一切影響しない
- 今後 Asana を導入したくなったらトークンを設定するだけで切り替わる

## Phase 0: ファイル名変更 `asana-tasks.md` → `tasks.md`

Asana に依存しない汎用的な名前に統一する。ファイルフォーマット (Markdown チェックリスト) は変更しない。

### ソースコード変更対象

| ファイル | 箇所数 | 変更内容 |
|---|---|---|
| Services/TodayQueueService.cs | 4 | パス文字列 + コメント |
| Services/AsanaTaskParser.cs | 2 | コメント + エラーメッセージ |
| Services/AsanaSyncService.cs | 3 | 出力先パス + コメント |
| Services/FocusUpdateService.cs | 5 | パス文字列 + コメント |
| Services/MeetingNotesService.cs | 5 | パス文字列 + コメント |
| Services/ContextCompressionLayerService.cs | 1 | CLAUDE.md テンプレート内の文字列 |
| Services/StateSnapshotService.cs | 2 | パス文字列 |
| Services/StandupGeneratorService.cs | 2 | パス文字列 + コメント |
| Views/Pages/DashboardPage.xaml.cs | 2 | パス文字列 |
| Views/Pages/EditorPage.xaml.cs | 1 | UI テキスト `"Add tasks to tasks.md"` |
| Views/Pages/SettingsPage.xaml | 1 | UI テキスト `"Append to tasks.md on task creation"` |
| Models/AppConfig.cs | 1 | コメント |
| Models/AsanaTaskModels.cs | 1 | XML doc コメント |

### ドキュメント変更対象

| ファイル | 変更内容 |
|---|---|
| AGENTS.md | サービス説明内の参照 |
| README.md / README-ja.md | Mermaid 図内の表示 |
| docs/asana-setup.md / asana-setup-ja.md | ファイルパス参照 |
| docs/ui-guide.md / ui-guide-ja.md | 機能説明内の参照 |
| docs/ai-features.md / ai-features-ja.md | 機能説明内の参照 |
| docs/daily-workflow.md / daily-workflow-ja.md | Mermaid 図内の表示 |
| Assets/ContextCompressionLayer/templates/CLAUDE_MD_SNIPPET.md | コンテキストファイルリスト |
| Assets/ContextCompressionLayer/skills/project-curator/reference/session-end.md | 参照 |
| Assets/ContextCompressionLayer/skills/project-curator/reference/update-focus.md | 参照 |

### 環境マイグレーションスクリプト

既存の `asana-tasks.md` を `tasks.md` にリネームする PowerShell スクリプトを `_ai-work/Tools/` に配置。

対象:
- 各プロジェクトの `_ai-context/obsidian_notes/asana-tasks.md`
- 各 workstream の `_ai-context/obsidian_notes/workstreams/<id>/asana-tasks.md`

スクリプトは `_ai-work/Tools/Rename-AsanaTasksToTasks.ps1` として別途作成する。

## 方針

- ローカルモードでは `tasks.md` をアプリが直接 CRUD する
- TodayQueueService / AsanaTaskParser のパースロジックは変更しない (ファイル名のみ)
- 完了操作は「Asana API 呼び出し」か「ファイル内チェックボックス更新」かで分岐
- Capture の task カテゴリは「Asana 起票」か「ファイル直接追記」かで分岐
- 今後 Asana を導入したくなったらトークンを設定するだけで切り替わる

## 変更箇所の概要

### 1. Asana モード判定ヘルパー (新規)

`ConfigService` に Asana 有効判定を集約する。

```csharp
// ConfigService に追加
public bool IsAsanaConfigured()
{
    var envToken = Environment.GetEnvironmentVariable("ASANA_TOKEN");
    if (!string.IsNullOrWhiteSpace(envToken)) return true;
    var global = LoadAsanaGlobalConfig();
    return !string.IsNullOrWhiteSpace(global.AsanaToken);
}
```

各サービスが個別に `GetAsanaToken()` / `ResolveAsanaToken()` で判定している現状を、UI 層がモード判定だけ必要な場合に1箇所で確認できるようにする。既存の各サービス内 Token 取得メソッドは変更しない。

### 2. TodayQueueTask の拡張 (小)

```csharp
// TodayQueueService.cs 内の TodayQueueTask クラス

// Before
public bool CanComplete => !string.IsNullOrWhiteSpace(AsanaTaskGid);

// After: ローカルタスク (GID なし) もファイルパスがあれば完了可能
public bool CanComplete => !string.IsNullOrWhiteSpace(AsanaTaskGid)
                        || !string.IsNullOrWhiteSpace(AsanaFilePath);

// ローカル専用かどうか (API 不要で完了可能)
public bool IsLocalOnly => string.IsNullOrWhiteSpace(AsanaTaskGid);
```

### 3. TodayQueueService にローカルタスク完了メソッド追加 (小)

Asana GID がないタ��クのチェックボックスをトグルする。タイトル文字列で行を特定。

```csharp
/// <summary>
/// ロ��カルタスク (GID なし) のチェックボックスを [ ] → [x] に更新する。
/// タイトルの完全一致で対象行を特定する。
/// </summary>
public void MarkLocalTaskCompletedInFile(TodayQueueTask task)
{
    if (string.IsNullOrWhiteSpace(task.AsanaFilePath) || !File.Exists(task.AsanaFilePath))
        return;

    var (content, encoding) = _fileEncodingService.ReadFile(task.AsanaFilePath);
    // タイトルをエスケープしてマッチ (先頭/末尾の空白を許容)
    var escapedTitle = Regex.Escape(task.Title.Trim());
    var pattern = @"(?m)^([ \t]*-\s+)\[ \](\s+" + escapedTitle + @")";
    var updated = Regex.Replace(content, pattern, "$1[x]$2",
        RegexOptions.None, TimeSpan.FromSeconds(5));
    if (updated != content)
        _fileEncodingService.WriteFile(task.AsanaFilePath, updated, encoding);
}
```

### 4. DashboardViewModel.CompleteTaskAsync の分岐 (小)

```csharp
public async Task CompleteTaskAsync(TodayQueueTask task)
{
    if (task.IsLocalOnly)
    {
        // ローカルタスク: ファイルのチェックボックスを直接トグル
        TodayQueueStatus = "Today Queue: Completing local task...";
        await Task.Run(() => _todayQueueService.MarkLocalTaskCompletedInFile(task));
    }
    else
    {
        // Asana タスク: API 完了 + ファイル更新 (既存ロジック)
        TodayQueueStatus = "Today Queue: Submitting to Asana...";
        var (ok, msg) = await _todayQueueService.CompleteAsanaTaskAsync(task.AsanaTaskGid!);
        if (!ok)
        {
            TodayQueueStatus = $"Today Queue: {msg}";
            return;
        }
        await Task.Run(() => _todayQueueService.MarkTaskCompletedInFile(task));
    }

    _cachedAllTasks.Remove(task);
    Application.Current.Dispatcher.Invoke(() => TodayQueueTasks.Remove(task));
    TodayQueueStatus = "Today Queue: Task completed.";
}
```

### 5. CaptureService にローカルタスク作成パス追加 (中)

CaptureWindow の task カテゴリで、Asana 未設定時にファイル直接追記する経路を追加。

```csharp
/// <summary>
/// Asana 未設定時のローカルタスク作成。tasks.md の In Progress セクションに追記。
/// </summary>
public async Task<CaptureRouteResult> CreateLocalTaskAsync(
    CaptureClassification classification,
    string originalInput,
    CancellationToken ct = default)
{
    var projects = await _discoveryService.GetProjectInfoListAsync(ct: ct);
    var project = projects.FirstOrDefault(p =>
        string.Equals(p.Name, classification.ProjectName, StringComparison.OrdinalIgnoreCase));

    if (project == null)
        return new CaptureRouteResult { Success = false, Message = "Project not found." };

    var taskLine = BuildTaskLine(classification);
    var tasksPath = ResolveTasksPath(project, classification);

    EnsureTaskFileExists(tasksPath);
    AppendToInProgressSection(tasksPath, taskLine);

    // capture_log.md にも副次記録
    await AppendToCaptureLogInternalAsync(
        $"[task][local] [{classification.ProjectName}] {classification.Summary}\n{originalInput}", ct);

    return new CaptureRouteResult
    {
        Success = true,
        Message = $"Local task added to {Path.GetFileName(tasksPath)}"
    };
}

private static string BuildTaskLine(CaptureClassification classification)
{
    var line = $"- [ ] {classification.Summary}";
    if (!string.IsNullOrWhiteSpace(classification.DueDate))
        line += $" (Due: {classification.DueDate})";
    return line;
}

private string ResolveTasksPath(ProjectInfo project, CaptureClassification classification)
{
    var obsidianNotes = Path.Combine(project.AiContextPath, "obsidian_notes");

    // workstream ヒントがあればそちらに配置
    if (!string.IsNullOrWhiteSpace(classification.WorkstreamHint))
    {
        var wsPath = Path.Combine(obsidianNotes, "workstreams",
            classification.WorkstreamHint, "tasks.md");
        if (File.Exists(wsPath) || Directory.Exists(Path.GetDirectoryName(wsPath)!))
            return wsPath;
    }

    return Path.Combine(obsidianNotes, "tasks.md");
}
```

### 6. タスクファイルの初期生成 (小)

プロジェクトに `tasks.md` が存在しない場合、最小テンプレートを生成する。

```csharp
private void EnsureTaskFileExists(string tasksPath)
{
    if (File.Exists(tasksPath)) return;

    Directory.CreateDirectory(Path.GetDirectoryName(tasksPath)!);
    var template = """
        ## In Progress

        ## Completed
        """;
    File.WriteAllText(tasksPath, template.Replace("        ", ""), Encoding.UTF8);
}
```

TodayQueueService はファイルが存在しない場合に空リストを返す既存動作なので、ファイルが未��成の状態でもクラッシュしない。

### 7. `AppendToInProgressSection` ヘルパー (小)

`## In Progress` セクションの末尾にタスク行を追記する。

```csharp
private void AppendToInProgressSection(string tasksPath, string taskLine)
{
    var (content, encoding) = _encoding.ReadFile(tasksPath);
    var lines = content.Split('\n').ToList();

    // "## In Progress" の直後に挿入位置を探す
    int insertIdx = -1;
    for (int i = 0; i < lines.Count; i++)
    {
        if (Regex.IsMatch(lines[i], @"^\s*#{2,3}\s*In\s+Progress\b"))
        {
            // セクション内の最後の空でない行の次に挿入
            insertIdx = i + 1;
            while (insertIdx < lines.Count
                && !Regex.IsMatch(lines[insertIdx], @"^\s*#{2,3}\s")
                && !string.IsNullOrWhiteSpace(lines[insertIdx]))
            {
                insertIdx++;
            }
            break;
        }
    }

    if (insertIdx < 0)
    {
        // セクションが見つからない場合はファイル末尾に追加
        lines.Add("");
        lines.Add("## In Progress");
        lines.Add(taskLine);
    }
    else
    {
        lines.Insert(insertIdx, taskLine);
    }

    _encoding.WriteFile(tasksPath, string.Join('\n', lines), encoding);
}
```

### 8. CaptureWindow の分岐 (中)

CaptureWindow の task 承認フローで Asana 未設定時の UI を分岐する。

```
Asana 設定済み (従来):
  分類確認 → TaskApproval 画面 (Asana project/section 選択) → Asana API 起票

Asana 未設定 (新規):
  分類確認 → LocalTaskApproval 画面 (タスク名/Due 確認のみ) → ファイル追記
```

#### LocalTaskApproval 画面

```
+----------------------------------------------------------+
|  Quick Capture                                      [x]   |
+----------------------------------------------------------+
|                                                           |
|  Category:  Task (Local)                                  |
|  Project:   ProjectAlpha                          [v]     |
|                                                           |
|  Task Name: [________________________]                    |
|  Due Date:  [YYYY-MM-DD    ] (optional)                   |
|                                                           |
|                      [Create] [Back] [Cancel]             |
|                                                           |
+----------------------------------------------------------+
```

- Asana project/section の選択 UI は表示しない
- タスク名と Due Date のみ確認/編集可能
- [Create] で `CreateLocalTaskAsync` を呼び出し

### 9. Dashboard の Add Task ボタン (中)

Today Queue エリアにタスク追加の導線を設ける。

#### 案 A: CaptureWindow を "task 固定モード" で起動

既存の CaptureWindow を再利用し、カテゴリを `task` に固定した状態で開く。AI 分類をスキップし、直接 TaskApproval (または LocalTaskApproval) 画面へ遷移。

```csharp
// DashboardPage.xaml.cs
private void OnAddTaskClick(object sender, RoutedEventArgs e)
{
    var captureWindow = new CaptureWindow(
        _captureService, _discoveryService, _configService,
        fixedCategory: "task");  // カテゴリ固定パラメータを追加
    captureWindow.Owner = Window.GetWindow(this);
    captureWindow.ShowDialog();
    // ダイアログ��じた後に Today Queue をリフレッシュ
}
```

#### 案 B: 簡易インライン入力

Today Queue ヘッダー横の [+] ボタンから Flyout で入力。軽量だが新規 UI 作成が必要。

推奨: 案 A。CaptureWindow の再利用で実装コストが低く、AI 分類 / Asana 起票との一貫性も保てる。

### 10. 既存コードへの影響まとめ

| ファイル | 変更内容 | 規模 |
|---|---|---|
| Services/ConfigService.cs | `IsAsanaConfigured()` 追加 | 小 |
| Services/TodayQueueService.cs | `CanComplete` 拡張, `IsLocalOnly` 追加, `MarkLocalTaskCompletedInFile` 追加 | 小 |
| ViewModels/DashboardViewModel.cs | `CompleteTaskAsync` 分岐追加 | 小 |
| Services/CaptureService.cs | `CreateLocalTaskAsync`, `EnsureTaskFileExists`, `AppendToInProgressSection` 追加 | 中 |
| Views/CaptureWindow.cs | Asana 未設定時の LocalTaskApproval 画面追加 | 中 |
| Views/Pages/DashboardPage.xaml.cs | Add Task ボタン + CaptureWindow 起動 | 小 |
| Views/Pages/DashboardPage.xaml | Add Task ボタン配置 | 小 |

### 変更しないもの

- `tasks.md` の Markdown フォーマット
- TodayQueueService のパースロジック (`ParseTasksFromAsanaFile`)
- AsanaTaskParser (FocusUpdate, Standup 等の AI 機能への入力)
- FocusUpdateService / StandupGeneratorService / StateSnapshotService
- AsanaSyncService (Asana モードのフロー、出力先ファイル名のみ変更)
- MeetingNotesService (Asana 起票パスは Asana モード時のみ動作)

## AI 機能との連携

ローカルタスクでも以下が無変更で動作する:

| 機能 | 理由 |
|---|---|
| Today Queue 表示 | TodayQueueService は Markdown パース。ソースを問わない |
| Focus Update | AsanaTaskParser は Markdown パース。ファイルがあれば動く |
| Standup 生成 | StandupGeneratorService は完了タスク行を抽出。チェック済み行があれば動く |
| State Snapshot | StateSnapshotService は tasks.md を読むだけ |
| Capture (task 以外) | tension, memo, focus_update, decision は Asana 非依存 |

唯一の制限: Meeting Notes Import のタスク起票ステップは Asana API を使うため、ローカルモードでは Asana ��票をスキップし、ファイル追記のみ行う分岐が必要 (将来対応可)。

## 実装順序

### Phase 0: ファイル名変更

1. ソースコード内の `"asana-tasks.md"` を `"tasks.md"` に一括置換 (上記テーブルの全箇所)
2. ドキュメント/Assets 内の参照を更新
3. UI テキストを更新 (`"Append to tasks.md on task creation"` 等)
4. ビルド確認 (`dotnet build`)
5. 環境マイグレーションスクリプト実行 (別途 `_ai-work/Tools/Rename-AsanaTasksToTasks.ps1`)

### Phase 1: 完了操作のローカル対応 (最小 MVP)

6. `TodayQueueTask` に `IsLocalOnly` プロパティ追加、`CanComplete` 拡張
7. `TodayQueueService.MarkLocalTaskCompletedInFile` 追加
8. `DashboardViewModel.CompleteTaskAsync` に分岐追加
9. `ConfigService.IsAsanaConfigured()` 追加

これだけで、手動で `tasks.md` を書いたユーザーが Today Queue から完了操作できるようになる。

### Phase 2: ローカルタスク作成

10. `CaptureService.CreateLocalTaskAsync` + ヘルパー群追加
11. `CaptureWindow` に LocalTaskApproval 画面追加
12. タスクファイル未存在時の初期生成

### Phase 3: Dashboard 導線

13. Dashboard に Add Task ボタン追加
14. CaptureWindow の `fixedCategory` モード対応

## テスト方針

自動テストはないため、以下を手動で確認する:

Phase 0:
- Asana Sync 実行後に `tasks.md` (旧 `asana-tasks.md`) が正しく生成されること
- Today Queue / FocusUpdate / Standup が `tasks.md` から正常に読み込むこと

Phase 1+:
- Asana トークン未設定の状態で:
  - 手動作成した `tasks.md` が Today Queue に表示されること
  - Today Queue からタスクを完了できること (チェックボックスが [x] になる)
  - Capture でタスクを作成するとファイルに追記されること
  - Dashboard の Add Task ボタンからタスクが追加できること
  - FocusUpdate / Standup がローカルタスクでも動作すること
- Asana トークン設定済みの状態で:
  - 既存フローが壊れていないこと (Asana API 経由の完了/起票)
