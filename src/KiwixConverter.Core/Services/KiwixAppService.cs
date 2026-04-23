using KiwixConverter.Core.Conversion;
using KiwixConverter.Core.Infrastructure;
using KiwixConverter.Core.Models;

namespace KiwixConverter.Core.Services;

public sealed class KiwixAppService
{
    private readonly SqliteRepository _repository;
    private readonly LibraryScanner _libraryScanner;
    private readonly ConversionCoordinator _conversionCoordinator;
    private readonly ZimdumpClient _zimdumpClient;
    private readonly WeKnoraClient _weKnoraClient;
    private readonly WeKnoraSyncCoordinator _weKnoraSyncCoordinator;

    public KiwixAppService()
    {
        _repository = new SqliteRepository();
        _zimdumpClient = new ZimdumpClient();
        _weKnoraClient = new WeKnoraClient();
        _libraryScanner = new LibraryScanner(_repository);
        _conversionCoordinator = new ConversionCoordinator(_repository, _zimdumpClient, new ContentTransformService());
        _weKnoraSyncCoordinator = new WeKnoraSyncCoordinator(_repository, _weKnoraClient);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var scope = FileTraceLogger.Enter(nameof(KiwixAppService), nameof(InitializeAsync));

        try
        {
            await _repository.InitializeAsync(cancellationToken);
            await _repository.MarkInterruptedTasksAsPausedAsync(cancellationToken);
            await _repository.MarkInterruptedWeKnoraSyncTasksAsPausedAsync(cancellationToken);
            scope.Success();
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    public async Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = FileTraceLogger.Enter(nameof(KiwixAppService), nameof(GetSettingsAsync));

        try
        {
            var settings = await _repository.GetSettingsAsync(cancellationToken);
            scope.Success(SummarizeSettings(settings));
            return settings;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        using var scope = FileTraceLogger.Enter(nameof(KiwixAppService), nameof(SaveSettingsAsync), SummarizeSettings(settings));

        try
        {
            await _repository.SaveSettingsAsync(settings, cancellationToken);
            scope.Success();
        }
        catch (Exception exception)
        {
            scope.Fail(exception, SummarizeSettings(settings));
            throw;
        }
    }

    public async Task<IReadOnlyList<ZimLibraryItem>> ScanAsync(CancellationToken cancellationToken = default)
    {
        using var scope = FileTraceLogger.Enter(nameof(KiwixAppService), nameof(ScanAsync));

        try
        {
            var settings = await _repository.GetSettingsAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.KiwixDesktopDirectory))
            {
                throw new InvalidOperationException("The kiwix-desktop directory must be configured before scanning.");
            }

            var items = await _libraryScanner.ScanAsync(settings.KiwixDesktopDirectory, cancellationToken);
            scope.Success(new
            {
                kiwixDesktopDirectory = FileTraceLogger.SummarizePath(settings.KiwixDesktopDirectory),
                count = items.Count
            });
            return items;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    public Task<IReadOnlyList<ZimLibraryItem>> GetDownloadsAsync(CancellationToken cancellationToken = default)
        => _repository.GetZimLibraryItemsAsync(cancellationToken);

    public Task<IReadOnlyList<ConversionTaskRecord>> GetTasksAsync(string? searchText = null, CancellationToken cancellationToken = default)
        => _repository.GetTasksAsync(searchText, cancellationToken);

    public Task<IReadOnlyList<LogEntryRecord>> GetLogsAsync(string? searchText = null, long? taskId = null, int limit = 500, CancellationToken cancellationToken = default)
        => _repository.GetLogsAsync(searchText, taskId, limit, cancellationToken);

    public async Task<long> StartOrResumeTaskAsync(long zimLibraryItemId, string? outputOverride = null, CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        var outputRoot = string.IsNullOrWhiteSpace(outputOverride) ? settings.DefaultOutputDirectory : outputOverride;
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            throw new InvalidOperationException("A default output directory or a per-task output override is required.");
        }

        Directory.CreateDirectory(outputRoot);

        var zimLibraryItem = await _repository.GetZimLibraryItemAsync(zimLibraryItemId, cancellationToken)
            ?? throw new InvalidOperationException($"ZIM library item '{zimLibraryItemId}' was not found.");
        var archiveKey = LinkPathService.BuildArchiveKey(zimLibraryItem.FullPath);
        var archiveOutputDirectory = Path.Combine(outputRoot, archiveKey);
        Directory.CreateDirectory(archiveOutputDirectory);

        var latestTask = await _repository.GetLatestTaskForZimAsync(zimLibraryItemId, cancellationToken);
        long taskId;
        if (latestTask is not null
            && latestTask.Status is not (ConversionTaskStatus.Completed or ConversionTaskStatus.Faulted)
            && string.Equals(latestTask.OutputDirectory, archiveOutputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            taskId = latestTask.Id;
        }
        else
        {
            taskId = await _repository.CreateTaskAsync(zimLibraryItemId, zimLibraryItem.FullPath, archiveKey, archiveOutputDirectory, cancellationToken);
        }

        await _conversionCoordinator.StartAsync(taskId, cancellationToken);
        return taskId;
    }

    public Task PauseTaskAsync(long taskId, CancellationToken cancellationToken = default)
        => _conversionCoordinator.PauseAsync(taskId, cancellationToken);

    public async Task ResumeTaskAsync(long taskId, CancellationToken cancellationToken = default)
        => await _conversionCoordinator.StartAsync(taskId, cancellationToken);

    public async Task PauseAllAsync(CancellationToken cancellationToken = default)
    {
        var tasks = await _repository.GetTasksAsync(null, cancellationToken);
        foreach (var task in tasks.Where(static task => task.Status is ConversionTaskStatus.Running or ConversionTaskStatus.Pending))
        {
            await _conversionCoordinator.PauseAsync(task.Id, cancellationToken);
        }

        var syncTasks = await _repository.GetWeKnoraSyncTasksAsync(null, cancellationToken);
        foreach (var syncTask in syncTasks.Where(static task => task.Status is ConversionTaskStatus.Running or ConversionTaskStatus.Pending))
        {
            await _weKnoraSyncCoordinator.PauseAsync(syncTask.Id, cancellationToken);
        }
    }

    public Task<ConversionTaskRecord?> GetTaskAsync(long taskId, CancellationToken cancellationToken = default)
        => _repository.GetTaskAsync(taskId, cancellationToken);

    public async Task<string> GetZimdumpVersionAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        return await _zimdumpClient.GetVersionAsync(settings, cancellationToken);
    }

    public async Task<ToolAvailabilityResult> GetZimdumpAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        using var scope = FileTraceLogger.Enter(nameof(KiwixAppService), nameof(GetZimdumpAvailabilityAsync));

        var settings = await _repository.GetSettingsAsync(cancellationToken);
        try
        {
            var version = await _zimdumpClient.GetVersionAsync(settings, cancellationToken);
            var result = new ToolAvailabilityResult
            {
                IsAvailable = true,
                ResolvedPath = string.IsNullOrWhiteSpace(settings.ZimdumpExecutablePath) ? "PATH (zimdump)" : settings.ZimdumpExecutablePath,
                Version = version,
                Message = "zimdump is available."
            };
            scope.Success(SummarizeToolAvailability(result));
            return result;
        }
        catch (Exception exception)
        {
            var result = new ToolAvailabilityResult
            {
                IsAvailable = false,
                ResolvedPath = settings.ZimdumpExecutablePath,
                Message = exception.Message
            };
            scope.Success(SummarizeToolAvailability(result));
            return result;
        }
    }

    public async Task<IReadOnlyList<WeKnoraKnowledgeBaseInfo>> GetWeKnoraKnowledgeBasesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = FileTraceLogger.Enter(nameof(KiwixAppService), nameof(GetWeKnoraKnowledgeBasesAsync));

        try
        {
            var settings = await _repository.GetSettingsAsync(cancellationToken);
            var knowledgeBases = await _weKnoraClient.ListKnowledgeBasesAsync(settings, cancellationToken);
            scope.Success(new
            {
                count = knowledgeBases.Count,
                weKnoraBaseUrl = FileTraceLogger.SummarizeText(settings.WeKnoraBaseUrl, 240)
            });
            return knowledgeBases;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    public async Task<IReadOnlyList<WeKnoraModelInfo>> GetWeKnoraModelsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = FileTraceLogger.Enter(nameof(KiwixAppService), nameof(GetWeKnoraModelsAsync));

        try
        {
            var settings = await _repository.GetSettingsAsync(cancellationToken);
            var models = await _weKnoraClient.ListModelsAsync(settings, cancellationToken);
            scope.Success(new
            {
                count = models.Count,
                weKnoraBaseUrl = FileTraceLogger.SummarizeText(settings.WeKnoraBaseUrl, 240)
            });
            return models;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    public async Task<WeKnoraKnowledgeBaseInfo> CreateWeKnoraKnowledgeBaseAsync(string knowledgeBaseName, CancellationToken cancellationToken = default)
    {
        using var scope = FileTraceLogger.Enter(nameof(KiwixAppService), nameof(CreateWeKnoraKnowledgeBaseAsync), new
        {
            knowledgeBaseName = FileTraceLogger.SummarizeText(knowledgeBaseName)
        });

        if (string.IsNullOrWhiteSpace(knowledgeBaseName))
        {
            throw new InvalidOperationException("Enter a WeKnora knowledge base name before creating it.");
        }

        try
        {
            var settings = await _repository.GetSettingsAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.WeKnoraBaseUrl))
            {
                throw new InvalidOperationException("Configure the WeKnora base URL before creating a knowledge base.");
            }

            if (string.IsNullOrWhiteSpace(settings.WeKnoraAccessToken))
            {
                throw new InvalidOperationException("Configure the WeKnora access token before creating a knowledge base.");
            }

            var knowledgeBase = await _weKnoraClient.CreateKnowledgeBaseAsync(
                settings,
                knowledgeBaseName.Trim(),
                string.IsNullOrWhiteSpace(settings.WeKnoraKnowledgeBaseDescription)
                    ? "Imported Markdown articles from Kiwix Converter."
                    : settings.WeKnoraKnowledgeBaseDescription.Trim(),
                cancellationToken);

            await ApplyConfiguredWeKnoraModelsAsync(settings, knowledgeBase.Id, cancellationToken);

            settings.WeKnoraKnowledgeBaseId = knowledgeBase.Id;
            settings.WeKnoraKnowledgeBaseName = knowledgeBase.Name;
            await _repository.SaveSettingsAsync(settings, cancellationToken);
            scope.Success(new
            {
                knowledgeBaseId = FileTraceLogger.SummarizeText(knowledgeBase.Id),
                knowledgeBaseName = FileTraceLogger.SummarizeText(knowledgeBase.Name)
            });
            return knowledgeBase;
        }
        catch (Exception exception)
        {
            scope.Fail(exception, new { knowledgeBaseName = FileTraceLogger.SummarizeText(knowledgeBaseName) });
            throw;
        }
    }

    public async Task<ToolAvailabilityResult> TestWeKnoraConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var scope = FileTraceLogger.Enter(nameof(KiwixAppService), nameof(TestWeKnoraConnectionAsync));

        try
        {
            var knowledgeBases = await GetWeKnoraKnowledgeBasesAsync(cancellationToken);
            var result = new ToolAvailabilityResult
            {
                IsAvailable = true,
                Message = $"Connected to WeKnora. Found {knowledgeBases.Count} knowledge base(s)."
            };
            scope.Success(SummarizeToolAvailability(result));
            return result;
        }
        catch (Exception exception)
        {
            var result = new ToolAvailabilityResult
            {
                IsAvailable = false,
                Message = exception.Message
            };
            scope.Success(SummarizeToolAvailability(result));
            return result;
        }
    }

    public Task<IReadOnlyList<WeKnoraSyncTaskRecord>> GetWeKnoraSyncTasksAsync(string? searchText = null, CancellationToken cancellationToken = default)
        => _repository.GetWeKnoraSyncTasksAsync(searchText, cancellationToken);

    public Task<IReadOnlyList<WeKnoraSyncLogEntryRecord>> GetWeKnoraSyncLogsAsync(string? searchText = null, long? syncTaskId = null, int limit = 500, CancellationToken cancellationToken = default)
        => _repository.GetWeKnoraSyncLogsAsync(searchText, syncTaskId, limit, cancellationToken);

    public Task<WeKnoraSyncTaskRecord?> GetWeKnoraSyncTaskAsync(long syncTaskId, CancellationToken cancellationToken = default)
        => _repository.GetWeKnoraSyncTaskAsync(syncTaskId, cancellationToken);

    public async Task<long> StartOrResumeWeKnoraSyncAsync(long sourceTaskId, CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        var sourceTask = await _repository.GetTaskAsync(sourceTaskId, cancellationToken)
            ?? throw new InvalidOperationException($"Source conversion task '{sourceTaskId}' was not found.");
        var knowledgeBase = await ResolveKnowledgeBaseAsync(settings, cancellationToken);

        await ApplyConfiguredWeKnoraModelsAsync(settings, knowledgeBase.Id, cancellationToken);

        settings.WeKnoraKnowledgeBaseId = knowledgeBase.Id;
        settings.WeKnoraKnowledgeBaseName = knowledgeBase.Name;
        await _repository.SaveSettingsAsync(settings, cancellationToken);

        var latestTask = await _repository.GetLatestWeKnoraSyncTaskForSourceAsync(sourceTaskId, knowledgeBase.Id, cancellationToken);
        long syncTaskId;
        if (latestTask is not null
            && latestTask.Status is not (ConversionTaskStatus.Completed or ConversionTaskStatus.Faulted)
            && string.Equals(latestTask.SourceOutputDirectory, sourceTask.OutputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            syncTaskId = latestTask.Id;
        }
        else
        {
            syncTaskId = await _repository.CreateWeKnoraSyncTaskAsync(
                sourceTaskId,
                sourceTask.ArchiveKey,
                sourceTask.OutputDirectory,
                settings.WeKnoraBaseUrl!.Trim(),
                settings.WeKnoraAuthMode,
                knowledgeBase.Id,
                knowledgeBase.Name,
                cancellationToken);
        }

        await _weKnoraSyncCoordinator.StartAsync(syncTaskId, cancellationToken);
        return syncTaskId;
    }

    public Task PauseWeKnoraSyncTaskAsync(long syncTaskId, CancellationToken cancellationToken = default)
        => _weKnoraSyncCoordinator.PauseAsync(syncTaskId, cancellationToken);

    public Task ResumeWeKnoraSyncTaskAsync(long syncTaskId, CancellationToken cancellationToken = default)
        => _weKnoraSyncCoordinator.StartAsync(syncTaskId, cancellationToken);

    private async Task<WeKnoraKnowledgeBaseInfo> ResolveKnowledgeBaseAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.WeKnoraBaseUrl))
        {
            throw new InvalidOperationException("Configure the WeKnora base URL before starting a sync task.");
        }

        if (string.IsNullOrWhiteSpace(settings.WeKnoraAccessToken))
        {
            throw new InvalidOperationException("Configure the WeKnora access token before starting a sync task.");
        }

        var knowledgeBases = await _weKnoraClient.ListKnowledgeBasesAsync(settings, cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.WeKnoraKnowledgeBaseId))
        {
            var byId = knowledgeBases.FirstOrDefault(kb => string.Equals(kb.Id, settings.WeKnoraKnowledgeBaseId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.WeKnoraKnowledgeBaseName))
        {
            var byName = knowledgeBases.FirstOrDefault(kb => string.Equals(kb.Name, settings.WeKnoraKnowledgeBaseName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
            {
                return byName;
            }

            if (settings.WeKnoraAutoCreateKnowledgeBase)
            {
                return await _weKnoraClient.CreateKnowledgeBaseAsync(
                    settings,
                    settings.WeKnoraKnowledgeBaseName.Trim(),
                    string.IsNullOrWhiteSpace(settings.WeKnoraKnowledgeBaseDescription)
                        ? "Imported Markdown articles from Kiwix Converter."
                        : settings.WeKnoraKnowledgeBaseDescription.Trim(),
                    cancellationToken);
            }
        }

        if (knowledgeBases.Count == 1)
        {
            return knowledgeBases[0];
        }

        throw new InvalidOperationException("Select an existing WeKnora knowledge base ID/name, or enable auto-create with a knowledge base name.");
    }

    private Task ApplyConfiguredWeKnoraModelsAsync(AppSettings settings, string knowledgeBaseId, CancellationToken cancellationToken)
        => _weKnoraClient.UpdateKnowledgeBaseInitializationAsync(
            settings,
            knowledgeBaseId,
            settings.WeKnoraChatModelId,
            settings.WeKnoraMultimodalModelId,
            cancellationToken);

    private static object SummarizeSettings(AppSettings settings)
    {
        return new
        {
            kiwixDesktopDirectory = FileTraceLogger.SummarizePath(settings.KiwixDesktopDirectory),
            defaultOutputDirectory = FileTraceLogger.SummarizePath(settings.DefaultOutputDirectory),
            zimdumpExecutablePath = FileTraceLogger.SummarizePath(settings.ZimdumpExecutablePath),
            weKnoraBaseUrl = FileTraceLogger.SummarizeText(settings.WeKnoraBaseUrl, 240),
            weKnoraAccessToken = FileTraceLogger.RedactSecret(settings.WeKnoraAccessToken),
            weKnoraKnowledgeBaseId = FileTraceLogger.SummarizeText(settings.WeKnoraKnowledgeBaseId),
            weKnoraKnowledgeBaseName = FileTraceLogger.SummarizeText(settings.WeKnoraKnowledgeBaseName),
            weKnoraChatModelId = FileTraceLogger.SummarizeText(settings.WeKnoraChatModelId),
            weKnoraEmbeddingModelId = FileTraceLogger.SummarizeText(settings.WeKnoraEmbeddingModelId),
            weKnoraMultimodalModelId = FileTraceLogger.SummarizeText(settings.WeKnoraMultimodalModelId),
            weKnoraAuthMode = settings.WeKnoraAuthMode.ToString()
        };
    }

    private static object SummarizeToolAvailability(ToolAvailabilityResult result)
    {
        return new
        {
            result.IsAvailable,
            resolvedPath = FileTraceLogger.SummarizePath(result.ResolvedPath),
            version = FileTraceLogger.SummarizeText(result.Version, 240),
            message = FileTraceLogger.SummarizeText(result.Message, 320)
        };
    }
}