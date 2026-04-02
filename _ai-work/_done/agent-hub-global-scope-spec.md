# Agent Hub - Global Scope 拡張仕様書

- 作成日: 2026-04-02
- ステータス: Draft

## 概要

Agent HubのDeployment対象を「Project限定」から拡張し、Projectに依存しないグローバルなsub agent / context rule / skillの適用先を管理できるようにする。

本拡張では、既存のMaster Library(Agents/Rules/Skills)はそのまま維持し、Deploymentターゲット解決だけを抽象化して `Project Scope` と `Global Scope` を切り替え可能にする。

## 背景

### 現状

- ライブラリ定義は既に共通管理 (`_config/agent_hub/`)
- 右ペインのデプロイ先は `SelectedProject + TargetSubPath` に固定
- `AgentDeploymentService` の主APIは `ProjectInfo` 前提
- 状態保存 (`agent_hub_state.json`) も実質 project scope 前提

### 課題

- 個人環境のホーム直下や共通CLIディレクトリへ配布したいケースに対応できない
- プロジェクトを選ばないルール配布をGUIで扱えない
- 「全プロジェクトへ配布」と「グローバルへ配布」が同じ概念として扱えず、運用上わかりにくい

---

## 設計方針

1. 既存互換性を最優先
- 既存のProject Scope挙動は変更しない
- 既存 `agent_hub_state.json` は読み込み可能な後方互換を維持

2. 変更はDeployment経路に限定
- Agents/Rules/SkillsのライブラリCRUDは現行のまま
- 追加対象は主に `ViewModel` のターゲット選択と `AgentDeploymentService` のパス解決

3. Scope切替を明示
- `Project Scope` と `Global Scope` をUI上で明確に分離
- Global ScopeではProject選択UIを無効化し、Global Profile選択UIを有効化

4. File-system truthを維持
- ON/OFF状態の最終判定は現行と同様にファイル存在ベース

---

## 用語定義

- Project Scope: 既存どおり、ProjectDiscoveryで見えるプロジェクト配下にデプロイするモード
- Global Scope: プロジェクトに紐づかない任意ベースパスへデプロイするモード
- Global Profile: Global Scopeで使うデプロイ先設定の名前付きセット

---

## UI仕様

### 右ペイン上部の変更

現行:
- Project
- Target SubPath

拡張後:
- Scope: `Project` / `Global`
- Scope=Project: 既存のProject + Target SubPathを表示
- Scope=Global: Global Profile選択を表示

```text
+--------------------------------------------------------------------+
| Scope: (o) Project   ( ) Global                                   |
|                                                                    |
| [Project Scope] Project: [ERP-Core v]   Target: [development\source v] |
| [Global  Scope] Profile: [Personal v]                            |
+--------------------------------------------------------------------+
```

### Deployment Matrix

- 中央〜下部のAgent/Rule/SkillトグルUIは現行維持
- Scope切替時にチェック状態を再解決して表示
- Status barに現在Scopeを表示
  - 例: `Scope=Global(Profile:Personal) / Sync complete`

### Global Profile 編集導線

初期は最小実装とし、Agent Hub内に簡易ボタンを追加:
- `Manage Profiles` ボタンでモーダル表示
- Name + CLI別パス + Rule対象ファイルパスを編集

---

## データモデル

### 新規Enum

```csharp
public enum DeploymentScopeType
{
    Project,
    Global
}
```

### 新規Model: GlobalDeploymentProfile

```csharp
public class GlobalDeploymentProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    // Agent/Skill 配置のベース
    public string ClaudeBasePath { get; set; } = "";
    public string CodexBasePath { get; set; } = "";
    public string CopilotBasePath { get; set; } = "";
    public string GeminiBasePath { get; set; } = "";

    // Rule 配置ファイル
    public string ClaudeRuleFilePath { get; set; } = "";
    public string CodexRuleFilePath { get; set; } = "";
    public string CopilotRuleFilePath { get; set; } = "";
    public string GeminiRuleFilePath { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
```

### 既存Deploymentモデル拡張

`AgentDeployment` / `RuleDeployment` / `SkillDeployment` に共通で以下を追加:

```csharp
public DeploymentScopeType ScopeType { get; set; } = DeploymentScopeType.Project;
public string ScopeId { get; set; } = ""; // Project名 or GlobalProfileId
```

後方互換:
- `ScopeType` 未指定データは `Project` とみなす
- `ScopeId` 未指定時は既存 `ProjectName` を代用

### Configファイル

新規:
- `_config/global_agent_hub_profiles.json`

例:

```json
{
  "profiles": [
    {
      "id": "personal",
      "name": "Personal",
      "claudeBasePath": "%USERPROFILE%\\.claude",
      "codexBasePath": "%USERPROFILE%\\.codex",
      "copilotBasePath": "%USERPROFILE%",
      "geminiBasePath": "%USERPROFILE%\\.gemini",
      "claudeRuleFilePath": "%USERPROFILE%\\CLAUDE.md",
      "codexRuleFilePath": "%USERPROFILE%\\AGENTS.md",
      "copilotRuleFilePath": "%USERPROFILE%\\.github\\copilot-instructions.md",
      "geminiRuleFilePath": "%USERPROFILE%\\GEMINI.md"
    }
  ]
}
```

---

## サービス設計

### ConfigService 拡張

追加メソッド:
- `LoadGlobalAgentHubProfiles()`
- `SaveGlobalAgentHubProfiles()`

要件:
- 環境変数展開対応
- ファイル未存在時はデフォルトProfileを返す

### AgentDeploymentService 拡張

方針:
- 既存Project APIは残しつつ、内部で共通ターゲット解決に寄せる

新規内部モデル:

```csharp
internal class DeploymentTarget
{
    public DeploymentScopeType ScopeType { get; init; }
    public string ScopeId { get; init; } = "";

    // Project scope
    public ProjectInfo? Project { get; init; }
    public string TargetSubPath { get; init; } = "";

    // Global scope
    public GlobalDeploymentProfile? Profile { get; init; }
}
```

実装ポイント:
- `ResolveAgentWriteDir` / `ResolveSkillWriteDir` / `ResolveRuleContextFilePaths` をDeploymentTarget対応
- Project Scope時は既存ロジックそのまま
- Global Scope時はProfileの絶対パスを使用

### SyncDeploymentState 拡張

- 既存stateの走査時に `ScopeType` で分岐
- Project: 既存検証
- Global: Profile解決後にファイル存在検証
- Profile削除済みの場合は該当stateを無効化

---

## ViewModel設計

`AgentHubViewModel` に追加:

- `SelectedScopeType`
- `GlobalProfiles`
- `SelectedGlobalProfile`
- `IsProjectScopeSelected`
- `IsGlobalScopeSelected`

変更点:
- Deployment snapshot構築時の入力を `SelectedProject + TargetSubPath` 固定から `BuildCurrentDeploymentTarget()` に変更
- Scope変更時に `QueueDeploymentRefresh(includeTargetCandidates: true)`
- Global Scope中は `TargetSubPathCandidates` を空(または固定)にする

---

## XAML/Code-behind変更方針

対象:
- `Views/Pages/AgentHubPage.xaml`
- `Views/Pages/AgentHubPage.xaml.cs`

変更内容:
- 右上にScopeトグルを追加
- Project選択行とGlobal Profile選択行をVisibility切替
- Profile管理ダイアログ(最小版)を追加

注意:
- 既存テーマリソース (`AppSurface*`, `AppText`) を利用
- ComboBoxは明示Heightを設定しない

---

## 移行仕様

1. 初回起動時
- `global_agent_hub_profiles.json` がなければ自動生成
- `personal` プロファイルを1件作成

2. 既存state読み込み
- `ScopeType` 未指定は `Project` 扱い
- 現行データは破壊しない

3. 旧UIの操作互換
- デフォルト選択は `Project Scope`
- 既存ユーザーは意識せず従来運用を継続可能

---

## リスクと対策

1. グローバルパス誤設定による意図しない上書き
- 対策: Profile保存時に存在チェックと確認ダイアログ

2. Copilotの`.github`構成差異
- 対策: Profileで明示的に `copilotRuleFilePath` を編集可能にする

3. State肥大化
- 対策: Sync時に無効レコードを定期クリーンアップ

4. 権限不足(ホーム外パス)
- 対策: 例外をStatus barに明示、トグル状態をロールバック

---

## 実装タスク

### Phase 1: Model/Config

- [ ] 1. `DeploymentScopeType` を `AgentHubModels.cs` に追加
- [ ] 2. `GlobalDeploymentProfile` modelを追加
- [ ] 3. Deployment系modelに `ScopeType` / `ScopeId` を追加
- [ ] 4. `ConfigService` にGlobal profile load/saveを追加

### Phase 2: Service

- [ ] 5. `AgentDeploymentService` にDeploymentTarget抽象を追加
- [ ] 6. Agent/Rule/Skillのpath解決をscope対応
- [ ] 7. `SyncDeploymentState` をscope対応

### Phase 3: ViewModel

- [ ] 8. `AgentHubViewModel` にscope/profileプロパティ追加
- [ ] 9. deployment snapshot生成をscope対応
- [ ] 10. scope切替時のrefreshとstatus更新

### Phase 4: UI

- [ ] 11. `AgentHubPage.xaml` にScope切替UI追加
- [ ] 12. Global Profile選択UI追加
- [ ] 13. Profile管理ダイアログ(最小)追加

### Phase 5: 検証

- [ ] 14. Project Scope互換確認 (回帰)
- [ ] 15. Global ScopeでAgent/Rule/SkillのDeploy/Undeploy確認
- [ ] 16. `dotnet publish -p:PublishProfile=SingleFile` でビルド確認

---

## 画面イメージ (ASCII)

```text
+----------------------------------------------------------------------------------+
| Agent Hub                                                                        |
+-----------------------------------------+----------------------------------------+
| Library (Agents / Rules / Skills)       | Deployment                            |
|                                         | Scope: [Project v]                    |
| - strict-reviewer                       | -------------------------------------  |
| - csharp-guard                          | [Project] Project: [ERP-Core v]       |
| - project-curator                       |           Target : [development\ v]    |
|                                         | [Global ] Profile: [Personal v]       |
| Preview                                 |                                        |
| --------------------------------------  | Agents / Rules / Skills matrix        |
| ...                                     | Cl | Cx | Cp | Gm                     |
+-----------------------------------------+----------------------------------------+
```

## 決定事項

- Master Libraryは引き続き共通管理を維持
- 追加対象はDeploymentターゲット解決とScope UI
- 後方互換を維持し、既存ユーザーの運用を壊さない
