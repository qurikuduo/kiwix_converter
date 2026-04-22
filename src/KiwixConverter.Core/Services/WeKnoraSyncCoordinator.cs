using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using KiwixConverter.Core.Infrastructure;
using KiwixConverter.Core.Models;

namespace KiwixConverter.Core.Services;

public sealed class WeKnoraSyncCoordinator
{
    private readonly SqliteRepository _repository;
    private readonly WeKnoraClient _weKnoraClient;
    private readonly ConcurrentDictionary<long, TaskExecution> _runningTasks = new();
    private readonly UTF8Encoding _utf8WithoutBom = new(false);

    public WeKnoraSyncCoordinator(SqliteRepository repository, WeKnoraClient weKnoraClient)
    {
        _repository = repository;
        _weKnoraClient = weKnoraClient;
    }

    public async Task StartAsync(long syncTaskId, CancellationToken cancellationToken = default)
    {
        await _repository.SetWeKnoraSyncPauseRequestedAsync(syncTaskId, false, cancellationToken);
        if (_runningTasks.ContainsKey(syncTaskId))
        {
            return;
        }

        var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var execution = new TaskExecution(linkedCancellationTokenSource);
        if (!_runningTasks.TryAdd(syncTaskId, execution))
        {
            linkedCancellationTokenSource.Dispose();
            return;
        }

        execution.WorkerTask = Task.Run(() => RunTaskAsync(syncTaskId, linkedCancellationTokenSource.Token), CancellationToken.None);
    }

    public async Task PauseAsync(long syncTaskId, CancellationToken cancellationToken = default)
    {
        await _repository.SetWeKnoraSyncPauseRequestedAsync(syncTaskId, true, cancellationToken);
        if (_runningTasks.TryGetValue(syncTaskId, out var execution))
        {
            execution.CancellationTokenSource.Cancel();
            if (execution.WorkerTask is not null)
            {
                try
                {
                    await execution.WorkerTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            return;
        }

        var task = await _repository.GetWeKnoraSyncTaskAsync(syncTaskId, cancellationToken);
        if (task is not null && task.Status is ConversionTaskStatus.Pending or ConversionTaskStatus.Running)
        {
            await _repository.UpdateWeKnoraSyncTaskStatusAsync(syncTaskId, ConversionTaskStatus.Paused, task.ErrorMessage, task.StartedUtc, null, cancellationToken);
            await WriteTaskStateSnapshotAsync(syncTaskId, cancellationToken);
        }
    }

    private async Task RunTaskAsync(long syncTaskId, CancellationToken cancellationToken)
    {
        using var snapshotCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task snapshotTask = Task.CompletedTask;

        try
        {
            var syncTask = await _repository.GetWeKnoraSyncTaskAsync(syncTaskId, cancellationToken)
                ?? throw new InvalidOperationException($"WeKnora sync task '{syncTaskId}' was not found.");
            var sourceTask = await _repository.GetTaskAsync(syncTask.SourceTaskId, cancellationToken)
                ?? throw new InvalidOperationException($"Source conversion task '{syncTask.SourceTaskId}' was not found.");
            var settings = await _repository.GetSettingsAsync(cancellationToken);
            ValidateSyncTaskSettings(syncTask, sourceTask, settings);

            Directory.CreateDirectory(syncTask.SourceOutputDirectory);

            var startedUtc = syncTask.StartedUtc ?? DateTime.UtcNow;
            await _repository.UpdateWeKnoraSyncTaskStatusAsync(syncTaskId, ConversionTaskStatus.Running, null, startedUtc, null, cancellationToken);
            await LogAsync(syncTaskId, LogSeverity.Info, "task", "WeKnora sync task started.", null, null, null, cancellationToken);

            snapshotTask = RunSnapshotLoopAsync(syncTaskId, Math.Max(5, settings.SnapshotIntervalSeconds), snapshotCancellationTokenSource.Token);

            var archiveMetadata = await _repository.GetArchiveMetadataAsync(syncTask.SourceTaskId, cancellationToken);
            var sourceCheckpoints = (await _repository.GetArticleCheckpointsAsync(syncTask.SourceTaskId, cancellationToken))
                .Where(static checkpoint => checkpoint.Status == ArticleStatus.Completed && !string.IsNullOrWhiteSpace(checkpoint.OutputRelativePath))
                .ToList();
            if (sourceCheckpoints.Count == 0)
            {
                throw new InvalidOperationException("No completed exported articles were found for the selected conversion task.");
            }

            var sourceCheckpointsByUrl = sourceCheckpoints.ToDictionary(static checkpoint => checkpoint.ArticleUrl, StringComparer.OrdinalIgnoreCase);
            var syncItems = (await _repository.GetWeKnoraSyncItemsAsync(syncTaskId, cancellationToken))
                .ToDictionary(static item => item.ArticleUrl, StringComparer.OrdinalIgnoreCase);
            var processedCount = syncItems.Values.Count(item =>
                item.Status == ArticleStatus.Completed
                && sourceCheckpointsByUrl.TryGetValue(item.ArticleUrl, out var sourceCheckpoint)
                && string.Equals(sourceCheckpoint.ContentHash, item.ContentHash, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.RemoteKnowledgeId));
            var failedCount = syncItems.Values.Count(static item => item.Status is ArticleStatus.Failed or ArticleStatus.Skipped);
            await _repository.UpdateWeKnoraSyncTaskProgressAsync(syncTaskId, processedCount, sourceCheckpoints.Count, failedCount, null, null, cancellationToken);
            await WriteTaskStateSnapshotAsync(syncTaskId, cancellationToken);

            for (var index = 0; index < sourceCheckpoints.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var checkpoint = sourceCheckpoints[index];
                syncItems.TryGetValue(checkpoint.ArticleUrl, out var existingItem);
                var alreadySynced = existingItem is not null
                    && existingItem.Status == ArticleStatus.Completed
                    && !string.IsNullOrWhiteSpace(existingItem.RemoteKnowledgeId)
                    && string.Equals(existingItem.ContentHash, checkpoint.ContentHash, StringComparison.OrdinalIgnoreCase);

                await _repository.UpdateWeKnoraSyncTaskProgressAsync(syncTaskId, processedCount, sourceCheckpoints.Count, failedCount, checkpoint.ArticleUrl, index, cancellationToken);
                if (alreadySynced)
                {
                    continue;
                }

                var attemptCount = (existingItem?.AttemptCount ?? 0) + 1;
                var inProgressItem = new WeKnoraSyncItemRecord
                {
                    SyncTaskId = syncTaskId,
                    ArticleUrl = checkpoint.ArticleUrl,
                    ArticleTitle = checkpoint.ArticleTitle,
                    OutputRelativePath = checkpoint.OutputRelativePath,
                    Status = ArticleStatus.InProgress,
                    AttemptCount = attemptCount,
                    ContentHash = checkpoint.ContentHash,
                    RemoteKnowledgeId = existingItem?.RemoteKnowledgeId,
                    RemoteParseStatus = existingItem?.RemoteParseStatus,
                    LastProcessedUtc = DateTime.UtcNow
                };
                syncItems[checkpoint.ArticleUrl] = inProgressItem;
                await _repository.UpsertWeKnoraSyncItemAsync(inProgressItem, cancellationToken);
                await WriteTaskStateSnapshotAsync(syncTaskId, cancellationToken);

                try
                {
                    var markdownPath = Path.Combine(sourceTask.OutputDirectory, checkpoint.OutputRelativePath!.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(markdownPath))
                    {
                        throw new FileNotFoundException("The exported Markdown file for this article could not be found.", markdownPath);
                    }

                    var markdown = await File.ReadAllTextAsync(markdownPath, cancellationToken);
                    if (string.IsNullOrWhiteSpace(markdown))
                    {
                        throw new InvalidOperationException("The exported Markdown file was empty.");
                    }

                    var articleTitle = string.IsNullOrWhiteSpace(checkpoint.ArticleTitle)
                        ? Path.GetFileNameWithoutExtension(markdownPath)
                        : checkpoint.ArticleTitle;
                    var content = BuildSyncContent(markdown, checkpoint, syncTask, sourceTask, archiveMetadata, settings);

                    WeKnoraKnowledgeItemInfo knowledge;
                    if (!string.IsNullOrWhiteSpace(existingItem?.RemoteKnowledgeId))
                    {
                        knowledge = await _weKnoraClient.UpdateManualKnowledgeAsync(settings, existingItem.RemoteKnowledgeId, articleTitle, content, cancellationToken);
                    }
                    else
                    {
                        knowledge = await _weKnoraClient.CreateManualKnowledgeAsync(settings, syncTask.KnowledgeBaseId, articleTitle, content, cancellationToken);
                    }

                    inProgressItem.RemoteKnowledgeId = knowledge.Id;
                    inProgressItem.RemoteParseStatus = knowledge.ParseStatus;
                    inProgressItem.LastProcessedUtc = DateTime.UtcNow;
                    await _repository.UpsertWeKnoraSyncItemAsync(inProgressItem, cancellationToken);

                    var completedKnowledge = await WaitForKnowledgeReadyAsync(settings, knowledge.Id, cancellationToken);
                    processedCount++;

                    var completedItem = new WeKnoraSyncItemRecord
                    {
                        SyncTaskId = syncTaskId,
                        ArticleUrl = checkpoint.ArticleUrl,
                        ArticleTitle = articleTitle,
                        OutputRelativePath = checkpoint.OutputRelativePath,
                        Status = ArticleStatus.Completed,
                        AttemptCount = attemptCount,
                        ContentHash = checkpoint.ContentHash,
                        RemoteKnowledgeId = completedKnowledge.Id,
                        RemoteParseStatus = completedKnowledge.ParseStatus,
                        LastProcessedUtc = DateTime.UtcNow
                    };

                    syncItems[checkpoint.ArticleUrl] = completedItem;
                    await _repository.UpsertWeKnoraSyncItemAsync(completedItem, cancellationToken);
                    await LogAsync(syncTaskId, LogSeverity.Info, "article", $"Article synced to WeKnora: {articleTitle}", completedKnowledge.Id, checkpoint.ArticleUrl, null, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    failedCount++;
                    var failedItem = new WeKnoraSyncItemRecord
                    {
                        SyncTaskId = syncTaskId,
                        ArticleUrl = checkpoint.ArticleUrl,
                        ArticleTitle = checkpoint.ArticleTitle,
                        OutputRelativePath = checkpoint.OutputRelativePath,
                        Status = ArticleStatus.Failed,
                        AttemptCount = attemptCount,
                        ContentHash = checkpoint.ContentHash,
                        RemoteKnowledgeId = existingItem?.RemoteKnowledgeId,
                        RemoteParseStatus = existingItem?.RemoteParseStatus,
                        LastError = exception.Message,
                        LastProcessedUtc = DateTime.UtcNow
                    };

                    syncItems[checkpoint.ArticleUrl] = failedItem;
                    await _repository.UpsertWeKnoraSyncItemAsync(failedItem, cancellationToken);
                    await LogAsync(syncTaskId, LogSeverity.Warning, "article", "Article sync failed and can be retried later.", exception.Message, checkpoint.ArticleUrl, exception, cancellationToken);
                }

                await _repository.UpdateWeKnoraSyncTaskProgressAsync(syncTaskId, processedCount, sourceCheckpoints.Count, failedCount, null, null, cancellationToken);
                await WriteTaskStateSnapshotAsync(syncTaskId, cancellationToken);
            }

            await _repository.UpdateWeKnoraSyncTaskProgressAsync(syncTaskId, processedCount, sourceCheckpoints.Count, failedCount, null, null, cancellationToken);
            await _repository.UpdateWeKnoraSyncTaskStatusAsync(syncTaskId, ConversionTaskStatus.Completed, null, startedUtc, DateTime.UtcNow, cancellationToken);
            await WriteTaskStateSnapshotAsync(syncTaskId, cancellationToken);
            await LogAsync(syncTaskId, LogSeverity.Info, "task", "WeKnora sync task completed.", null, null, null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                var currentTask = await _repository.GetWeKnoraSyncTaskAsync(syncTaskId, CancellationToken.None);
                if (currentTask is not null)
                {
                    await _repository.UpdateWeKnoraSyncTaskStatusAsync(syncTaskId, ConversionTaskStatus.Paused, currentTask.ErrorMessage, currentTask.StartedUtc, null, CancellationToken.None);
                    await WriteTaskStateSnapshotAsync(syncTaskId, CancellationToken.None);
                    await LogAsync(syncTaskId, LogSeverity.Info, "task", "WeKnora sync task paused.", null, currentTask.CurrentArticleUrl, null, CancellationToken.None);
                }
            }
            catch
            {
            }
        }
        catch (Exception exception)
        {
            try
            {
                var currentTask = await _repository.GetWeKnoraSyncTaskAsync(syncTaskId, CancellationToken.None);
                await _repository.UpdateWeKnoraSyncTaskStatusAsync(
                    syncTaskId,
                    ConversionTaskStatus.Faulted,
                    exception.Message,
                    currentTask?.StartedUtc ?? DateTime.UtcNow,
                    null,
                    CancellationToken.None);
                await WriteTaskStateSnapshotAsync(syncTaskId, CancellationToken.None);
                await LogAsync(syncTaskId, LogSeverity.Error, "task", "WeKnora sync task faulted.", exception.Message, currentTask?.CurrentArticleUrl, exception, CancellationToken.None);
            }
            catch
            {
            }
        }
        finally
        {
            snapshotCancellationTokenSource.Cancel();
            try
            {
                await snapshotTask;
            }
            catch
            {
            }

            if (_runningTasks.TryRemove(syncTaskId, out var execution))
            {
                execution.CancellationTokenSource.Dispose();
            }
        }
    }

    private async Task<WeKnoraKnowledgeItemInfo> WaitForKnowledgeReadyAsync(AppSettings settings, string knowledgeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(knowledgeId))
        {
            throw new InvalidOperationException("WeKnora did not return a knowledge ID for the synced article.");
        }

        WeKnoraKnowledgeItemInfo? lastKnownState = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lastKnownState = await _weKnoraClient.GetKnowledgeAsync(settings, knowledgeId, cancellationToken);
            var parseStatus = lastKnownState.ParseStatus?.Trim();
            if (string.IsNullOrWhiteSpace(parseStatus)
                || parseStatus.Equals("completed", StringComparison.OrdinalIgnoreCase)
                || parseStatus.Equals("success", StringComparison.OrdinalIgnoreCase)
                || parseStatus.Equals("succeeded", StringComparison.OrdinalIgnoreCase)
                || parseStatus.Equals("ready", StringComparison.OrdinalIgnoreCase))
            {
                return lastKnownState;
            }

            if (parseStatus.Equals("failed", StringComparison.OrdinalIgnoreCase)
                || parseStatus.Equals("error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(lastKnownState.ErrorMessage)
                    ? $"WeKnora reported parse_status '{parseStatus}' for knowledge '{knowledgeId}'."
                    : lastKnownState.ErrorMessage);
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new InvalidOperationException($"Timed out while waiting for WeKnora to finish processing knowledge '{knowledgeId}' (last status: {lastKnownState?.ParseStatus ?? "unknown"}).");
    }

    private static string BuildSyncContent(
        string markdown,
        ArticleCheckpointRecord checkpoint,
        WeKnoraSyncTaskRecord syncTask,
        ConversionTaskRecord sourceTask,
        ZimArchiveMetadata? archiveMetadata,
        AppSettings settings)
    {
        if (!settings.WeKnoraAppendMetadataBlock)
        {
            return markdown;
        }

        var builder = new StringBuilder(markdown.TrimEnd());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine("## Source Metadata");
        builder.AppendLine();
        builder.AppendLine($"- Article URL: {checkpoint.ArticleUrl}");
        builder.AppendLine($"- Archive Key: {sourceTask.ArchiveKey}");
        builder.AppendLine($"- Sync Task ID: {syncTask.Id}");

        if (!string.IsNullOrWhiteSpace(archiveMetadata?.Title))
        {
            builder.AppendLine($"- Archive Title: {archiveMetadata.Title}");
        }

        if (!string.IsNullOrWhiteSpace(archiveMetadata?.Language))
        {
            builder.AppendLine($"- Language: {archiveMetadata.Language}");
        }

        if (!string.IsNullOrWhiteSpace(archiveMetadata?.Publisher))
        {
            builder.AppendLine($"- Publisher: {archiveMetadata.Publisher}");
        }

        if (!string.IsNullOrWhiteSpace(archiveMetadata?.ArchiveDate))
        {
            builder.AppendLine($"- Archive Date: {archiveMetadata.ArchiveDate}");
        }

        if (!string.IsNullOrWhiteSpace(checkpoint.ContentHash))
        {
            builder.AppendLine($"- Export Hash: {checkpoint.ContentHash}");
        }

        builder.AppendLine($"- Exported By: Kiwix Converter");
        return builder.ToString();
    }

    private async Task RunSnapshotLoopAsync(long syncTaskId, int intervalSeconds, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await _repository.TouchWeKnoraSyncTaskHeartbeatAsync(syncTaskId, cancellationToken);
            await WriteTaskStateSnapshotAsync(syncTaskId, cancellationToken);
        }
    }

    private async Task WriteTaskStateSnapshotAsync(long syncTaskId, CancellationToken cancellationToken)
    {
        var task = await _repository.GetWeKnoraSyncTaskAsync(syncTaskId, cancellationToken);
        if (task is null)
        {
            return;
        }

        Directory.CreateDirectory(task.SourceOutputDirectory);
        var syncItems = await _repository.GetWeKnoraSyncItemsAsync(syncTaskId, cancellationToken);
        var snapshot = new
        {
            task.Id,
            task.SourceTaskId,
            task.SourceArchiveKey,
            task.SourceOutputDirectory,
            task.BaseUrl,
            task.AuthMode,
            task.KnowledgeBaseId,
            task.KnowledgeBaseName,
            status = task.Status.ToString(),
            task.CreatedUtc,
            task.StartedUtc,
            task.CompletedUtc,
            task.LastHeartbeatUtc,
            task.ProcessedDocuments,
            task.TotalDocuments,
            task.FailedDocuments,
            task.CurrentArticleUrl,
            task.CurrentArticleIndex,
            task.ErrorMessage,
            snapshotUtc = DateTime.UtcNow,
            checkpointSummary = new
            {
                completed = syncItems.Count(static item => item.Status == ArticleStatus.Completed),
                failed = syncItems.Count(static item => item.Status is ArticleStatus.Failed or ArticleStatus.Skipped),
                inProgress = syncItems.Count(static item => item.Status == ArticleStatus.InProgress)
            }
        };

        var filePath = Path.Combine(task.SourceOutputDirectory, $"weknora-sync-state-{task.Id}.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(snapshot, JsonDefaults.Options), _utf8WithoutBom, cancellationToken);
    }

    private async Task LogAsync(
        long? syncTaskId,
        LogSeverity severity,
        string category,
        string message,
        string? details,
        string? articleUrl,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        await _repository.WriteWeKnoraSyncLogAsync(new WeKnoraSyncLogEntryRecord
        {
            SyncTaskId = syncTaskId,
            TimestampUtc = DateTime.UtcNow,
            Level = severity,
            Category = category,
            Message = message,
            Details = details,
            ArticleUrl = articleUrl,
            Exception = exception?.ToString()
        }, cancellationToken);
    }

    private static void ValidateSyncTaskSettings(WeKnoraSyncTaskRecord syncTask, ConversionTaskRecord sourceTask, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.WeKnoraBaseUrl))
        {
            throw new InvalidOperationException("The WeKnora base URL must be configured before syncing.");
        }

        if (string.IsNullOrWhiteSpace(settings.WeKnoraAccessToken))
        {
            throw new InvalidOperationException("The WeKnora access token must be configured before syncing.");
        }

        if (string.IsNullOrWhiteSpace(syncTask.KnowledgeBaseId))
        {
            throw new InvalidOperationException("The WeKnora knowledge base ID is missing for this sync task.");
        }

        if (!Directory.Exists(sourceTask.OutputDirectory))
        {
            throw new DirectoryNotFoundException("The exported archive directory for the source conversion task no longer exists.");
        }
    }

    private sealed class TaskExecution
    {
        public TaskExecution(CancellationTokenSource cancellationTokenSource)
        {
            CancellationTokenSource = cancellationTokenSource;
        }

        public CancellationTokenSource CancellationTokenSource { get; }

        public Task? WorkerTask { get; set; }
    }
}