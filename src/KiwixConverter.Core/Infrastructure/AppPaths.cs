namespace KiwixConverter.Core.Infrastructure;

public static class AppPaths
{
    private static readonly object SyncRoot = new();
    private static string? _resolvedApplicationDataDirectory;
    private static string? _resolvedLogsDirectory;

    public static string ApplicationDataDirectory => ResolveApplicationDataDirectory();

    public static string DatabasePath => Path.Combine(ApplicationDataDirectory, "kiwix-converter.db");

    public static string ScratchDirectory => EnsureDirectory(Path.Combine(ApplicationDataDirectory, "scratch"));

    public static string LogsDirectory => ResolveLogsDirectory();

    public static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveApplicationDataDirectory()
    {
        EnsureStorageLayout();
        return _resolvedApplicationDataDirectory!;
    }

    private static string ResolveLogsDirectory()
    {
        EnsureStorageLayout();
        return _resolvedLogsDirectory!;
    }

    private static void EnsureStorageLayout()
    {
        if (_resolvedApplicationDataDirectory is not null && _resolvedLogsDirectory is not null)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_resolvedApplicationDataDirectory is not null && _resolvedLogsDirectory is not null)
            {
                return;
            }

            if (TryResolvePortableStorage(out var portableDataDirectory, out var portableLogsDirectory))
            {
                TryMigrateLegacyDatabase(portableDataDirectory);
                _resolvedApplicationDataDirectory = portableDataDirectory;
                _resolvedLogsDirectory = portableLogsDirectory;
                return;
            }

            var legacyRootDirectory = EnsureDirectory(GetLegacyRootDirectory());
            _resolvedApplicationDataDirectory = legacyRootDirectory;
            _resolvedLogsDirectory = EnsureDirectory(Path.Combine(legacyRootDirectory, "logs"));
        }
    }

    private static bool TryResolvePortableStorage(out string dataDirectory, out string logsDirectory)
    {
        dataDirectory = string.Empty;
        logsDirectory = string.Empty;

        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return false;
        }

        var candidateDataDirectory = Path.Combine(baseDirectory, "data");
        var candidateLogsDirectory = Path.Combine(baseDirectory, "logs");
        if (TryEnsureDirectory(candidateDataDirectory) is null || TryEnsureDirectory(candidateLogsDirectory) is null)
        {
            return false;
        }

        dataDirectory = candidateDataDirectory;
        logsDirectory = candidateLogsDirectory;
        return true;
    }

    private static void TryMigrateLegacyDatabase(string portableDataDirectory)
    {
        var legacyDatabasePath = Path.Combine(GetLegacyRootDirectory(), "kiwix-converter.db");
        var portableDatabasePath = Path.Combine(portableDataDirectory, "kiwix-converter.db");

        if (File.Exists(portableDatabasePath) || !File.Exists(legacyDatabasePath))
        {
            return;
        }

        try
        {
            File.Copy(legacyDatabasePath, portableDatabasePath);
        }
        catch
        {
        }
    }

    private static string GetLegacyRootDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KiwixConverter");

    private static string? TryEnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch
        {
            return null;
        }
    }
}