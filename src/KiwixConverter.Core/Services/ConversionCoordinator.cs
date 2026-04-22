using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using KiwixConverter.Core.Conversion;
using KiwixConverter.Core.Infrastructure;
using KiwixConverter.Core.Models;

namespace KiwixConverter.Core.Services;

public sealed class ConversionCoordinator
{
    private readonly SqliteRepository _repository;
    private readonly ZimdumpClient _zimdumpClient;
    private readonly ContentTransformService _contentTransformService;
    private readonly ConcurrentDictionary<long, TaskExecution> _runningTasks = new();
    private readonly UTF8Encoding _utf8WithoutBom = new(false);

    public ConversionCoordinator(
        SqliteRepository repository,
        ZimdumpClient zimdumpClient,
        ContentTransformService contentTransformService)
    {
        _repository = repository;
        _zimdumpClient = zimdumpClient;
        _contentTransformService = contentTransformService;
    }

    public async Task StartAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await _repository.SetTaskPauseRequestedAsync(taskId, false, cancellationToken);
        if (_runningTasks.ContainsKey(taskId))
        {
            return;
        }

        var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var execution = new TaskExecution(linkedCancellationTokenSource);
        if (!_runningTasks.TryAdd(taskId, execution))
        {
            linkedCancellationTokenSource.Dispose();
            return;
        }

        execution.WorkerTask = Task.Run(() => RunTaskAsync(taskId, linkedCancellationTokenSource.Token), CancellationToken.None);
    }

    public async Task PauseAsync(long taskId, CancellationToken cancellationToken = default)
    {
        await _repository.SetTaskPauseRequestedAsync(taskId, true, cancellationToken);
        if (_runningTasks.TryGetValue(taskId, out var execution))
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

        var task = await _repository.GetTaskAsync(taskId, cancellationToken);
        if (task is not null && task.Status is ConversionTaskStatus.Pending or ConversionTaskStatus.Running)
        {
            await _repository.UpdateTaskStatusAsync(taskId, ConversionTaskStatus.Paused, task.ErrorMessage, task.StartedUtc, null, cancellationToken);
            await WriteTaskStateSnapshotAsync(taskId, cancellationToken);
        }
    }

    private async Task RunTaskAsync(long taskId, CancellationToken cancellationToken)
    {
        using var snapshotCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task snapshotTask = Task.CompletedTask;

        try
        {
            var task = await _repository.GetTaskAsync(taskId, cancellationToken)
                ?? throw new InvalidOperationException($"Task '{taskId}' was not found.");
            var settings = await _repository.GetSettingsAsync(cancellationToken);
            ValidateTaskSettings(task, settings);

            Directory.CreateDirectory(task.OutputDirectory);

            var startedUtc = task.StartedUtc ?? DateTime.UtcNow;
            await _repository.UpdateTaskStatusAsync(taskId, ConversionTaskStatus.Running, null, startedUtc, null, cancellationToken);
            await LogAsync(taskId, LogSeverity.Info, "task", "Conversion task started.", null, null, null, cancellationToken);

            snapshotTask = RunSnapshotLoopAsync(taskId, Math.Max(5, settings.SnapshotIntervalSeconds), snapshotCancellationTokenSource.Token);

            var archiveMetadata = await _zimdumpClient.GetArchiveMetadataAsync(settings, task.ZimPath, cancellationToken);
            var articles = await _zimdumpClient.ListArticlesAsync(settings, task.ZimPath, cancellationToken);
            if (archiveMetadata.ArticleCount <= 0)
            {
                archiveMetadata.ArticleCount = articles.Count;
            }

            if (string.IsNullOrWhiteSpace(archiveMetadata.Title))
            {
                archiveMetadata.Title = Path.GetFileNameWithoutExtension(task.ZimPath);
            }

            await _repository.SaveArchiveMetadataAsync(taskId, archiveMetadata, cancellationToken);
            await _repository.UpdateZimLibraryMetadataAsync(task.ZimLibraryItemId, archiveMetadata, cancellationToken);
            await WriteArchiveMetadataFileAsync(task, archiveMetadata, cancellationToken);

            var checkpoints = (await _repository.GetArticleCheckpointsAsync(taskId, cancellationToken))
                .ToDictionary(static checkpoint => checkpoint.ArticleUrl, StringComparer.OrdinalIgnoreCase);
            var processedCount = checkpoints.Values.Count(static checkpoint => checkpoint.Status == ArticleStatus.Completed);
            var skippedCount = checkpoints.Values.Count(static checkpoint => checkpoint.Status is ArticleStatus.Skipped or ArticleStatus.Failed);
            await _repository.UpdateTaskProgressAsync(taskId, processedCount, articles.Count, skippedCount, null, null, cancellationToken);
            await WriteTaskStateSnapshotAsync(taskId, cancellationToken);

            for (var index = 0; index < articles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var article = articles[index];
                checkpoints.TryGetValue(article.Url, out var existingCheckpoint);
                if (existingCheckpoint?.Status == ArticleStatus.Completed)
                {
                    await _repository.UpdateTaskProgressAsync(taskId, processedCount, articles.Count, skippedCount, article.Url, index, cancellationToken);
                    continue;
                }

                var attemptCount = (existingCheckpoint?.AttemptCount ?? 0) + 1;
                await _repository.UpdateTaskProgressAsync(taskId, processedCount, articles.Count, skippedCount, article.Url, index, cancellationToken);
                await _repository.UpsertArticleCheckpointAsync(new ArticleCheckpointRecord
                {
                    TaskId = taskId,
                    ArticleUrl = article.Url,
                    ArticleTitle = article.Title,
                    Status = ArticleStatus.InProgress,
                    AttemptCount = attemptCount,
                    LastProcessedUtc = DateTime.UtcNow,
                    LastError = null
                }, cancellationToken);
                await WriteTaskStateSnapshotAsync(taskId, cancellationToken);

                try
                {
                    var result = await ProcessArticleWithFallbackAsync(task, settings, archiveMetadata, article, cancellationToken);
                    processedCount++;

                    var completedCheckpoint = new ArticleCheckpointRecord
                    {
                        TaskId = taskId,
                        ArticleUrl = article.Url,
                        ArticleTitle = result.ArticleTitle,
                        OutputRelativePath = result.OutputRelativePath.Replace('\\', '/'),
                        Status = ArticleStatus.Completed,
                        AttemptCount = attemptCount,
                        ImageCount = result.ImageCount,
                        ChunkCount = result.ChunkCount,
                        ContentHash = result.ContentHash,
                        LastProcessedUtc = DateTime.UtcNow
                    };

                    checkpoints[article.Url] = completedCheckpoint;
                    await _repository.UpsertArticleCheckpointAsync(completedCheckpoint, cancellationToken);
                    await LogAsync(taskId, LogSeverity.Info, "article", $"Article exported: {result.ArticleTitle}", null, article.Url, null, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    skippedCount++;
                    var skippedCheckpoint = new ArticleCheckpointRecord
                    {
                        TaskId = taskId,
                        ArticleUrl = article.Url,
                        ArticleTitle = existingCheckpoint?.ArticleTitle ?? article.Title,
                        OutputRelativePath = existingCheckpoint?.OutputRelativePath,
                        Status = ArticleStatus.Skipped,
                        AttemptCount = attemptCount,
                        ImageCount = existingCheckpoint?.ImageCount ?? 0,
                        ChunkCount = existingCheckpoint?.ChunkCount ?? 0,
                        ContentHash = existingCheckpoint?.ContentHash,
                        LastError = exception.Message,
                        LastProcessedUtc = DateTime.UtcNow
                    };

                    checkpoints[article.Url] = skippedCheckpoint;
                    await _repository.UpsertArticleCheckpointAsync(skippedCheckpoint, cancellationToken);
                    await LogAsync(taskId, LogSeverity.Warning, "article", "Article processing failed and was skipped.", exception.Message, article.Url, exception, cancellationToken);
                }

                await _repository.UpdateTaskProgressAsync(taskId, processedCount, articles.Count, skippedCount, null, null, cancellationToken);
                await WriteTaskStateSnapshotAsync(taskId, cancellationToken);
            }

            await _repository.UpdateTaskProgressAsync(taskId, processedCount, articles.Count, skippedCount, null, null, cancellationToken);
            await _repository.UpdateTaskStatusAsync(taskId, ConversionTaskStatus.Completed, null, startedUtc, DateTime.UtcNow, cancellationToken);
            await WriteTaskStateSnapshotAsync(taskId, cancellationToken);
            await LogAsync(taskId, LogSeverity.Info, "task", "Conversion task completed.", null, null, null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                var currentTask = await _repository.GetTaskAsync(taskId, CancellationToken.None);
                if (currentTask is not null)
                {
                    await _repository.UpdateTaskStatusAsync(taskId, ConversionTaskStatus.Paused, currentTask.ErrorMessage, currentTask.StartedUtc, null, CancellationToken.None);
                    await WriteTaskStateSnapshotAsync(taskId, CancellationToken.None);
                    await LogAsync(taskId, LogSeverity.Info, "task", "Conversion task paused.", null, currentTask.CurrentArticleUrl, null, CancellationToken.None);
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
                var currentTask = await _repository.GetTaskAsync(taskId, CancellationToken.None);
                await _repository.UpdateTaskStatusAsync(
                    taskId,
                    ConversionTaskStatus.Faulted,
                    exception.Message,
                    currentTask?.StartedUtc ?? DateTime.UtcNow,
                    null,
                    CancellationToken.None);
                await WriteTaskStateSnapshotAsync(taskId, CancellationToken.None);
                await LogAsync(taskId, LogSeverity.Error, "task", "Conversion task faulted.", exception.Message, currentTask?.CurrentArticleUrl, exception, CancellationToken.None);
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

            if (_runningTasks.TryRemove(taskId, out var execution))
            {
                execution.CancellationTokenSource.Dispose();
            }
        }
    }

    private async Task<ArticleProcessingResult> ProcessArticleWithFallbackAsync(
        ConversionTaskRecord task,
        AppSettings settings,
        ZimArchiveMetadata archiveMetadata,
        ZimArticleDescriptor article,
        CancellationToken cancellationToken)
    {
        var html = await _zimdumpClient.GetArticleHtmlAsync(settings, task.ZimPath, article, cancellationToken);
        Exception? lastException = null;

        foreach (var usePreparedContent in new[] { true, false })
        {
            try
            {
                return await ExportArticleAsync(task, settings, archiveMetadata, article, html, usePreparedContent, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                var strategy = usePreparedContent ? "main-content" : "raw-body-fallback";
                await LogAsync(task.Id, LogSeverity.Warning, "article", $"Article export strategy '{strategy}' failed.", exception.Message, article.Url, exception, cancellationToken);
            }
        }

        throw new InvalidOperationException($"All export strategies failed for article '{article.Url}'.", lastException);
    }

    private async Task<ArticleProcessingResult> ExportArticleAsync(
        ConversionTaskRecord task,
        AppSettings settings,
        ZimArchiveMetadata archiveMetadata,
        ZimArticleDescriptor article,
        string html,
        bool usePreparedContent,
        CancellationToken cancellationToken)
    {
        var contentRelativePath = LinkPathService.BuildArticleContentRelativePath(article.Url);
        var articleRelativeDirectory = Path.GetDirectoryName(contentRelativePath) ?? string.Empty;
        var articleFullDirectory = Path.Combine(task.OutputDirectory, articleRelativeDirectory);
        Directory.CreateDirectory(articleFullDirectory);

        HtmlDocument document;
        string articleTitle;
        string strategyName;
        if (usePreparedContent)
        {
            var prepared = _contentTransformService.PrepareMainContent(html, article.Title);
            document = _contentTransformService.CreateDocumentFromFragment(prepared.HtmlFragment);
            articleTitle = string.IsNullOrWhiteSpace(prepared.Title) ? article.Title : prepared.Title;
            strategyName = prepared.Strategy;
        }
        else
        {
            var fallbackBody = ExtractBodyHtml(html);
            document = _contentTransformService.CreateDocumentFromFragment(fallbackBody);
            articleTitle = string.IsNullOrWhiteSpace(article.Title) ? Path.GetFileName(article.Url) : article.Title;
            strategyName = "raw-body-fallback";
        }

        var bodyNode = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        var exportedImages = new List<ExportedImage>();
        await RewriteImageSourcesAsync(task, settings, article.Url, articleFullDirectory, bodyNode, exportedImages, cancellationToken);
        RewriteArticleLinks(article.Url, bodyNode);

        var markdown = _contentTransformService.ConvertToMarkdown(document);
        if (markdown.Trim().Length < 40)
        {
            throw new InvalidOperationException("Generated Markdown content was too short to be considered a valid article export.");
        }

        var chunks = _contentTransformService.CreateChunks(markdown, article.Url);
        var contentFullPath = Path.Combine(task.OutputDirectory, contentRelativePath);
        await File.WriteAllTextAsync(contentFullPath, markdown, _utf8WithoutBom, cancellationToken);

        var chunksRelativePath = Path.Combine(articleRelativeDirectory, "chunks.jsonl");
        var chunksFullPath = Path.Combine(task.OutputDirectory, chunksRelativePath);
        await WriteChunksAsync(chunksFullPath, task, archiveMetadata, article.Url, articleTitle, chunks, cancellationToken);

        var contentHash = ComputeHash(markdown);
        var metadataRelativePath = Path.Combine(articleRelativeDirectory, "metadata.json");
        var metadataFullPath = Path.Combine(task.OutputDirectory, metadataRelativePath);
        var metadata = new ArticleExportMetadata
        {
            Title = articleTitle,
            ArticleUrl = article.Url,
            Language = archiveMetadata.Language,
            Publisher = archiveMetadata.Publisher,
            ArchiveDate = archiveMetadata.ArchiveDate,
            ContentPath = contentRelativePath.Replace('\\', '/'),
            ChunksPath = chunksRelativePath.Replace('\\', '/'),
            Images = exportedImages,
            Chunks = chunks.ToList(),
            ContentHash = contentHash,
            ExportedAtUtc = DateTime.UtcNow,
            ArchiveMetadata = new Dictionary<string, string>(archiveMetadata.RawMetadata, StringComparer.OrdinalIgnoreCase)
        };
        await File.WriteAllTextAsync(metadataFullPath, JsonSerializer.Serialize(metadata, JsonDefaults.Options), _utf8WithoutBom, cancellationToken);

        await LogAsync(task.Id, LogSeverity.Trace, "article", $"Article exported using strategy '{strategyName}'.", null, article.Url, null, cancellationToken);

        return new ArticleProcessingResult
        {
            ArticleTitle = articleTitle,
            OutputRelativePath = contentRelativePath,
            ContentHash = contentHash,
            ChunkCount = chunks.Count,
            ImageCount = exportedImages.Count
        };
    }

    private async Task RewriteImageSourcesAsync(
        ConversionTaskRecord task,
        AppSettings settings,
        string articleUrl,
        string articleFullDirectory,
        HtmlNode bodyNode,
        List<ExportedImage> exportedImages,
        CancellationToken cancellationToken)
    {
        var imageNodes = bodyNode.SelectNodes(".//img[@src]")?.ToList() ?? [];
        var sequence = 1;

        foreach (var imageNode in imageNodes)
        {
            var originalSource = imageNode.GetAttributeValue("src", string.Empty);
            if (!LinkPathService.TryNormalizeImageUrl(articleUrl, originalSource, out var normalizedImageUrl))
            {
                continue;
            }

            try
            {
                var imageBytes = await _zimdumpClient.GetBinaryContentAsync(settings, task.ZimPath, normalizedImageUrl, cancellationToken);
                var imageRelativePath = LinkPathService.BuildImageRelativePath(articleUrl, normalizedImageUrl, sequence++);
                var imageFullPath = Path.Combine(task.OutputDirectory, imageRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(imageFullPath) ?? articleFullDirectory);
                await File.WriteAllBytesAsync(imageFullPath, imageBytes, cancellationToken);

                var relativeFromArticle = Path.GetRelativePath(articleFullDirectory, imageFullPath).Replace('\\', '/');
                imageNode.SetAttributeValue("src", relativeFromArticle);
                exportedImages.Add(new ExportedImage
                {
                    SourceUrl = normalizedImageUrl,
                    RelativePath = imageRelativePath.Replace('\\', '/'),
                    AltText = imageNode.GetAttributeValue("alt", null)
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                await LogAsync(task.Id, LogSeverity.Warning, "image", "Image extraction failed and was skipped.", exception.Message, articleUrl, exception, cancellationToken);
            }
        }
    }

    private static void RewriteArticleLinks(string currentArticleUrl, HtmlNode bodyNode)
    {
        var anchorNodes = bodyNode.SelectNodes(".//a[@href]")?.ToList() ?? [];
        foreach (var anchorNode in anchorNodes)
        {
            var href = anchorNode.GetAttributeValue("href", string.Empty);
            if (href.StartsWith('#'))
            {
                continue;
            }

            if (LinkPathService.TryNormalizeArticleUrl(currentArticleUrl, href, out var targetArticleUrl, out var fragment))
            {
                anchorNode.SetAttributeValue("href", LinkPathService.BuildRelativeArticleLink(currentArticleUrl, targetArticleUrl, fragment));
            }
        }
    }

    private async Task RunSnapshotLoopAsync(long taskId, int intervalSeconds, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await _repository.TouchTaskHeartbeatAsync(taskId, cancellationToken);
            await WriteTaskStateSnapshotAsync(taskId, cancellationToken);
        }
    }

    private async Task WriteArchiveMetadataFileAsync(ConversionTaskRecord task, ZimArchiveMetadata archiveMetadata, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(task.OutputDirectory);
        var filePath = Path.Combine(task.OutputDirectory, "archive-metadata.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(archiveMetadata, JsonDefaults.Options), _utf8WithoutBom, cancellationToken);
    }

    private async Task WriteTaskStateSnapshotAsync(long taskId, CancellationToken cancellationToken)
    {
        var task = await _repository.GetTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return;
        }

        Directory.CreateDirectory(task.OutputDirectory);
        var metadata = await _repository.GetArchiveMetadataAsync(taskId, cancellationToken);
        var checkpoints = await _repository.GetArticleCheckpointsAsync(taskId, cancellationToken);
        var snapshot = new
        {
            task.Id,
            task.ZimLibraryItemId,
            task.ZimPath,
            task.ArchiveKey,
            task.OutputDirectory,
            status = task.Status.ToString(),
            task.CreatedUtc,
            task.StartedUtc,
            task.CompletedUtc,
            task.LastHeartbeatUtc,
            task.ProcessedArticles,
            task.TotalArticles,
            task.SkippedArticles,
            task.CurrentArticleUrl,
            task.CurrentArticleIndex,
            task.ErrorMessage,
            snapshotUtc = DateTime.UtcNow,
            archive = metadata,
            checkpointSummary = new
            {
                completed = checkpoints.Count(static checkpoint => checkpoint.Status == ArticleStatus.Completed),
                skipped = checkpoints.Count(static checkpoint => checkpoint.Status is ArticleStatus.Skipped or ArticleStatus.Failed),
                inProgress = checkpoints.Count(static checkpoint => checkpoint.Status == ArticleStatus.InProgress)
            }
        };

        var filePath = Path.Combine(task.OutputDirectory, "task-state.json");
        await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(snapshot, JsonDefaults.Options), _utf8WithoutBom, cancellationToken);
    }

    private async Task WriteChunksAsync(
        string chunksFullPath,
        ConversionTaskRecord task,
        ZimArchiveMetadata archiveMetadata,
        string articleUrl,
        string articleTitle,
        IReadOnlyList<RagChunk> chunks,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(chunksFullPath) ?? task.OutputDirectory);
        await using var stream = new FileStream(chunksFullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, _utf8WithoutBom);

        foreach (var chunk in chunks)
        {
            var line = new
            {
                taskId = task.Id,
                archiveKey = task.ArchiveKey,
                articleUrl,
                title = articleTitle,
                language = archiveMetadata.Language,
                publisher = archiveMetadata.Publisher,
                archiveDate = archiveMetadata.ArchiveDate,
                chunk.ChunkId,
                chunk.Index,
                chunk.Heading,
                chunk.CharacterCount,
                text = chunk.Text
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(line, JsonDefaults.Options));
        }
    }

    private async Task LogAsync(
        long? taskId,
        LogSeverity severity,
        string category,
        string message,
        string? details,
        string? articleUrl,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        await _repository.WriteLogAsync(new LogEntryRecord
        {
            TaskId = taskId,
            TimestampUtc = DateTime.UtcNow,
            Level = severity,
            Category = category,
            Message = message,
            Details = details,
            ArticleUrl = articleUrl,
            Exception = exception?.ToString()
        }, cancellationToken);
    }

    private static string ExtractBodyHtml(string html)
    {
        var document = new HtmlDocument
        {
            OptionFixNestedTags = true,
            OptionAutoCloseOnEnd = true
        };
        document.LoadHtml(html);
        return document.DocumentNode.SelectSingleNode("//body")?.InnerHtml ?? html;
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void ValidateTaskSettings(ConversionTaskRecord task, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.KiwixDesktopDirectory) || !Directory.Exists(settings.KiwixDesktopDirectory))
        {
            throw new DirectoryNotFoundException("The configured kiwix-desktop directory does not exist.");
        }

        if (!File.Exists(task.ZimPath))
        {
            throw new FileNotFoundException("The ZIM file for this task no longer exists.", task.ZimPath);
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