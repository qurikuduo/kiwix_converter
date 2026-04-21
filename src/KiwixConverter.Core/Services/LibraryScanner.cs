using KiwixConverter.Core.Infrastructure;
using KiwixConverter.Core.Models;

namespace KiwixConverter.Core.Services;

public sealed class LibraryScanner
{
    private static readonly string[] SupportedPatterns = ["*.zim", "*.zimaa"];

    private readonly SqliteRepository _repository;

    public LibraryScanner(SqliteRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ZimLibraryItem>> ScanAsync(string kiwixDesktopDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(kiwixDesktopDirectory) || !Directory.Exists(kiwixDesktopDirectory))
        {
            throw new DirectoryNotFoundException("The configured kiwix-desktop directory does not exist.");
        }

        var files = SupportedPatterns
            .SelectMany(pattern => Directory.EnumerateFiles(kiwixDesktopDirectory, pattern, SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .Where(static file => file.Exists)
            .OrderBy(static file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _repository.SyncZimLibraryAsync(files, cancellationToken);
        return await _repository.GetZimLibraryItemsAsync(cancellationToken);
    }
}