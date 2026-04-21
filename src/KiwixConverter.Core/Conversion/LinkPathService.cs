using System.Security.Cryptography;
using System.Text;

namespace KiwixConverter.Core.Conversion;

public static class LinkPathService
{
    public static string BuildArchiveKey(string zimPath)
    {
        var baseName = Path.GetFileNameWithoutExtension(zimPath);
        return SanitizeSegment(baseName);
    }

    public static string NormalizeUrl(string rawUrl, string defaultNamespace = "A")
    {
        var value = rawUrl.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultNamespace + "/index";
        }

        value = value.Replace('\\', '/');
        var queryIndex = value.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
        {
            value = value[..queryIndex];
        }

        value = value.TrimStart('/');
        if (!value.Contains('/'))
        {
            value = defaultNamespace + "/" + value;
        }
        else if (value.Length >= 2 && value[1] == '/')
        {
            return value;
        }
        else if (value.StartsWith(defaultNamespace + "/", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }
        else if (value.StartsWith("A/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("I/", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }
        else
        {
            value = defaultNamespace + "/" + value;
        }

        return value;
    }

    public static bool TryNormalizeArticleUrl(string currentArticleUrl, string href, out string normalizedUrl, out string? fragment)
    {
        normalizedUrl = string.Empty;
        fragment = null;

        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var anchorIndex = href.IndexOf('#');
        if (anchorIndex >= 0)
        {
            fragment = href[(anchorIndex + 1)..];
            href = href[..anchorIndex];
        }

        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        if (href.StartsWith("/I/", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("I/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (href.StartsWith("/A/", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("A/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUrl = NormalizeUrl(href, "A");
            return true;
        }

        var baseUri = new Uri("https://zim.local/" + NormalizeUrl(currentArticleUrl));
        var resolvedUri = new Uri(baseUri, href);
        normalizedUrl = resolvedUri.AbsolutePath.TrimStart('/');
        normalizedUrl = NormalizeUrl(normalizedUrl, "A");
        return true;
    }

    public static bool TryNormalizeImageUrl(string currentArticleUrl, string src, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(src))
        {
            return false;
        }

        if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (src.StartsWith("/I/", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("I/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedUrl = NormalizeUrl(src, "I");
            return true;
        }

        var baseUri = new Uri("https://zim.local/" + NormalizeUrl(currentArticleUrl));
        var resolvedUri = new Uri(baseUri, src);
        normalizedUrl = NormalizeUrl(resolvedUri.AbsolutePath.TrimStart('/'), "I");
        return normalizedUrl.StartsWith("I/", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildArticleContentRelativePath(string articleUrl)
    {
        var normalized = NormalizeUrl(articleUrl);
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        if (parts.Count == 0)
        {
            parts.Add("index");
        }

        var sanitizedParts = parts.Select(SanitizeSegment).Where(static value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (sanitizedParts.Count == 0)
        {
            sanitizedParts.Add("index");
        }

        var lastIndex = sanitizedParts.Count - 1;
        sanitizedParts[lastIndex] = sanitizedParts[lastIndex] + "--" + ShortHash(normalized);
        sanitizedParts.Add("content.md");
        return Path.Combine(sanitizedParts.ToArray());
    }

    public static string BuildImageRelativePath(string articleUrl, string sourceUrl, int sequence)
    {
        var articleDirectory = Path.GetDirectoryName(BuildArticleContentRelativePath(articleUrl)) ?? string.Empty;
        var sourceFileName = Path.GetFileName(sourceUrl.Replace('/', Path.DirectorySeparatorChar));
        var extension = Path.GetExtension(sourceFileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 10)
        {
            extension = ".bin";
        }

        var baseName = Path.GetFileNameWithoutExtension(sourceFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "image";
        }

        var safeName = SanitizeSegment(baseName) + "-" + sequence.ToString("D3") + extension.ToLowerInvariant();
        return Path.Combine(articleDirectory, "images", safeName);
    }

    public static string BuildRelativeArticleLink(string currentArticleUrl, string targetArticleUrl, string? fragment = null)
    {
        var currentDirectory = Path.GetDirectoryName(BuildArticleContentRelativePath(currentArticleUrl)) ?? string.Empty;
        var targetContentPath = BuildArticleContentRelativePath(targetArticleUrl);
        var relative = Path.GetRelativePath(currentDirectory, targetContentPath).Replace('\\', '/');
        return string.IsNullOrWhiteSpace(fragment) ? relative : relative + "#" + fragment;
    }

    private static string SanitizeSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(invalidCharacters.Contains(character) || character == ':' ? '_' : character);
        }

        var sanitized = builder.ToString().Trim(' ', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? "item" : sanitized;
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes[..4]).ToLowerInvariant();
    }
}