using System.Globalization;
using System.Text.Json;
using KiwixConverter.Core.Models;
using Microsoft.Data.Sqlite;

namespace KiwixConverter.Core.Infrastructure;

public sealed class SqliteRepository
{
    private const string Iso8601Format = "O";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectory(AppPaths.ApplicationDataDirectory);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;

CREATE TABLE IF NOT EXISTS settings (
    key TEXT PRIMARY KEY,
    value TEXT NULL
);

CREATE TABLE IF NOT EXISTS zim_library (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    display_name TEXT NOT NULL,
    full_path TEXT NOT NULL UNIQUE,
    size_bytes INTEGER NOT NULL,
    last_write_utc TEXT NOT NULL,
    language TEXT NULL,
    publisher TEXT NULL,
    archive_date TEXT NULL,
    last_scanned_utc TEXT NULL,
    is_available INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS conversion_tasks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    zim_library_item_id INTEGER NOT NULL,
    zim_path TEXT NOT NULL,
    archive_key TEXT NOT NULL,
    output_directory TEXT NOT NULL,
    status TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    started_utc TEXT NULL,
    completed_utc TEXT NULL,
    last_heartbeat_utc TEXT NULL,
    requested_pause INTEGER NOT NULL DEFAULT 0,
    processed_articles INTEGER NOT NULL DEFAULT 0,
    total_articles INTEGER NOT NULL DEFAULT 0,
    skipped_articles INTEGER NOT NULL DEFAULT 0,
    current_article_url TEXT NULL,
    current_article_index INTEGER NULL,
    error_message TEXT NULL,
    FOREIGN KEY (zim_library_item_id) REFERENCES zim_library(id)
);

CREATE TABLE IF NOT EXISTS archive_metadata (
    task_id INTEGER PRIMARY KEY,
    title TEXT NULL,
    language TEXT NULL,
    publisher TEXT NULL,
    archive_date TEXT NULL,
    article_count INTEGER NOT NULL DEFAULT 0,
    raw_json TEXT NOT NULL,
    FOREIGN KEY (task_id) REFERENCES conversion_tasks(id)
);

CREATE TABLE IF NOT EXISTS article_checkpoints (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id INTEGER NOT NULL,
    article_url TEXT NOT NULL,
    article_title TEXT NULL,
    output_relative_path TEXT NULL,
    status TEXT NOT NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    image_count INTEGER NOT NULL DEFAULT 0,
    chunk_count INTEGER NOT NULL DEFAULT 0,
    content_hash TEXT NULL,
    last_error TEXT NULL,
    last_processed_utc TEXT NULL,
    UNIQUE (task_id, article_url),
    FOREIGN KEY (task_id) REFERENCES conversion_tasks(id)
);

CREATE TABLE IF NOT EXISTS log_entries (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id INTEGER NULL,
    timestamp_utc TEXT NOT NULL,
    level TEXT NOT NULL,
    category TEXT NOT NULL,
    message TEXT NOT NULL,
    details TEXT NULL,
    article_url TEXT NULL,
    exception TEXT NULL,
    FOREIGN KEY (task_id) REFERENCES conversion_tasks(id)
);

CREATE TABLE IF NOT EXISTS weknora_sync_tasks (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_task_id INTEGER NOT NULL,
    source_archive_key TEXT NOT NULL,
    source_output_directory TEXT NOT NULL,
    base_url TEXT NOT NULL,
    auth_mode TEXT NOT NULL,
    knowledge_base_id TEXT NOT NULL,
    knowledge_base_name TEXT NULL,
    status TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    started_utc TEXT NULL,
    completed_utc TEXT NULL,
    last_heartbeat_utc TEXT NULL,
    requested_pause INTEGER NOT NULL DEFAULT 0,
    processed_documents INTEGER NOT NULL DEFAULT 0,
    total_documents INTEGER NOT NULL DEFAULT 0,
    failed_documents INTEGER NOT NULL DEFAULT 0,
    current_article_url TEXT NULL,
    current_article_index INTEGER NULL,
    error_message TEXT NULL,
    FOREIGN KEY (source_task_id) REFERENCES conversion_tasks(id)
);

CREATE TABLE IF NOT EXISTS weknora_sync_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sync_task_id INTEGER NOT NULL,
    article_url TEXT NOT NULL,
    article_title TEXT NULL,
    output_relative_path TEXT NULL,
    status TEXT NOT NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    content_hash TEXT NULL,
    remote_knowledge_id TEXT NULL,
    remote_parse_status TEXT NULL,
    last_error TEXT NULL,
    last_processed_utc TEXT NULL,
    UNIQUE (sync_task_id, article_url),
    FOREIGN KEY (sync_task_id) REFERENCES weknora_sync_tasks(id)
);

CREATE TABLE IF NOT EXISTS weknora_sync_log_entries (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sync_task_id INTEGER NULL,
    timestamp_utc TEXT NOT NULL,
    level TEXT NOT NULL,
    category TEXT NOT NULL,
    message TEXT NOT NULL,
    details TEXT NULL,
    article_url TEXT NULL,
    exception TEXT NULL,
    FOREIGN KEY (sync_task_id) REFERENCES weknora_sync_tasks(id)
);
";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = new AppSettings();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM settings;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = reader.GetString(0);
            var value = reader.IsDBNull(1) ? null : reader.GetString(1);

            switch (key)
            {
                case "kiwixDesktopDirectory":
                    settings.KiwixDesktopDirectory = value;
                    break;
                case "defaultOutputDirectory":
                    settings.DefaultOutputDirectory = value;
                    break;
                case "zimdumpExecutablePath":
                    settings.ZimdumpExecutablePath = value;
                    break;
                case "snapshotIntervalSeconds":
                    if (int.TryParse(value, out var snapshotSeconds))
                    {
                        settings.SnapshotIntervalSeconds = Math.Max(5, snapshotSeconds);
                    }

                    break;
                case "weKnoraBaseUrl":
                    settings.WeKnoraBaseUrl = value;
                    break;
                case "weKnoraAccessToken":
                    settings.WeKnoraAccessToken = value;
                    break;
                case "weKnoraKnowledgeBaseId":
                    settings.WeKnoraKnowledgeBaseId = value;
                    break;
                case "weKnoraKnowledgeBaseName":
                    settings.WeKnoraKnowledgeBaseName = value;
                    break;
                case "weKnoraChatModelId":
                    settings.WeKnoraChatModelId = value;
                    break;
                case "weKnoraEmbeddingModelId":
                    settings.WeKnoraEmbeddingModelId = value;
                    break;
                case "weKnoraMultimodalModelId":
                    settings.WeKnoraMultimodalModelId = value;
                    break;
                case "weKnoraAuthMode":
                    if (Enum.TryParse<WeKnoraAuthMode>(value, out var authMode))
                    {
                        settings.WeKnoraAuthMode = authMode;
                    }

                    break;
                case "weKnoraAutoCreateKnowledgeBase":
                    if (bool.TryParse(value, out var autoCreateKnowledgeBase))
                    {
                        settings.WeKnoraAutoCreateKnowledgeBase = autoCreateKnowledgeBase;
                    }
                    else if (int.TryParse(value, out var autoCreateKnowledgeBaseInt))
                    {
                        settings.WeKnoraAutoCreateKnowledgeBase = autoCreateKnowledgeBaseInt != 0;
                    }

                    break;
                case "weKnoraAppendMetadataBlock":
                    if (bool.TryParse(value, out var appendMetadataBlock))
                    {
                        settings.WeKnoraAppendMetadataBlock = appendMetadataBlock;
                    }
                    else if (int.TryParse(value, out var appendMetadataBlockInt))
                    {
                        settings.WeKnoraAppendMetadataBlock = appendMetadataBlockInt != 0;
                    }

                    break;
            }
        }

        return settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await UpsertSettingAsync(connection, transaction, "kiwixDesktopDirectory", settings.KiwixDesktopDirectory, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "defaultOutputDirectory", settings.DefaultOutputDirectory, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "zimdumpExecutablePath", settings.ZimdumpExecutablePath, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "snapshotIntervalSeconds", settings.SnapshotIntervalSeconds.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await UpsertSettingAsync(connection, transaction, "weKnoraBaseUrl", settings.WeKnoraBaseUrl, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "weKnoraAccessToken", settings.WeKnoraAccessToken, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "weKnoraKnowledgeBaseId", settings.WeKnoraKnowledgeBaseId, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "weKnoraKnowledgeBaseName", settings.WeKnoraKnowledgeBaseName, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "weKnoraChatModelId", settings.WeKnoraChatModelId, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "weKnoraEmbeddingModelId", settings.WeKnoraEmbeddingModelId, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "weKnoraMultimodalModelId", settings.WeKnoraMultimodalModelId, cancellationToken);
        await UpsertSettingAsync(connection, transaction, "weKnoraAuthMode", settings.WeKnoraAuthMode.ToString(), cancellationToken);
        await UpsertSettingAsync(connection, transaction, "weKnoraAutoCreateKnowledgeBase", settings.WeKnoraAutoCreateKnowledgeBase.ToString(), cancellationToken);
        await UpsertSettingAsync(connection, transaction, "weKnoraAppendMetadataBlock", settings.WeKnoraAppendMetadataBlock.ToString(), cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SyncZimLibraryAsync(IEnumerable<FileInfo> files, CancellationToken cancellationToken = default)
    {
        var fileList = files.ToList();
        var scannedAt = DateTime.UtcNow;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await using (var markUnavailable = connection.CreateCommand())
        {
            markUnavailable.Transaction = transaction;
            markUnavailable.CommandText = "UPDATE zim_library SET is_available = 0;";
            await markUnavailable.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var file in fileList)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO zim_library(display_name, full_path, size_bytes, last_write_utc, last_scanned_utc, is_available)
VALUES($displayName, $fullPath, $sizeBytes, $lastWriteUtc, $lastScannedUtc, 1)
ON CONFLICT(full_path) DO UPDATE SET
    display_name = excluded.display_name,
    size_bytes = excluded.size_bytes,
    last_write_utc = excluded.last_write_utc,
    last_scanned_utc = excluded.last_scanned_utc,
    is_available = 1;";

            command.Parameters.AddWithValue("$displayName", file.Name);
            command.Parameters.AddWithValue("$fullPath", file.FullName);
            command.Parameters.AddWithValue("$sizeBytes", file.Length);
            command.Parameters.AddWithValue("$lastWriteUtc", file.LastWriteTimeUtc.ToString(Iso8601Format, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$lastScannedUtc", scannedAt.ToString(Iso8601Format, CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ZimLibraryItem>> GetZimLibraryItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<ZimLibraryItem>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT z.id,
       z.display_name,
       z.full_path,
       z.size_bytes,
       z.last_write_utc,
       z.language,
       z.publisher,
       z.archive_date,
       z.last_scanned_utc,
       z.is_available,
       EXISTS(SELECT 1 FROM conversion_tasks t WHERE t.zim_library_item_id = z.id AND t.status = 'Completed') AS is_converted,
       (SELECT t.id FROM conversion_tasks t WHERE t.zim_library_item_id = z.id AND t.status = 'Completed' ORDER BY t.completed_utc DESC LIMIT 1) AS last_completed_task_id
FROM zim_library z
WHERE z.is_available = 1
ORDER BY z.display_name COLLATE NOCASE;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadZimLibraryItem(reader));
        }

        return items;
    }

    public async Task<ZimLibraryItem?> GetZimLibraryItemAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT z.id,
       z.display_name,
       z.full_path,
       z.size_bytes,
       z.last_write_utc,
       z.language,
       z.publisher,
       z.archive_date,
       z.last_scanned_utc,
       z.is_available,
       EXISTS(SELECT 1 FROM conversion_tasks t WHERE t.zim_library_item_id = z.id AND t.status = 'Completed') AS is_converted,
       (SELECT t.id FROM conversion_tasks t WHERE t.zim_library_item_id = z.id AND t.status = 'Completed' ORDER BY t.completed_utc DESC LIMIT 1) AS last_completed_task_id
FROM zim_library z
WHERE z.id = $id
LIMIT 1;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadZimLibraryItem(reader);
        }

        return null;
    }

    public async Task UpdateZimLibraryMetadataAsync(long id, ZimArchiveMetadata metadata, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE zim_library
SET language = $language,
    publisher = $publisher,
    archive_date = $archiveDate
WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$language", (object?)metadata.Language ?? DBNull.Value);
        command.Parameters.AddWithValue("$publisher", (object?)metadata.Publisher ?? DBNull.Value);
        command.Parameters.AddWithValue("$archiveDate", (object?)metadata.ArchiveDate ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> CreateTaskAsync(
        long zimLibraryItemId,
        string zimPath,
        string archiveKey,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var createdUtc = DateTime.UtcNow;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO conversion_tasks(
    zim_library_item_id,
    zim_path,
    archive_key,
    output_directory,
    status,
    created_utc,
    last_heartbeat_utc,
    requested_pause)
VALUES(
    $zimLibraryItemId,
    $zimPath,
    $archiveKey,
    $outputDirectory,
    $status,
    $createdUtc,
    $lastHeartbeatUtc,
    0);
SELECT last_insert_rowid();";

        command.Parameters.AddWithValue("$zimLibraryItemId", zimLibraryItemId);
        command.Parameters.AddWithValue("$zimPath", zimPath);
        command.Parameters.AddWithValue("$archiveKey", archiveKey);
        command.Parameters.AddWithValue("$outputDirectory", outputDirectory);
        command.Parameters.AddWithValue("$status", ConversionTaskStatus.Pending.ToString());
        command.Parameters.AddWithValue("$createdUtc", createdUtc.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$lastHeartbeatUtc", createdUtc.ToString(Iso8601Format, CultureInfo.InvariantCulture));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public async Task<ConversionTaskRecord?> GetTaskAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id,
       zim_library_item_id,
       zim_path,
       archive_key,
       output_directory,
       status,
       created_utc,
       started_utc,
       completed_utc,
       last_heartbeat_utc,
       requested_pause,
       processed_articles,
       total_articles,
       skipped_articles,
       current_article_url,
       current_article_index,
       error_message
FROM conversion_tasks
WHERE id = $taskId
LIMIT 1;";
        command.Parameters.AddWithValue("$taskId", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadTask(reader);
        }

        return null;
    }

    public async Task<ConversionTaskRecord?> GetLatestTaskForZimAsync(long zimLibraryItemId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id,
       zim_library_item_id,
       zim_path,
       archive_key,
       output_directory,
       status,
       created_utc,
       started_utc,
       completed_utc,
       last_heartbeat_utc,
       requested_pause,
       processed_articles,
       total_articles,
       skipped_articles,
       current_article_url,
       current_article_index,
       error_message
FROM conversion_tasks
WHERE zim_library_item_id = $zimLibraryItemId
ORDER BY created_utc DESC
LIMIT 1;";
        command.Parameters.AddWithValue("$zimLibraryItemId", zimLibraryItemId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadTask(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<ConversionTaskRecord>> GetTasksAsync(string? searchText = null, CancellationToken cancellationToken = default)
    {
        var tasks = new List<ConversionTaskRecord>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id,
       zim_library_item_id,
       zim_path,
       archive_key,
       output_directory,
       status,
       created_utc,
       started_utc,
       completed_utc,
       last_heartbeat_utc,
       requested_pause,
       processed_articles,
       total_articles,
       skipped_articles,
       current_article_url,
       current_article_index,
       error_message
FROM conversion_tasks
WHERE ($searchText IS NULL
    OR zim_path LIKE '%' || $searchText || '%'
    OR output_directory LIKE '%' || $searchText || '%'
    OR status LIKE '%' || $searchText || '%'
    OR IFNULL(error_message, '') LIKE '%' || $searchText || '%')
ORDER BY created_utc DESC;";
        command.Parameters.AddWithValue("$searchText", (object?)searchText ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(ReadTask(reader));
        }

        return tasks;
    }

    public async Task SetTaskPauseRequestedAsync(long taskId, bool requested, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE conversion_tasks
SET requested_pause = $requestedPause,
    last_heartbeat_utc = $lastHeartbeatUtc
WHERE id = $taskId;";
        command.Parameters.AddWithValue("$taskId", taskId);
        command.Parameters.AddWithValue("$requestedPause", requested ? 1 : 0);
        command.Parameters.AddWithValue("$lastHeartbeatUtc", DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateTaskStatusAsync(
        long taskId,
        ConversionTaskStatus status,
        string? errorMessage = null,
        DateTime? startedUtc = null,
        DateTime? completedUtc = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE conversion_tasks
SET status = $status,
    started_utc = COALESCE($startedUtc, started_utc),
    completed_utc = $completedUtc,
    last_heartbeat_utc = $lastHeartbeatUtc,
    error_message = $errorMessage,
    requested_pause = CASE WHEN $status = 'Running' THEN 0 ELSE requested_pause END
WHERE id = $taskId;";
        command.Parameters.AddWithValue("$taskId", taskId);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$startedUtc", startedUtc.HasValue ? startedUtc.Value.ToString(Iso8601Format, CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", completedUtc.HasValue ? completedUtc.Value.ToString(Iso8601Format, CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$lastHeartbeatUtc", DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateTaskProgressAsync(
        long taskId,
        int processedArticles,
        int totalArticles,
        int skippedArticles,
        string? currentArticleUrl,
        int? currentArticleIndex,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE conversion_tasks
SET processed_articles = $processedArticles,
    total_articles = $totalArticles,
    skipped_articles = $skippedArticles,
    current_article_url = $currentArticleUrl,
    current_article_index = $currentArticleIndex,
    last_heartbeat_utc = $lastHeartbeatUtc
WHERE id = $taskId;";
        command.Parameters.AddWithValue("$taskId", taskId);
        command.Parameters.AddWithValue("$processedArticles", processedArticles);
        command.Parameters.AddWithValue("$totalArticles", totalArticles);
        command.Parameters.AddWithValue("$skippedArticles", skippedArticles);
        command.Parameters.AddWithValue("$currentArticleUrl", (object?)currentArticleUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$currentArticleIndex", currentArticleIndex.HasValue ? currentArticleIndex.Value : DBNull.Value);
        command.Parameters.AddWithValue("$lastHeartbeatUtc", DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchTaskHeartbeatAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE conversion_tasks
SET last_heartbeat_utc = $lastHeartbeatUtc
WHERE id = $taskId;";
        command.Parameters.AddWithValue("$taskId", taskId);
        command.Parameters.AddWithValue("$lastHeartbeatUtc", DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkInterruptedTasksAsPausedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE conversion_tasks
SET status = 'Paused',
    requested_pause = 1,
    last_heartbeat_utc = $lastHeartbeatUtc,
    error_message = CASE
        WHEN error_message IS NULL OR error_message = '' THEN 'Recovered from previous session interruption.'
        ELSE error_message
    END
WHERE status = 'Running';";
        command.Parameters.AddWithValue("$lastHeartbeatUtc", DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> CreateWeKnoraSyncTaskAsync(
        long sourceTaskId,
        string sourceArchiveKey,
        string sourceOutputDirectory,
        string baseUrl,
        WeKnoraAuthMode authMode,
        string knowledgeBaseId,
        string? knowledgeBaseName,
        CancellationToken cancellationToken = default)
    {
        var createdUtc = DateTime.UtcNow;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO weknora_sync_tasks(
    source_task_id,
    source_archive_key,
    source_output_directory,
    base_url,
    auth_mode,
    knowledge_base_id,
    knowledge_base_name,
    status,
    created_utc,
    last_heartbeat_utc,
    requested_pause)
VALUES(
    $sourceTaskId,
    $sourceArchiveKey,
    $sourceOutputDirectory,
    $baseUrl,
    $authMode,
    $knowledgeBaseId,
    $knowledgeBaseName,
    $status,
    $createdUtc,
    $lastHeartbeatUtc,
    0);
SELECT last_insert_rowid();";

        command.Parameters.AddWithValue("$sourceTaskId", sourceTaskId);
        command.Parameters.AddWithValue("$sourceArchiveKey", sourceArchiveKey);
        command.Parameters.AddWithValue("$sourceOutputDirectory", sourceOutputDirectory);
        command.Parameters.AddWithValue("$baseUrl", baseUrl);
        command.Parameters.AddWithValue("$authMode", authMode.ToString());
        command.Parameters.AddWithValue("$knowledgeBaseId", knowledgeBaseId);
        command.Parameters.AddWithValue("$knowledgeBaseName", (object?)knowledgeBaseName ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", ConversionTaskStatus.Pending.ToString());
        command.Parameters.AddWithValue("$createdUtc", createdUtc.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$lastHeartbeatUtc", createdUtc.ToString(Iso8601Format, CultureInfo.InvariantCulture));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public async Task<WeKnoraSyncTaskRecord?> GetWeKnoraSyncTaskAsync(long syncTaskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id,
       source_task_id,
       source_archive_key,
       source_output_directory,
       base_url,
       auth_mode,
       knowledge_base_id,
       knowledge_base_name,
       status,
       created_utc,
       started_utc,
       completed_utc,
       last_heartbeat_utc,
       requested_pause,
       processed_documents,
       total_documents,
       failed_documents,
       current_article_url,
       current_article_index,
       error_message
FROM weknora_sync_tasks
WHERE id = $syncTaskId
LIMIT 1;";
        command.Parameters.AddWithValue("$syncTaskId", syncTaskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadWeKnoraSyncTask(reader);
        }

        return null;
    }

    public async Task<WeKnoraSyncTaskRecord?> GetLatestWeKnoraSyncTaskForSourceAsync(
        long sourceTaskId,
        string knowledgeBaseId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id,
       source_task_id,
       source_archive_key,
       source_output_directory,
       base_url,
       auth_mode,
       knowledge_base_id,
       knowledge_base_name,
       status,
       created_utc,
       started_utc,
       completed_utc,
       last_heartbeat_utc,
       requested_pause,
       processed_documents,
       total_documents,
       failed_documents,
       current_article_url,
       current_article_index,
       error_message
FROM weknora_sync_tasks
WHERE source_task_id = $sourceTaskId
  AND knowledge_base_id = $knowledgeBaseId
ORDER BY created_utc DESC
LIMIT 1;";
        command.Parameters.AddWithValue("$sourceTaskId", sourceTaskId);
        command.Parameters.AddWithValue("$knowledgeBaseId", knowledgeBaseId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadWeKnoraSyncTask(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<WeKnoraSyncTaskRecord>> GetWeKnoraSyncTasksAsync(string? searchText = null, CancellationToken cancellationToken = default)
    {
        var tasks = new List<WeKnoraSyncTaskRecord>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id,
       source_task_id,
       source_archive_key,
       source_output_directory,
       base_url,
       auth_mode,
       knowledge_base_id,
       knowledge_base_name,
       status,
       created_utc,
       started_utc,
       completed_utc,
       last_heartbeat_utc,
       requested_pause,
       processed_documents,
       total_documents,
       failed_documents,
       current_article_url,
       current_article_index,
       error_message
FROM weknora_sync_tasks
WHERE ($searchText IS NULL
    OR source_archive_key LIKE '%' || $searchText || '%'
    OR source_output_directory LIKE '%' || $searchText || '%'
    OR knowledge_base_id LIKE '%' || $searchText || '%'
    OR IFNULL(knowledge_base_name, '') LIKE '%' || $searchText || '%'
    OR status LIKE '%' || $searchText || '%'
    OR IFNULL(error_message, '') LIKE '%' || $searchText || '%')
ORDER BY created_utc DESC;";
        command.Parameters.AddWithValue("$searchText", (object?)searchText ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tasks.Add(ReadWeKnoraSyncTask(reader));
        }

        return tasks;
    }

    public async Task SetWeKnoraSyncPauseRequestedAsync(long syncTaskId, bool requested, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE weknora_sync_tasks
SET requested_pause = $requestedPause,
    last_heartbeat_utc = $lastHeartbeatUtc
WHERE id = $syncTaskId;";
        command.Parameters.AddWithValue("$syncTaskId", syncTaskId);
        command.Parameters.AddWithValue("$requestedPause", requested ? 1 : 0);
        command.Parameters.AddWithValue("$lastHeartbeatUtc", DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateWeKnoraSyncTaskStatusAsync(
        long syncTaskId,
        ConversionTaskStatus status,
        string? errorMessage = null,
        DateTime? startedUtc = null,
        DateTime? completedUtc = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE weknora_sync_tasks
SET status = $status,
    started_utc = COALESCE($startedUtc, started_utc),
    completed_utc = $completedUtc,
    last_heartbeat_utc = $lastHeartbeatUtc,
    error_message = $errorMessage,
    requested_pause = CASE WHEN $status = 'Running' THEN 0 ELSE requested_pause END
WHERE id = $syncTaskId;";
        command.Parameters.AddWithValue("$syncTaskId", syncTaskId);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$startedUtc", startedUtc.HasValue ? startedUtc.Value.ToString(Iso8601Format, CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$completedUtc", completedUtc.HasValue ? completedUtc.Value.ToString(Iso8601Format, CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$lastHeartbeatUtc", DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateWeKnoraSyncTaskProgressAsync(
        long syncTaskId,
        int processedDocuments,
        int totalDocuments,
        int failedDocuments,
        string? currentArticleUrl,
        int? currentArticleIndex,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE weknora_sync_tasks
SET processed_documents = $processedDocuments,
    total_documents = $totalDocuments,
    failed_documents = $failedDocuments,
    current_article_url = $currentArticleUrl,
    current_article_index = $currentArticleIndex,
    last_heartbeat_utc = $lastHeartbeatUtc
WHERE id = $syncTaskId;";
        command.Parameters.AddWithValue("$syncTaskId", syncTaskId);
        command.Parameters.AddWithValue("$processedDocuments", processedDocuments);
        command.Parameters.AddWithValue("$totalDocuments", totalDocuments);
        command.Parameters.AddWithValue("$failedDocuments", failedDocuments);
        command.Parameters.AddWithValue("$currentArticleUrl", (object?)currentArticleUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$currentArticleIndex", currentArticleIndex.HasValue ? currentArticleIndex.Value : DBNull.Value);
        command.Parameters.AddWithValue("$lastHeartbeatUtc", DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchWeKnoraSyncTaskHeartbeatAsync(long syncTaskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE weknora_sync_tasks
SET last_heartbeat_utc = $lastHeartbeatUtc
WHERE id = $syncTaskId;";
        command.Parameters.AddWithValue("$syncTaskId", syncTaskId);
        command.Parameters.AddWithValue("$lastHeartbeatUtc", DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkInterruptedWeKnoraSyncTasksAsPausedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE weknora_sync_tasks
SET status = 'Paused',
    requested_pause = 1,
    last_heartbeat_utc = $lastHeartbeatUtc,
    error_message = CASE
        WHEN error_message IS NULL OR error_message = '' THEN 'Recovered from previous session interruption.'
        ELSE error_message
    END
WHERE status = 'Running';";
        command.Parameters.AddWithValue("$lastHeartbeatUtc", DateTime.UtcNow.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertWeKnoraSyncItemAsync(WeKnoraSyncItemRecord item, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO weknora_sync_items(
    sync_task_id,
    article_url,
    article_title,
    output_relative_path,
    status,
    attempt_count,
    content_hash,
    remote_knowledge_id,
    remote_parse_status,
    last_error,
    last_processed_utc)
VALUES(
    $syncTaskId,
    $articleUrl,
    $articleTitle,
    $outputRelativePath,
    $status,
    $attemptCount,
    $contentHash,
    $remoteKnowledgeId,
    $remoteParseStatus,
    $lastError,
    $lastProcessedUtc)
ON CONFLICT(sync_task_id, article_url) DO UPDATE SET
    article_title = excluded.article_title,
    output_relative_path = excluded.output_relative_path,
    status = excluded.status,
    attempt_count = excluded.attempt_count,
    content_hash = excluded.content_hash,
    remote_knowledge_id = excluded.remote_knowledge_id,
    remote_parse_status = excluded.remote_parse_status,
    last_error = excluded.last_error,
    last_processed_utc = excluded.last_processed_utc;";

        command.Parameters.AddWithValue("$syncTaskId", item.SyncTaskId);
        command.Parameters.AddWithValue("$articleUrl", item.ArticleUrl);
        command.Parameters.AddWithValue("$articleTitle", (object?)item.ArticleTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputRelativePath", (object?)item.OutputRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", item.Status.ToString());
        command.Parameters.AddWithValue("$attemptCount", item.AttemptCount);
        command.Parameters.AddWithValue("$contentHash", (object?)item.ContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$remoteKnowledgeId", (object?)item.RemoteKnowledgeId ?? DBNull.Value);
        command.Parameters.AddWithValue("$remoteParseStatus", (object?)item.RemoteParseStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastError", (object?)item.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastProcessedUtc", item.LastProcessedUtc.HasValue ? item.LastProcessedUtc.Value.ToString(Iso8601Format, CultureInfo.InvariantCulture) : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WeKnoraSyncItemRecord>> GetWeKnoraSyncItemsAsync(long syncTaskId, CancellationToken cancellationToken = default)
    {
        var items = new List<WeKnoraSyncItemRecord>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id,
       sync_task_id,
       article_url,
       article_title,
       output_relative_path,
       status,
       attempt_count,
       content_hash,
       remote_knowledge_id,
       remote_parse_status,
       last_error,
       last_processed_utc
FROM weknora_sync_items
WHERE sync_task_id = $syncTaskId
ORDER BY article_url COLLATE NOCASE;";
        command.Parameters.AddWithValue("$syncTaskId", syncTaskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadWeKnoraSyncItem(reader));
        }

        return items;
    }

    public async Task WriteWeKnoraSyncLogAsync(WeKnoraSyncLogEntryRecord entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO weknora_sync_log_entries(sync_task_id, timestamp_utc, level, category, message, details, article_url, exception)
VALUES($syncTaskId, $timestampUtc, $level, $category, $message, $details, $articleUrl, $exception);";

        command.Parameters.AddWithValue("$syncTaskId", entry.SyncTaskId.HasValue ? entry.SyncTaskId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$timestampUtc", entry.TimestampUtc.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$level", entry.Level.ToString());
        command.Parameters.AddWithValue("$category", entry.Category);
        command.Parameters.AddWithValue("$message", entry.Message);
        command.Parameters.AddWithValue("$details", (object?)entry.Details ?? DBNull.Value);
        command.Parameters.AddWithValue("$articleUrl", (object?)entry.ArticleUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$exception", (object?)entry.Exception ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WeKnoraSyncLogEntryRecord>> GetWeKnoraSyncLogsAsync(
        string? searchText = null,
        long? syncTaskId = null,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<WeKnoraSyncLogEntryRecord>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id,
       sync_task_id,
       timestamp_utc,
       level,
       category,
       message,
       details,
       article_url,
       exception
FROM weknora_sync_log_entries
WHERE ($syncTaskId IS NULL OR sync_task_id = $syncTaskId)
  AND ($searchText IS NULL
    OR message LIKE '%' || $searchText || '%'
    OR category LIKE '%' || $searchText || '%'
    OR IFNULL(details, '') LIKE '%' || $searchText || '%'
    OR IFNULL(article_url, '') LIKE '%' || $searchText || '%')
ORDER BY timestamp_utc DESC
LIMIT $limit;";
        command.Parameters.AddWithValue("$syncTaskId", syncTaskId.HasValue ? syncTaskId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$searchText", (object?)searchText ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(ReadWeKnoraSyncLogEntry(reader));
        }

        return entries;
    }

    public async Task SaveArchiveMetadataAsync(long taskId, ZimArchiveMetadata metadata, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO archive_metadata(task_id, title, language, publisher, archive_date, article_count, raw_json)
VALUES($taskId, $title, $language, $publisher, $archiveDate, $articleCount, $rawJson)
ON CONFLICT(task_id) DO UPDATE SET
    title = excluded.title,
    language = excluded.language,
    publisher = excluded.publisher,
    archive_date = excluded.archive_date,
    article_count = excluded.article_count,
    raw_json = excluded.raw_json;";
        command.Parameters.AddWithValue("$taskId", taskId);
        command.Parameters.AddWithValue("$title", (object?)metadata.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$language", (object?)metadata.Language ?? DBNull.Value);
        command.Parameters.AddWithValue("$publisher", (object?)metadata.Publisher ?? DBNull.Value);
        command.Parameters.AddWithValue("$archiveDate", (object?)metadata.ArchiveDate ?? DBNull.Value);
        command.Parameters.AddWithValue("$articleCount", metadata.ArticleCount);
        command.Parameters.AddWithValue("$rawJson", JsonSerializer.Serialize(metadata.RawMetadata, JsonDefaults.Options));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ZimArchiveMetadata?> GetArchiveMetadataAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT title, language, publisher, archive_date, article_count, raw_json
FROM archive_metadata
WHERE task_id = $taskId
LIMIT 1;";
        command.Parameters.AddWithValue("$taskId", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ZimArchiveMetadata
        {
            Title = ReadNullableString(reader, 0),
            Language = ReadNullableString(reader, 1),
            Publisher = ReadNullableString(reader, 2),
            ArchiveDate = ReadNullableString(reader, 3),
            ArticleCount = reader.GetInt32(4),
            RawMetadata = DeserializeMetadataDictionary(ReadNullableString(reader, 5))
        };
    }

    public async Task UpsertArticleCheckpointAsync(ArticleCheckpointRecord checkpoint, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO article_checkpoints(
    task_id,
    article_url,
    article_title,
    output_relative_path,
    status,
    attempt_count,
    image_count,
    chunk_count,
    content_hash,
    last_error,
    last_processed_utc)
VALUES(
    $taskId,
    $articleUrl,
    $articleTitle,
    $outputRelativePath,
    $status,
    $attemptCount,
    $imageCount,
    $chunkCount,
    $contentHash,
    $lastError,
    $lastProcessedUtc)
ON CONFLICT(task_id, article_url) DO UPDATE SET
    article_title = excluded.article_title,
    output_relative_path = excluded.output_relative_path,
    status = excluded.status,
    attempt_count = excluded.attempt_count,
    image_count = excluded.image_count,
    chunk_count = excluded.chunk_count,
    content_hash = excluded.content_hash,
    last_error = excluded.last_error,
    last_processed_utc = excluded.last_processed_utc;";

        command.Parameters.AddWithValue("$taskId", checkpoint.TaskId);
        command.Parameters.AddWithValue("$articleUrl", checkpoint.ArticleUrl);
        command.Parameters.AddWithValue("$articleTitle", (object?)checkpoint.ArticleTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputRelativePath", (object?)checkpoint.OutputRelativePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", checkpoint.Status.ToString());
        command.Parameters.AddWithValue("$attemptCount", checkpoint.AttemptCount);
        command.Parameters.AddWithValue("$imageCount", checkpoint.ImageCount);
        command.Parameters.AddWithValue("$chunkCount", checkpoint.ChunkCount);
        command.Parameters.AddWithValue("$contentHash", (object?)checkpoint.ContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastError", (object?)checkpoint.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastProcessedUtc", checkpoint.LastProcessedUtc.HasValue ? checkpoint.LastProcessedUtc.Value.ToString(Iso8601Format, CultureInfo.InvariantCulture) : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ArticleCheckpointRecord>> GetArticleCheckpointsAsync(long taskId, CancellationToken cancellationToken = default)
    {
        var checkpoints = new List<ArticleCheckpointRecord>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id,
       task_id,
       article_url,
       article_title,
       output_relative_path,
       status,
       attempt_count,
       image_count,
       chunk_count,
       content_hash,
       last_error,
       last_processed_utc
FROM article_checkpoints
WHERE task_id = $taskId
ORDER BY article_url COLLATE NOCASE;";
        command.Parameters.AddWithValue("$taskId", taskId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            checkpoints.Add(ReadCheckpoint(reader));
        }

        return checkpoints;
    }

    public async Task WriteLogAsync(LogEntryRecord entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO log_entries(task_id, timestamp_utc, level, category, message, details, article_url, exception)
VALUES($taskId, $timestampUtc, $level, $category, $message, $details, $articleUrl, $exception);";

        command.Parameters.AddWithValue("$taskId", entry.TaskId.HasValue ? entry.TaskId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$timestampUtc", entry.TimestampUtc.ToString(Iso8601Format, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$level", entry.Level.ToString());
        command.Parameters.AddWithValue("$category", entry.Category);
        command.Parameters.AddWithValue("$message", entry.Message);
        command.Parameters.AddWithValue("$details", (object?)entry.Details ?? DBNull.Value);
        command.Parameters.AddWithValue("$articleUrl", (object?)entry.ArticleUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$exception", (object?)entry.Exception ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LogEntryRecord>> GetLogsAsync(
        string? searchText = null,
        long? taskId = null,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<LogEntryRecord>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT id,
       task_id,
       timestamp_utc,
       level,
       category,
       message,
       details,
       article_url,
       exception
FROM log_entries
WHERE ($taskId IS NULL OR task_id = $taskId)
  AND ($searchText IS NULL
    OR message LIKE '%' || $searchText || '%'
    OR category LIKE '%' || $searchText || '%'
    OR IFNULL(details, '') LIKE '%' || $searchText || '%'
    OR IFNULL(article_url, '') LIKE '%' || $searchText || '%')
ORDER BY timestamp_utc DESC
LIMIT $limit;";
        command.Parameters.AddWithValue("$taskId", taskId.HasValue ? taskId.Value : DBNull.Value);
        command.Parameters.AddWithValue("$searchText", (object?)searchText ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(ReadLogEntry(reader));
        }

        return entries;
    }

    private static async Task UpsertSettingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string? value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO settings(key, value)
VALUES($key, $value)
ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Dictionary<string, string> DeserializeMetadataDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonDefaults.Options)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static ZimLibraryItem ReadZimLibraryItem(SqliteDataReader reader)
    {
        return new ZimLibraryItem
        {
            Id = reader.GetInt64(0),
            DisplayName = reader.GetString(1),
            FullPath = reader.GetString(2),
            SizeBytes = reader.GetInt64(3),
            LastWriteUtc = ReadDateTime(reader, 4) ?? DateTime.MinValue,
            Language = ReadNullableString(reader, 5),
            Publisher = ReadNullableString(reader, 6),
            ArchiveDate = ReadNullableString(reader, 7),
            LastScannedUtc = ReadDateTime(reader, 8),
            IsAvailable = reader.GetInt64(9) == 1,
            IsConverted = reader.GetInt64(10) == 1,
            LastCompletedTaskId = ReadNullableInt64(reader, 11)
        };
    }

    private static ConversionTaskRecord ReadTask(SqliteDataReader reader)
    {
        return new ConversionTaskRecord
        {
            Id = reader.GetInt64(0),
            ZimLibraryItemId = reader.GetInt64(1),
            ZimPath = reader.GetString(2),
            ArchiveKey = reader.GetString(3),
            OutputDirectory = reader.GetString(4),
            Status = Enum.TryParse<ConversionTaskStatus>(reader.GetString(5), out var status) ? status : ConversionTaskStatus.Pending,
            CreatedUtc = ReadDateTime(reader, 6) ?? DateTime.MinValue,
            StartedUtc = ReadDateTime(reader, 7),
            CompletedUtc = ReadDateTime(reader, 8),
            LastHeartbeatUtc = ReadDateTime(reader, 9),
            RequestedPause = reader.GetInt64(10) == 1,
            ProcessedArticles = reader.GetInt32(11),
            TotalArticles = reader.GetInt32(12),
            SkippedArticles = reader.GetInt32(13),
            CurrentArticleUrl = ReadNullableString(reader, 14),
            CurrentArticleIndex = ReadNullableInt32(reader, 15),
            ErrorMessage = ReadNullableString(reader, 16)
        };
    }

    private static ArticleCheckpointRecord ReadCheckpoint(SqliteDataReader reader)
    {
        return new ArticleCheckpointRecord
        {
            Id = reader.GetInt64(0),
            TaskId = reader.GetInt64(1),
            ArticleUrl = reader.GetString(2),
            ArticleTitle = ReadNullableString(reader, 3),
            OutputRelativePath = ReadNullableString(reader, 4),
            Status = Enum.TryParse<ArticleStatus>(reader.GetString(5), out var status) ? status : ArticleStatus.Pending,
            AttemptCount = reader.GetInt32(6),
            ImageCount = reader.GetInt32(7),
            ChunkCount = reader.GetInt32(8),
            ContentHash = ReadNullableString(reader, 9),
            LastError = ReadNullableString(reader, 10),
            LastProcessedUtc = ReadDateTime(reader, 11)
        };
    }

    private static LogEntryRecord ReadLogEntry(SqliteDataReader reader)
    {
        return new LogEntryRecord
        {
            Id = reader.GetInt64(0),
            TaskId = ReadNullableInt64(reader, 1),
            TimestampUtc = ReadDateTime(reader, 2) ?? DateTime.MinValue,
            Level = Enum.TryParse<LogSeverity>(reader.GetString(3), out var level) ? level : LogSeverity.Info,
            Category = reader.GetString(4),
            Message = reader.GetString(5),
            Details = ReadNullableString(reader, 6),
            ArticleUrl = ReadNullableString(reader, 7),
            Exception = ReadNullableString(reader, 8)
        };
    }

    private static WeKnoraSyncTaskRecord ReadWeKnoraSyncTask(SqliteDataReader reader)
    {
        return new WeKnoraSyncTaskRecord
        {
            Id = reader.GetInt64(0),
            SourceTaskId = reader.GetInt64(1),
            SourceArchiveKey = reader.GetString(2),
            SourceOutputDirectory = reader.GetString(3),
            BaseUrl = reader.GetString(4),
            AuthMode = reader.GetString(5),
            KnowledgeBaseId = reader.GetString(6),
            KnowledgeBaseName = ReadNullableString(reader, 7),
            Status = Enum.TryParse<ConversionTaskStatus>(reader.GetString(8), out var status) ? status : ConversionTaskStatus.Pending,
            CreatedUtc = ReadDateTime(reader, 9) ?? DateTime.MinValue,
            StartedUtc = ReadDateTime(reader, 10),
            CompletedUtc = ReadDateTime(reader, 11),
            LastHeartbeatUtc = ReadDateTime(reader, 12),
            RequestedPause = reader.GetInt64(13) == 1,
            ProcessedDocuments = reader.GetInt32(14),
            TotalDocuments = reader.GetInt32(15),
            FailedDocuments = reader.GetInt32(16),
            CurrentArticleUrl = ReadNullableString(reader, 17),
            CurrentArticleIndex = ReadNullableInt32(reader, 18),
            ErrorMessage = ReadNullableString(reader, 19)
        };
    }

    private static WeKnoraSyncItemRecord ReadWeKnoraSyncItem(SqliteDataReader reader)
    {
        return new WeKnoraSyncItemRecord
        {
            Id = reader.GetInt64(0),
            SyncTaskId = reader.GetInt64(1),
            ArticleUrl = reader.GetString(2),
            ArticleTitle = ReadNullableString(reader, 3),
            OutputRelativePath = ReadNullableString(reader, 4),
            Status = Enum.TryParse<ArticleStatus>(reader.GetString(5), out var status) ? status : ArticleStatus.Pending,
            AttemptCount = reader.GetInt32(6),
            ContentHash = ReadNullableString(reader, 7),
            RemoteKnowledgeId = ReadNullableString(reader, 8),
            RemoteParseStatus = ReadNullableString(reader, 9),
            LastError = ReadNullableString(reader, 10),
            LastProcessedUtc = ReadDateTime(reader, 11)
        };
    }

    private static WeKnoraSyncLogEntryRecord ReadWeKnoraSyncLogEntry(SqliteDataReader reader)
    {
        return new WeKnoraSyncLogEntryRecord
        {
            Id = reader.GetInt64(0),
            SyncTaskId = ReadNullableInt64(reader, 1),
            TimestampUtc = ReadDateTime(reader, 2) ?? DateTime.MinValue,
            Level = Enum.TryParse<LogSeverity>(reader.GetString(3), out var level) ? level : LogSeverity.Info,
            Category = reader.GetString(4),
            Message = reader.GetString(5),
            Details = ReadNullableString(reader, 6),
            ArticleUrl = ReadNullableString(reader, 7),
            Exception = ReadNullableString(reader, 8)
        };
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt32(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static long? ReadNullableInt64(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static DateTime? ReadDateTime(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return DateTime.TryParse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection($"Data Source={AppPaths.DatabasePath};Cache=Shared;Mode=ReadWriteCreate;Default Timeout=30");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}