using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ProjectCurator.Models;

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

// --- Query History ---

public class WikiQueryRecord
{
    public DateTime AskedAt { get; set; }
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public List<string> ReferencedPages { get; set; } = [];
    public string? SavedAsPage { get; set; }
}
