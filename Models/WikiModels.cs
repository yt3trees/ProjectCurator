using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Curia.Models;

// --- Wiki Page ---

public class WikiPageItem
{
    public string Title { get; set; } = "";
    public string RelativePath { get; set; } = "";   // e.g. "pages/entities/customer-master.md"
    public string Category { get; set; } = "";        // sources / entities / concepts / analysis / root
    public string Content { get; set; } = "";
    public DateTime LastModified { get; set; }
    public bool IsRoot { get; set; }                  // index.md / log.md
}

// --- Wiki Source ---

public class WikiSourceItem
{
    public string FileName { get; set; } = "";
    public string RelativePath { get; set; } = "";   // raw/ 配下の相対パス
    public string FullPath { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public long FileSizeBytes { get; set; }
}

// --- Wiki Lint ---

public enum WikiLintSeverity { High, Medium, Low }

public class WikiLintIssue
{
    public string Category { get; set; } = "";        // Contradiction / Stale / Orphan / Missing / BrokenLink / MissingSource
    public WikiLintSeverity Severity { get; set; }
    public string Description { get; set; } = "";
    public string? PagePath { get; set; }
    public string? RelatedPagePath { get; set; }
}

public class WikiLintResult
{
    public DateTime RunAt { get; set; }
    public List<WikiLintIssue> Issues { get; set; } = [];
    public bool IsEmpty => Issues.Count == 0;
}

// --- Wiki Meta (.wiki-meta.json) ---

public class WikiMetaStats
{
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("totalSources")]
    public int TotalSources { get; set; }

    [JsonPropertyName("lastIngest")]
    public DateTime? LastIngest { get; set; }

    [JsonPropertyName("lastLint")]
    public DateTime? LastLint { get; set; }

    [JsonPropertyName("lastQuery")]
    public DateTime? LastQuery { get; set; }
}

public class WikiMetaSettings
{
    [JsonPropertyName("autoUpdateIndex")]
    public bool AutoUpdateIndex { get; set; } = true;

    [JsonPropertyName("autoAppendLog")]
    public bool AutoAppendLog { get; set; } = true;

    [JsonPropertyName("maxPagesBeforeSearchRequired")]
    public int MaxPagesBeforeSearchRequired { get; set; } = 100;
}

public class WikiMeta
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "1.0";

    [JsonPropertyName("stats")]
    public WikiMetaStats Stats { get; set; } = new();

    [JsonPropertyName("settings")]
    public WikiMetaSettings Settings { get; set; } = new();
}

// --- Ingest Result ---

public class IngestPageResult
{
    public string Path { get; set; } = "";
    public string Content { get; set; } = "";
}

public class IngestUpdatedPageResult
{
    public string Path { get; set; } = "";
    public string Diff { get; set; } = "";
}

public class IngestResult
{
    public bool Success { get; set; }
    public string Summary { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public List<IngestPageResult> NewPages { get; set; } = [];
    public List<IngestUpdatedPageResult> UpdatedPages { get; set; } = [];
    public string IndexUpdate { get; set; } = "";
    public string LogEntry { get; set; } = "";
    public string DebugSystemPrompt { get; set; } = "";
    public string DebugUserPrompt { get; set; } = "";
    public string DebugResponse { get; set; } = "";
}

// --- LLM Response (Ingest) ---

public class IngestLlmResponse
{
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    [JsonPropertyName("newPages")]
    public List<IngestPageResult> NewPages { get; set; } = [];

    [JsonPropertyName("updatedPages")]
    public List<IngestUpdatedPageResult> UpdatedPages { get; set; } = [];

    [JsonPropertyName("indexUpdate")]
    public string IndexUpdate { get; set; } = "";

    [JsonPropertyName("logEntry")]
    public string LogEntry { get; set; } = "";
}

// --- Wiki Category Config (.wiki-categories.json) ---

public class WikiCategoriesConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = ["sources", "entities", "concepts", "analysis"];

    [JsonIgnore]
    public bool IsUnknownVersion { get; set; }

    [JsonIgnore]
    public bool HasNamingConflict { get; set; }

    [JsonIgnore]
    public string? ConflictDetail { get; set; }
}

// --- Wiki Prompt Config (.wiki-prompts.json) ---

public class WikiPromptOverrides
{
    [JsonPropertyName("systemPrefix")]
    public string SystemPrefix { get; set; } = "";

    [JsonPropertyName("systemSuffix")]
    public string SystemSuffix { get; set; } = "";

    [JsonPropertyName("userSuffix")]
    public string UserSuffix { get; set; } = "";
}

public class WikiPromptConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("import")]
    public WikiPromptOverrides Import { get; set; } = new();

    [JsonPropertyName("query")]
    public WikiPromptOverrides Query { get; set; } = new();

    [JsonPropertyName("lint")]
    public WikiPromptOverrides Lint { get; set; } = new();

    [JsonIgnore]
    public bool IsUnknownVersion { get; set; }
}

// --- Path Validation ---

public record WikiPathValidationResult(bool IsValid, string? ErrorReason);

// --- Transaction Models (.wiki-txn.json) ---

public enum WikiTxnPhase
{
    Prepared,
    Committing,
    Committed,
    Rollbacking,
    RolledBack
}

public enum WikiTxnEntryType { Create, Update }

public enum WikiTxnEntryState
{
    Prepared,
    TempWritten,
    Replaced,
    RolledBack
}

public class WikiTxnEntry
{
    [JsonPropertyName("entryType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WikiTxnEntryType EntryType { get; set; }

    [JsonPropertyName("targetPath")]
    public string TargetPath { get; set; } = "";

    [JsonPropertyName("tempPath")]
    public string TempPath { get; set; } = "";

    [JsonPropertyName("backupPath")]
    public string BackupPath { get; set; } = "";

    [JsonPropertyName("state")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WikiTxnEntryState State { get; set; } = WikiTxnEntryState.Prepared;
}

public class WikiTransaction
{
    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("phase")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WikiTxnPhase Phase { get; set; } = WikiTxnPhase.Prepared;

    [JsonPropertyName("createdAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("entries")]
    public List<WikiTxnEntry> Entries { get; set; } = [];
}

// --- Rename Transaction Models (.wiki-rename-txn.json) ---

public enum WikiRenameTxnPhase
{
    Prepared,
    MovingToTemp,
    MovingToNew,
    UpdatingConfig,
    Compensating,
    Committed,
    RolledBack
}

public class WikiRenameTxn
{
    [JsonPropertyName("oldCategory")]
    public string OldCategory { get; set; } = "";

    [JsonPropertyName("newCategory")]
    public string NewCategory { get; set; } = "";

    [JsonPropertyName("oldPath")]
    public string OldPath { get; set; } = "";

    [JsonPropertyName("newPath")]
    public string NewPath { get; set; } = "";

    [JsonPropertyName("tempPath")]
    public string TempPath { get; set; } = "";

    [JsonPropertyName("oldExistedAtStart")]
    public bool OldExistedAtStart { get; set; }

    [JsonPropertyName("phase")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WikiRenameTxnPhase Phase { get; set; } = WikiRenameTxnPhase.Prepared;
}

// --- Query History ---

public class WikiQueryRecord
{
    public DateTime AskedAt { get; set; }
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public List<string> ReferencedPages { get; set; } = [];
    public string? SavedAsPage { get; set; }

    /// <summary>過去セッションから読み込んだ場合のセッションファイルパス。JSONには保存しない。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SessionFilePath { get; set; }
}
