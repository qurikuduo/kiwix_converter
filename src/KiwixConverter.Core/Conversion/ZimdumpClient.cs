using System.Diagnostics;
using System.Text;
using KiwixConverter.Core.Infrastructure;
using KiwixConverter.Core.Models;

namespace KiwixConverter.Core.Conversion;

public sealed class ZimdumpClient
{
    public async Task<ZimArchiveMetadata> GetArchiveMetadataAsync(AppSettings settings, string zimPath, CancellationToken cancellationToken = default)
    {
        var output = await ExecuteTextUsingStrategiesAsync(
            settings,
            [
                ["info", zimPath],
                ["-F", zimPath]
            ],
            cancellationToken);
        var metadata = ParseMetadata(output);
        return metadata;
    }

    public async Task<IReadOnlyList<ZimArticleDescriptor>> ListArticlesAsync(AppSettings settings, string zimPath, CancellationToken cancellationToken = default)
    {
        var strategies = new[]
        {
            new[] { "list", "--details", "--ns=A", zimPath },
            new[] { "-l", "-n", "A", zimPath },
            new[] { "-L", "-n", "A", zimPath },
            new[] { "-i", "-n", "A", zimPath }
        };

        foreach (var strategy in strategies)
        {
            var output = await ExecuteTextAsync(settings, strategy, cancellationToken);
            var articles = ParseArticleListing(output);
            if (articles.Count > 0)
            {
                return articles;
            }
        }

        throw new InvalidOperationException("No article entries could be enumerated from the ZIM archive using zimdump.");
    }

    public async Task<string> GetArticleHtmlAsync(AppSettings settings, string zimPath, ZimArticleDescriptor article, CancellationToken cancellationToken = default)
    {
        var lastException = default(Exception);

        if (article.Index.HasValue)
        {
            try
            {
                var bytes = await ExecuteBytesAsync(settings, ["show", $"--idx={article.Index.Value}", zimPath], cancellationToken);
                var text = DecodeText(bytes).Trim();
                if (LooksLikeHtml(text))
                {
                    return text;
                }
            }
            catch (Exception exception)
            {
                lastException = exception;
            }
        }

        foreach (var candidate in BuildUrlCandidates(article.Url))
        {
            foreach (var arguments in BuildShowArguments(zimPath, candidate))
            {
                try
                {
                    var bytes = await ExecuteBytesAsync(settings, arguments, cancellationToken);
                    var text = DecodeText(bytes).Trim();
                    if (LooksLikeHtml(text))
                    {
                        return text;
                    }
                }
                catch (Exception exception)
                {
                    lastException = exception;
                }
            }
        }

        throw new InvalidOperationException($"Unable to extract article HTML for '{article.Url}'.", lastException);
    }

    public async Task<byte[]> GetBinaryContentAsync(AppSettings settings, string zimPath, string entryUrl, CancellationToken cancellationToken = default)
    {
        var lastException = default(Exception);
        foreach (var candidate in BuildUrlCandidates(entryUrl))
        {
            foreach (var arguments in BuildShowArguments(zimPath, candidate))
            {
                try
                {
                    var bytes = await ExecuteBytesAsync(settings, arguments, cancellationToken);
                    if (bytes.Length > 0)
                    {
                        return bytes;
                    }
                }
                catch (Exception exception)
                {
                    lastException = exception;
                }
            }
        }

        throw new InvalidOperationException($"Unable to extract binary content for '{entryUrl}'.", lastException);
    }

    public async Task<string> GetVersionAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        using var scope = FileTraceLogger.Enter(nameof(ZimdumpClient), nameof(GetVersionAsync), new
        {
            zimdumpExecutablePath = FileTraceLogger.SummarizePath(settings.ZimdumpExecutablePath)
        });

        try
        {
            var version = (await ExecuteTextUsingStrategiesAsync(
                settings,
                [
                    ["--version"],
                    ["-V"]
                ],
                cancellationToken)).Trim();
            scope.Success(new { version = FileTraceLogger.SummarizeText(version, 240) });
            return version;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    private static IEnumerable<string> BuildUrlCandidates(string url)
    {
        var raw = url.Trim();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            yield return raw;
            yield return raw.TrimStart('/');
        }

        var normalized = LinkPathService.NormalizeUrl(raw, raw.StartsWith("I/", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("/I/", StringComparison.OrdinalIgnoreCase)
            ? "I"
            : "A");

        yield return normalized;
        yield return normalized.TrimStart('/');
        yield return "/" + normalized;

        if (normalized.Length > 2)
        {
            yield return normalized[2..];
        }
    }

    private static ZimArchiveMetadata ParseMetadata(string output)
    {
        var rawFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(output))
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                separatorIndex = line.IndexOf('=');
            }

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                rawFields[key] = value;
            }
        }

        return new ZimArchiveMetadata
        {
            Title = FindFirstValue(rawFields, "title", "name", "book name"),
            Language = FindFirstValue(rawFields, "language", "lang"),
            Publisher = FindFirstValue(rawFields, "publisher", "creator", "author"),
            ArchiveDate = FindFirstValue(rawFields, "date", "creation date", "build date"),
            ArticleCount = FindIntValue(rawFields, "article count", "articles", "entry count", "count-entries"),
            RawMetadata = rawFields
        };
    }

    private static IReadOnlyList<ZimArticleDescriptor> ParseArticleListing(string output)
    {
        var modernResults = ParseModernArticleListing(output);
        if (modernResults.Count > 0)
        {
            return modernResults;
        }

        var results = new List<ZimArticleDescriptor>();

        foreach (var line in SplitLines(output))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.Trim();
            if (trimmed.StartsWith("Index", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Namespace", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("---", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("File", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parsed = TryParseArticleDescriptor(trimmed);
            if (parsed is not null)
            {
                results.Add(parsed);
            }
        }

        return results
            .Where(static article => !string.IsNullOrWhiteSpace(article.Url))
            .GroupBy(static article => article.Url, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private static IReadOnlyList<ZimArticleDescriptor> ParseModernArticleListing(string output)
    {
        var results = new List<ZimArticleDescriptor>();
        string? currentPath = null;
        string? currentTitle = null;
        string? currentType = null;
        string? currentMimeType = null;
        int? currentIndex = null;

        foreach (var line in SplitLines(output))
        {
            if (line.StartsWith("path:", StringComparison.OrdinalIgnoreCase))
            {
                FlushCurrentModernEntry(results, currentPath, currentTitle, currentType, currentMimeType, currentIndex);
                currentPath = line[5..].Trim();
                currentTitle = null;
                currentType = null;
                currentMimeType = null;
                currentIndex = null;
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentPath))
            {
                continue;
            }

            if (line.StartsWith("* title:", StringComparison.OrdinalIgnoreCase))
            {
                currentTitle = line[(line.IndexOf(':') + 1)..].Trim();
            }
            else if (line.StartsWith("* idx:", StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = TryParseTrailingInteger(line);
            }
            else if (line.StartsWith("* type:", StringComparison.OrdinalIgnoreCase))
            {
                currentType = line[(line.IndexOf(':') + 1)..].Trim();
            }
            else if (line.StartsWith("* mime-type:", StringComparison.OrdinalIgnoreCase))
            {
                currentMimeType = line[(line.IndexOf(':') + 1)..].Trim();
            }
        }

        FlushCurrentModernEntry(results, currentPath, currentTitle, currentType, currentMimeType, currentIndex);
        return results;
    }

    private static ZimArticleDescriptor? TryParseArticleDescriptor(string line)
    {
        if (line.Contains('|'))
        {
            var columns = line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var urlToken = columns.FirstOrDefault(LooksLikeUrlToken);
            if (!string.IsNullOrWhiteSpace(urlToken))
            {
                return CreateDescriptor(urlToken, columns.LastOrDefault() ?? urlToken, TryParseLeadingInteger(columns.FirstOrDefault()));
            }
        }

        if (line.Contains('\t'))
        {
            var columns = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var urlToken = columns.FirstOrDefault(LooksLikeUrlToken) ?? columns.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(urlToken))
            {
                return CreateDescriptor(urlToken, columns.LastOrDefault() ?? urlToken, TryParseLeadingInteger(columns.FirstOrDefault()));
            }
        }

        var doubleSpaceColumns = line.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (doubleSpaceColumns.Length > 1)
        {
            var urlToken = doubleSpaceColumns.FirstOrDefault(LooksLikeUrlToken) ?? doubleSpaceColumns[0];
            return CreateDescriptor(urlToken, doubleSpaceColumns.Last(), TryParseLeadingInteger(doubleSpaceColumns[0]));
        }

        var singleTokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (singleTokens.Length == 1)
        {
            return CreateDescriptor(singleTokens[0], singleTokens[0], null);
        }

        if (singleTokens.Length > 1)
        {
            var urlToken = singleTokens.FirstOrDefault(LooksLikeUrlToken) ?? singleTokens[0];
            var title = line[(line.IndexOf(urlToken, StringComparison.Ordinal) + urlToken.Length)..].Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                title = urlToken;
            }

            return CreateDescriptor(urlToken, title, TryParseLeadingInteger(singleTokens[0]));
        }

        return null;
    }

    private static ZimArticleDescriptor CreateDescriptor(string urlToken, string titleToken, int? index)
    {
        var normalizedUrl = LinkPathService.NormalizeUrl(urlToken, "A");
        var title = titleToken;
        if (string.Equals(title, urlToken, StringComparison.OrdinalIgnoreCase))
        {
            title = Path.GetFileName(normalizedUrl.Replace('/', Path.DirectorySeparatorChar));
        }

        return new ZimArticleDescriptor
        {
            Url = normalizedUrl,
            Title = title,
            Namespace = normalizedUrl[..1],
            Index = index
        };
    }

    private static bool LooksLikeUrlToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var value = token.Trim();
        return value.StartsWith("A/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/A/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("I/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/I/", StringComparison.OrdinalIgnoreCase)
            || (!value.Contains(' ') && value.Contains('/'));
    }

    private static int? TryParseLeadingInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : null;
    }

    private static int? TryParseTrailingInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : null;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? FindFirstValue(Dictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            var match = fields.FirstOrDefault(pair => pair.Key.Contains(key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Key))
            {
                return match.Value;
            }
        }

        return null;
    }

    private static int FindIntValue(Dictionary<string, string> fields, params string[] keys)
    {
        var candidate = FindFirstValue(fields, keys);
        return int.TryParse(candidate, out var parsed) ? parsed : 0;
    }

    private static async Task<string> ExecuteTextAsync(AppSettings settings, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var bytes = await ExecuteBytesAsync(settings, arguments, cancellationToken);
        return DecodeText(bytes);
    }

    private static async Task<string> ExecuteTextUsingStrategiesAsync(AppSettings settings, IReadOnlyList<string>[] argumentSets, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        foreach (var arguments in argumentSets)
        {
            try
            {
                FileTraceLogger.Info(nameof(ZimdumpClient), "ExecuteTextUsingStrategiesAsync ATTEMPT", new
                {
                    arguments = arguments.ToArray(),
                    zimdumpExecutablePath = FileTraceLogger.SummarizePath(settings.ZimdumpExecutablePath)
                });
                return await ExecuteTextAsync(settings, arguments, cancellationToken);
            }
            catch (Exception exception)
            {
                lastException = exception;
                FileTraceLogger.Warning(nameof(ZimdumpClient), "ExecuteTextUsingStrategiesAsync ATTEMPT FAILED", new
                {
                    arguments = arguments.ToArray(),
                    message = FileTraceLogger.SummarizeText(exception.Message, 320)
                });
            }
        }

        throw new InvalidOperationException("All zimdump command strategies failed.", lastException);
    }

    private static async Task<byte[]> ExecuteBytesAsync(AppSettings settings, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var scope = FileTraceLogger.Enter(nameof(ZimdumpClient), nameof(ExecuteBytesAsync), new
        {
            zimdumpExecutablePath = FileTraceLogger.SummarizePath(settings.ZimdumpExecutablePath),
            arguments = arguments.ToArray()
        });

        try
        {
            var executable = ResolveExecutable(settings);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Failed to start zimdump executable '{executable}'.", exception);
            }

            await using var outputStream = new MemoryStream();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.StandardOutput.BaseStream.CopyToAsync(outputStream, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var standardError = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"zimdump exited with code {process.ExitCode}: {standardError}".Trim());
            }

            var bytes = outputStream.ToArray();
            scope.Success(new
            {
                executable = FileTraceLogger.SummarizePath(executable),
                byteCount = bytes.Length
            });
            return bytes;
        }
        catch (Exception exception)
        {
            scope.Fail(exception);
            throw;
        }
    }

    private static string ResolveExecutable(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ZimdumpExecutablePath))
        {
            if (!File.Exists(settings.ZimdumpExecutablePath))
            {
                throw new FileNotFoundException("Configured zimdump executable was not found.", settings.ZimdumpExecutablePath);
            }

            return settings.ZimdumpExecutablePath;
        }

        return "zimdump";
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes[3..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }

        var utf8 = Encoding.UTF8.GetString(bytes);
        return utf8.Contains('\0') ? Encoding.Unicode.GetString(bytes) : utf8;
    }

    private static void FlushCurrentModernEntry(
        List<ZimArticleDescriptor> results,
        string? path,
        string? title,
        string? type,
        string? mimeType,
        int? index)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !string.Equals(type, "item", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(mimeType) && !mimeType.Contains("html", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        results.Add(CreateDescriptor(path, string.IsNullOrWhiteSpace(title) ? path : title, index));
    }

    private static bool LooksLikeHtml(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.TrimStart();
        return trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<body", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<IReadOnlyList<string>> BuildShowArguments(string zimPath, string candidate)
    {
        yield return ["show", $"--url={candidate}", zimPath];
        yield return ["-u", candidate, "-d", zimPath];
    }
}