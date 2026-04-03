# ConfigDir リファクタリング仕様書

- 作成日: 2026-04-03
- ステータス: Draft

## 概要

`ConfigService.WorkspaceRoot` が「設定ファイルの置き場所」と「プロジェクトスキャンのルート」を兼ねており、
どちらも `%USERPROFILE%\Documents\Projects` に固定されている問題を解消する。

設定ディレクトリを `%USERPROFILE%\.projectcurator\` に移し、
プロジェクトスキャンは `settings.LocalProjectsRoot` の値を正しく使うよう修正する。

## 背景

### 現状の問題

1. `ConfigDir` が `Documents\Projects\_config` に固定
   - `ConfigService.DetectWorkspaceRoot()` が `%USERPROFILE%\Documents\Projects\_config\settings.json`
     を決め打ちで探す
   - 見つからない場合も `%USERPROFILE%\Documents\Projects` をデフォルトにする
   - UIから変更する手段がない

2. `settings.LocalProjectsRoot` がプロジェクトスキャンに使われていない
   - `ProjectDiscoveryService.ScanProjects()` が `_configService.WorkspaceRoot` を直接使用
   - Settingsページで `LocalProjectsRoot` を変更してもスキャン先が変わらない

3. 初回起動の動作が不明確
   - `Documents\Projects` ディレクトリが存在しない環境でも `WorkspaceRoot` にセットされる
   - 設定保存するまで `_config` フォルダが作られない

### 設計方針

- `WorkspaceRoot` プロパティを廃止し、`ConfigDir` を独立したプロパティにする
- 新しい標準パス: `%USERPROFILE%\.projectcurator\`
- `.claude\`, `.codex\` と同じホームディレクトリに並ぶ統一感を重視
- 旧パス (`Documents\Projects\_config\`) は後方互換として継続サポート (移行作業不要)

## 設計

### ConfigDir の解決順序

```
1. 環境変数 PROJECTCURATOR_CONFIG_DIR  (任意のオーバーライド)
2. %USERPROFILE%\.projectcurator\      (新しい標準パス、settings.json が存在する場合)
3. %USERPROFILE%\Documents\Projects\_config\  (後方互換)
4. %USERPROFILE%\.projectcurator\      (初回起動時のデフォルト)
```

初回起動時は 1〜3 いずれも存在しないため 4 が選ばれる。
設定保存時に `EnsureConfigDir()` が `.projectcurator\` を作成する。

### プロジェクトスキャン

`ScanProjects()` および関連メソッドは `WorkspaceRoot` ではなく
`LoadSettings().LocalProjectsRoot` を参照する。
`LocalProjectsRoot` が未設定の場合はスキャンをスキップして空リストを返す。

### 初回起動時の動作

1. `.projectcurator\` が作成される (設定保存時)
2. `LocalProjectsRoot` が未設定のためプロジェクト一覧は空
3. Settingsページで `LocalProjectsRoot` を設定 → スキャン開始

Settingsページに未設定時の警告表示を追加することを推奨する (本仕様のスコープ外)。

## 変更箇所

### ConfigService.cs

`WorkspaceRoot` プロパティを廃止し、`ConfigDir` を直接解決するプロパティに変更する。

```csharp
// Before
public string WorkspaceRoot { get; }
public string ConfigDir => Path.Combine(WorkspaceRoot, "_config");

public ConfigService()
{
    WorkspaceRoot = DetectWorkspaceRoot();
}

private static string DetectWorkspaceRoot()
{
    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var candidate = Path.Combine(userProfile, "Documents", "Projects", "_config", "settings.json");
    if (File.Exists(candidate))
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(candidate)!, ".."));
    return Path.Combine(userProfile, "Documents", "Projects");
}
```

```csharp
// After
public string ConfigDir { get; }

public ConfigService()
{
    ConfigDir = DetectConfigDir();
}

private static string DetectConfigDir()
{
    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // 1. 環境変数オーバーライド
    var envOverride = Environment.GetEnvironmentVariable("PROJECTCURATOR_CONFIG_DIR");
    if (!string.IsNullOrWhiteSpace(envOverride) && Directory.Exists(envOverride))
        return envOverride;

    // 2. 新標準パス
    var newPath = Path.Combine(userProfile, ".projectcurator");
    if (File.Exists(Path.Combine(newPath, "settings.json")))
        return newPath;

    // 3. 後方互換 (旧パス)
    var legacyPath = Path.Combine(userProfile, "Documents", "Projects", "_config");
    if (File.Exists(Path.Combine(legacyPath, "settings.json")))
        return legacyPath;

    // 4. 初回起動デフォルト
    return newPath;
}
```

テスト用コンストラクタ `ConfigService(string configDir)` は引数の意味を `workspaceRoot` から
`configDir` に変更する。

### ProjectDiscoveryService.cs - ScanProjects()

```csharp
// Before
var root = _configService.WorkspaceRoot;

// After
var root = _configService.LoadSettings().LocalProjectsRoot;
if (string.IsNullOrWhiteSpace(root)) return projects;
```

### TodayQueueService.cs - SnoozeFilePath

```csharp
// Before
Path.Combine(_configService.WorkspaceRoot, "_config", "today_queue_snooze.json");

// After
Path.Combine(_configService.ConfigDir, "today_queue_snooze.json");
```

## 後方互換

- 既存ユーザーが `Documents\Projects\_config\settings.json` を持つ場合、解決順序の 3 で検出されるため
  移行作業は不要。
- 新規インストールユーザーは `%USERPROFILE%\.projectcurator\` が自動作成される。

## スコープ外

- `ProjectDiscoveryService` 内の `_INHOUSE` ハードコード問題 (別途対応)
- Settingsページの `LocalProjectsRoot` 未設定警告 UI
