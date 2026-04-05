# Rename-AsanaTasksToTasks.ps1
# asana-tasks.md -> tasks.md 環境マイグレーションスクリプト
#
# 対象:
#   - 各プロジェクトの _ai-context/obsidian_notes/asana-tasks.md
#   - 各 workstream の _ai-context/obsidian_notes/workstreams/<id>/asana-tasks.md
#   - Obsidian vault 側の同名ファイル (ai-context junction 経由で同一の場合はスキップ)
#
# 使い方:
#   .\Rename-AsanaTasksToTasks.ps1                    # dry-run (既定)
#   .\Rename-AsanaTasksToTasks.ps1 -Execute           # 実行

param(
    [switch]$Execute
)

$ErrorActionPreference = "Stop"

# --- settings.json からパス取得 ---
$configDir = $env:PROJECTCURATOR_CONFIG_DIR
if (-not $configDir) {
    $newDefault = Join-Path $env:USERPROFILE ".projectcurator"
    $legacyDefault = Join-Path $env:USERPROFILE "Documents\Projects\_config"
    if (Test-Path $newDefault) {
        $configDir = $newDefault
    } elseif (Test-Path $legacyDefault) {
        $configDir = $legacyDefault
    } else {
        Write-Error "Config directory not found. Set PROJECTCURATOR_CONFIG_DIR or ensure ~/.projectcurator/ exists."
        return
    }
}

$settingsPath = Join-Path $configDir "settings.json"
if (-not (Test-Path $settingsPath)) {
    Write-Error "settings.json not found at $settingsPath"
    return
}

$settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
$localRoot = [Environment]::ExpandEnvironmentVariables($settings.LocalProjectsRoot)
$syncRoot = [Environment]::ExpandEnvironmentVariables($settings.CloudSyncRoot)
$obsidianRoot = if ($settings.ObsidianVaultRoot) {
    [Environment]::ExpandEnvironmentVariables($settings.ObsidianVaultRoot)
} else { "" }

Write-Host "=== asana-tasks.md -> tasks.md Migration ===" -ForegroundColor Cyan
Write-Host "Config dir:     $configDir"
Write-Host "Local root:     $localRoot"
Write-Host "Cloud sync root: $syncRoot"
Write-Host "Obsidian root:  $obsidianRoot"
if (-not $Execute) {
    Write-Host "[DRY RUN] Add -Execute to actually rename files." -ForegroundColor Yellow
}
Write-Host ""

# --- 対象ファイルを収集 ---
$targets = [System.Collections.Generic.List[string]]::new()

# 探索対象のルートディレクトリ
$searchRoots = @()
if ($localRoot -and (Test-Path $localRoot)) { $searchRoots += $localRoot }
if ($syncRoot -and (Test-Path $syncRoot)) { $searchRoots += $syncRoot }
if ($obsidianRoot -and (Test-Path $obsidianRoot)) { $searchRoots += $obsidianRoot }

foreach ($root in $searchRoots) {
    # obsidian_notes 配下の asana-tasks.md を再帰検索
    Get-ChildItem -Path $root -Recurse -Filter "asana-tasks.md" -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            $resolved = $_.FullName
            # シンボリックリンク/ジャンクション経由で同一ファイルを二重処理しないよう正規化
            try {
                $resolved = [System.IO.Path]::GetFullPath($_.FullName)
            } catch {}
            if (-not $targets.Contains($resolved)) {
                $targets.Add($resolved)
            }
        }
}

if ($targets.Count -eq 0) {
    Write-Host "No asana-tasks.md files found. Nothing to do." -ForegroundColor Green
    return
}

Write-Host "Found $($targets.Count) file(s) to rename:" -ForegroundColor Cyan
Write-Host ""

$renamed = 0
$skipped = 0
$errors = 0

foreach ($file in $targets) {
    $dir = Split-Path $file -Parent
    $newPath = Join-Path $dir "tasks.md"

    if (Test-Path $newPath) {
        Write-Host "  SKIP (tasks.md already exists): $file" -ForegroundColor Yellow
        $skipped++
        continue
    }

    if ($Execute) {
        try {
            Rename-Item -LiteralPath $file -NewName "tasks.md"
            Write-Host "  RENAMED: $file -> tasks.md" -ForegroundColor Green
            $renamed++
        } catch {
            Write-Host "  ERROR: $file - $_" -ForegroundColor Red
            $errors++
        }
    } else {
        Write-Host "  WOULD RENAME: $file -> tasks.md" -ForegroundColor Gray
        $renamed++
    }
}

Write-Host ""
Write-Host "--- Summary ---" -ForegroundColor Cyan
if ($Execute) {
    Write-Host "  Renamed: $renamed"
} else {
    Write-Host "  Would rename: $renamed"
}
Write-Host "  Skipped: $skipped"
Write-Host "  Errors:  $errors"

if (-not $Execute -and $renamed -gt 0) {
    Write-Host ""
    Write-Host "Run with -Execute to apply changes." -ForegroundColor Yellow
}
