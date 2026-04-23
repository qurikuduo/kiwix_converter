namespace KiwixConverter.Core.Models;

public enum ConversionTaskStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Faulted
}

public enum ArticleStatus
{
    Pending,
    InProgress,
    Completed,
    Skipped,
    Failed
}

public enum LogSeverity
{
    Trace,
    Info,
    Warning,
    Error
}

public enum WeKnoraAuthMode
{
    ApiKey,
    BearerToken
}

public sealed class AppSettings
{
    public string? KiwixDesktopDirectory { get; set; }

    public string? DefaultOutputDirectory { get; set; }

    public string? ZimdumpExecutablePath { get; set; }

    public string? TaskOutputOverrideDirectory { get; set; }

    public int SnapshotIntervalSeconds { get; set; } = 15;

    public string? WeKnoraBaseUrl { get; set; }

    public string? WeKnoraAccessToken { get; set; }

    public string? WeKnoraKnowledgeBaseId { get; set; }

    public string? WeKnoraKnowledgeBaseName { get; set; }

    public string? WeKnoraKnowledgeBaseDescription { get; set; }

    public string? WeKnoraChatModelId { get; set; }

    public string? WeKnoraEmbeddingModelId { get; set; }

    public string? WeKnoraMultimodalModelId { get; set; }

    public int WeKnoraChunkSize { get; set; } = 1000;

    public int WeKnoraChunkOverlap { get; set; } = 200;

    public bool WeKnoraEnableParentChild { get; set; }

    public WeKnoraAuthMode WeKnoraAuthMode { get; set; } = WeKnoraAuthMode.ApiKey;

    public bool WeKnoraAutoCreateKnowledgeBase { get; set; } = true;

    public bool WeKnoraAppendMetadataBlock { get; set; } = true;

    public string? HistorySearchText { get; set; }

    public string? LogSearchText { get; set; }

    public bool SelectedTaskLogsOnly { get; set; }

    public string? WeKnoraSyncSearchText { get; set; }

    public string? WeKnoraSyncLogSearchText { get; set; }

    public bool SelectedWeKnoraSyncLogsOnly { get; set; }

    public int MainWindowWidth { get; set; } = 1760;

    public int MainWindowHeight { get; set; } = 920;

    public int RootSplitterDistance { get; set; } = 680;

    public int WeKnoraSyncUpperSplitterDistance { get; set; } = 230;
}

public sealed class ToolAvailabilityResult
{
    public bool IsAvailable { get; set; }

    public string? ResolvedPath { get; set; }

    public string? Version { get; set; }

    public string? Message { get; set; }
}

public sealed class ZimLibraryItem
{
    public long Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime LastWriteUtc { get; set; }

    public string? Language { get; set; }

    public string? Publisher { get; set; }

    public string? ArchiveDate { get; set; }

    public DateTime? LastScannedUtc { get; set; }

    public bool IsAvailable { get; set; }

    public bool IsConverted { get; set; }

    public long? LastCompletedTaskId { get; set; }
}

public sealed class ConversionTaskRecord
{
    public long Id { get; set; }

    public long ZimLibraryItemId { get; set; }

    public string ZimPath { get; set; } = string.Empty;

    public string ArchiveKey { get; set; } = string.Empty;

    public string OutputDirectory { get; set; } = string.Empty;

    public ConversionTaskStatus Status { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public DateTime? LastHeartbeatUtc { get; set; }

    public bool RequestedPause { get; set; }

    public int ProcessedArticles { get; set; }

    public int TotalArticles { get; set; }

    public int SkippedArticles { get; set; }

    public string? CurrentArticleUrl { get; set; }

    public int? CurrentArticleIndex { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class ArticleCheckpointRecord
{
    public long Id { get; set; }

    public long TaskId { get; set; }

    public string ArticleUrl { get; set; } = string.Empty;

    public string? ArticleTitle { get; set; }

    public string? OutputRelativePath { get; set; }

    public ArticleStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public int ImageCount { get; set; }

    public int ChunkCount { get; set; }

    public string? ContentHash { get; set; }

    public string? LastError { get; set; }

    public DateTime? LastProcessedUtc { get; set; }
}

public sealed class LogEntryRecord
{
    public long Id { get; set; }

    public long? TaskId { get; set; }

    public DateTime TimestampUtc { get; set; }

    public LogSeverity Level { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string? ArticleUrl { get; set; }

    public string? Exception { get; set; }
}

public sealed class WeKnoraKnowledgeBaseInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Type { get; set; } = "document";

    public bool IsTemporary { get; set; }

    public string? StorageProvider { get; set; }
}

public sealed class WeKnoraModelInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Provider { get; set; }

    public bool IsDefault { get; set; }
}

public sealed class WeKnoraKnowledgeItemInfo
{
    public string Id { get; set; } = string.Empty;

    public string KnowledgeBaseId { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? ParseStatus { get; set; }

    public string? EnableStatus { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class WeKnoraSyncTaskRecord
{
    public long Id { get; set; }

    public long SourceTaskId { get; set; }

    public string SourceArchiveKey { get; set; } = string.Empty;

    public string SourceOutputDirectory { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string AuthMode { get; set; } = WeKnoraAuthMode.ApiKey.ToString();

    public string KnowledgeBaseId { get; set; } = string.Empty;

    public string? KnowledgeBaseName { get; set; }

    public ConversionTaskStatus Status { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public DateTime? LastHeartbeatUtc { get; set; }

    public bool RequestedPause { get; set; }

    public int ProcessedDocuments { get; set; }

    public int TotalDocuments { get; set; }

    public int FailedDocuments { get; set; }

    public string? CurrentArticleUrl { get; set; }

    public int? CurrentArticleIndex { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class WeKnoraSyncItemRecord
{
    public long Id { get; set; }

    public long SyncTaskId { get; set; }

    public string ArticleUrl { get; set; } = string.Empty;

    public string? ArticleTitle { get; set; }

    public string? OutputRelativePath { get; set; }

    public ArticleStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public string? ContentHash { get; set; }

    public string? RemoteKnowledgeId { get; set; }

    public string? RemoteParseStatus { get; set; }

    public string? LastError { get; set; }

    public DateTime? LastProcessedUtc { get; set; }
}

public sealed class WeKnoraSyncLogEntryRecord
{
    public long Id { get; set; }

    public long? SyncTaskId { get; set; }

    public DateTime TimestampUtc { get; set; }

    public LogSeverity Level { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string? ArticleUrl { get; set; }

    public string? Exception { get; set; }
}

public sealed class ZimArchiveMetadata
{
    public string? Title { get; set; }

    public string? Language { get; set; }

    public string? Publisher { get; set; }

    public string? ArchiveDate { get; set; }

    public int ArticleCount { get; set; }

    public Dictionary<string, string> RawMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ZimArticleDescriptor
{
    public string Url { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Namespace { get; set; } = "A";

    public int? Index { get; set; }
}

public sealed class PreparedArticleContent
{
    public string Title { get; set; } = string.Empty;

    public string Strategy { get; set; } = string.Empty;

    public string HtmlFragment { get; set; } = string.Empty;
}

public sealed class ExportedImage
{
    public string SourceUrl { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string? AltText { get; set; }
}

public sealed class RagChunk
{
    public string ChunkId { get; set; } = string.Empty;

    public int Index { get; set; }

    public string Text { get; set; } = string.Empty;

    public int CharacterCount { get; set; }

    public string? Heading { get; set; }
}

public sealed class ArticleExportMetadata
{
    public string Title { get; set; } = string.Empty;

    public string ArticleUrl { get; set; } = string.Empty;

    public string? Language { get; set; }

    public string? Publisher { get; set; }

    public string? ArchiveDate { get; set; }

    public string ContentPath { get; set; } = string.Empty;

    public string ChunksPath { get; set; } = string.Empty;

    public List<ExportedImage> Images { get; set; } = [];

    public List<RagChunk> Chunks { get; set; } = [];

    public string ContentHash { get; set; } = string.Empty;

    public DateTime ExportedAtUtc { get; set; }

    public Dictionary<string, string> ArchiveMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ArticleProcessingResult
{
    public string ArticleTitle { get; set; } = string.Empty;

    public string OutputRelativePath { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;

    public int ChunkCount { get; set; }

    public int ImageCount { get; set; }
}