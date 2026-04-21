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

    public KiwixAppService()
    {
        _repository = new SqliteRepository();
        _zimdumpClient = new ZimdumpClient();
        _libraryScanner = new LibraryScanner(_repository);
        _conversionCoordinator = new ConversionCoordinator(_repository, _zimdumpClient, new ContentTransformService());
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _repository.InitializeAsync(cancellationToken);
        await _repository.MarkInterruptedTasksAsPausedAsync(cancellationToken);
    }

    public Task<AppSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
        => _repository.GetSettingsAsync(cancellationToken);

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => _repository.SaveSettingsAsync(settings, cancellationToken);

    public async Task<IReadOnlyList<ZimLibraryItem>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.KiwixDesktopDirectory))
        {
            throw new InvalidOperationException("The kiwix-desktop directory must be configured before scanning.");
        }

        return await _libraryScanner.ScanAsync(settings.KiwixDesktopDirectory, cancellationToken);
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
    }

    public Task<ConversionTaskRecord?> GetTaskAsync(long taskId, CancellationToken cancellationToken = default)
        => _repository.GetTaskAsync(taskId, cancellationToken);

    public async Task<string> GetZimdumpVersionAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        return await _zimdumpClient.GetVersionAsync(settings, cancellationToken);
    }
}