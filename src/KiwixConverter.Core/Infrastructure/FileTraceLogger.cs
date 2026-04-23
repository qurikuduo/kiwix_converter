using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace KiwixConverter.Core.Infrastructure;

public static class FileTraceLogger
{
    private const int MaxLogFiles = 100;
    private const int MaxDataLength = 4096;
    private const int MaxExceptionLength = 16000;

    private static readonly object SyncRoot = new();
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonDefaults.Options)
    {
        WriteIndented = false
    };

    private static string? _resolvedLogDirectory;

    public static string LogDirectory => ResolveLogDirectory();

    public static string CurrentLogFilePath => Path.Combine(LogDirectory, $"kiwix-converter-{DateTime.Now:yyyy-MM-dd}.log");

    public static TraceScope Enter(string source, string method, object? parameters = null)
    {
        Info(source, $"{method} ENTER", parameters);
        return new TraceScope(source, method);
    }

    public static void Info(string source, string eventName, object? data = null)
        => Write("INFO", source, eventName, data, null);

    public static void Warning(string source, string eventName, object? data = null)
        => Write("WARN", source, eventName, data, null);

    public static void Error(string source, string eventName, Exception exception, object? data = null)
        => Write("ERROR", source, eventName, data, exception);

    public static string RedactSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 8
            ? $"{trimmed[..1]}***{trimmed[^1..]} (len={trimmed.Length})"
            : $"{trimmed[..4]}...{trimmed[^4..]} (len={trimmed.Length})";
    }

    public static string SummarizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? "<empty>"
            : Truncate(path.Trim(), 260);
    }

    public static string SummarizeText(string? value, int maxLength = 160)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        return Truncate(Flatten(value.Trim()), Math.Max(32, maxLength));
    }

    private static void Write(string level, string source, string eventName, object? data, Exception? exception)
    {
        try
        {
            var directory = ResolveLogDirectory();
            var filePath = Path.Combine(directory, $"kiwix-converter-{DateTime.Now:yyyy-MM-dd}.log");
            var line = BuildLine(level, source, eventName, data, exception);

            lock (SyncRoot)
            {
                PruneOldFiles(directory);
                File.AppendAllText(filePath, line + Environment.NewLine, Utf8WithoutBom);
            }
        }
        catch
        {
        }
    }

    private static string BuildLine(string level, string source, string eventName, object? data, Exception? exception)
    {
        var builder = new StringBuilder(256);
        builder.Append(DateTimeOffset.Now.ToString("O"));
        builder.Append(" [").Append(level).Append("] ");
        builder.Append("[pid:").Append(Environment.ProcessId).Append(" tid:").Append(Environment.CurrentManagedThreadId).Append("] ");
        builder.Append(source).Append(' ').Append(eventName);

        var serializedData = Serialize(data, MaxDataLength);
        if (!string.IsNullOrWhiteSpace(serializedData))
        {
            builder.Append(" | data=").Append(serializedData);
        }

        if (exception is not null)
        {
            builder.Append(" | exception=").Append(SerializeException(exception));
        }

        return builder.ToString();
    }

    private static string Serialize(object? value, int maxLength)
    {
        if (value is null)
        {
            return string.Empty;
        }

        try
        {
            return Truncate(Flatten(JsonSerializer.Serialize(value, JsonOptions)), maxLength);
        }
        catch
        {
            return Truncate(Flatten(value.ToString() ?? string.Empty), maxLength);
        }
    }

    private static string SerializeException(Exception exception)
    {
        return Truncate(Flatten(exception.ToString()), MaxExceptionLength);
    }

    private static string Flatten(string value)
    {
        return value.Replace("\r", " ").Replace("\n", " ");
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return $"{value[..Math.Max(0, maxLength - 16)]}...(truncated)";
    }

    private static string ResolveLogDirectory()
    {
        if (_resolvedLogDirectory is not null)
        {
            return _resolvedLogDirectory;
        }

        lock (SyncRoot)
        {
            if (_resolvedLogDirectory is not null)
            {
                return _resolvedLogDirectory;
            }

            _resolvedLogDirectory = TryCreateDirectory(AppPaths.LogsDirectory)
                ?? TryCreateDirectory(Path.Combine(Path.GetTempPath(), "KiwixConverter", "logs"))
                ?? throw new InvalidOperationException("Unable to create a logs directory for startup diagnostics.");

            return _resolvedLogDirectory;
        }
    }

    private static string? TryCreateDirectory(string path)
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

    private static void PruneOldFiles(string directory)
    {
        try
        {
            var oldFiles = new DirectoryInfo(directory)
                .EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(static file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Skip(MaxLogFiles)
                .ToList();

            foreach (var file in oldFiles)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    public sealed class TraceScope : IDisposable
    {
        private readonly string _source;
        private readonly string _method;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _completed;

        internal TraceScope(string source, string method)
        {
            _source = source;
            _method = method;
        }

        public void Success(object? result = null)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            Info(_source, $"{_method} EXIT", new { elapsedMs = _stopwatch.ElapsedMilliseconds, result });
        }

        public void Fail(Exception exception, object? data = null)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            Error(_source, $"{_method} EXCEPTION", exception, new { elapsedMs = _stopwatch.ElapsedMilliseconds, data });
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            Info(_source, $"{_method} EXIT", new { elapsedMs = _stopwatch.ElapsedMilliseconds, result = "<implicit>" });
        }
    }
}