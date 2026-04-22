using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KiwixConverter.Core.Infrastructure;
using KiwixConverter.Core.Models;

namespace KiwixConverter.Core.Services;

public sealed class WeKnoraClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    public async Task<IReadOnlyList<WeKnoraKnowledgeBaseInfo>> ListKnowledgeBasesAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, settings, "/knowledge-bases");
        using var document = await SendForDocumentAsync(request, cancellationToken);
        var data = GetDataElement(document.RootElement);
        if (data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<WeKnoraKnowledgeBaseInfo>();
        foreach (var item in data.EnumerateArray())
        {
            results.Add(ReadKnowledgeBase(item));
        }

        return results;
    }

    public async Task<WeKnoraKnowledgeBaseInfo> CreateKnowledgeBaseAsync(
        AppSettings settings,
        string name,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            name,
            description = string.IsNullOrWhiteSpace(description) ? "Created by Kiwix Converter." : description,
            type = "document",
            is_temporary = false,
            storage_provider_config = new
            {
                provider = "local"
            },
            chunking_config = new
            {
                chunk_size = 1000,
                chunk_overlap = 200,
                separators = new[] { "\n\n", "\n", "。", "！", "？", ".", "!", "?", ";", "；" },
                enable_multimodal = !string.IsNullOrWhiteSpace(settings.WeKnoraMultimodalModelId),
                enable_parent_child = false
            }
        };

        using var request = CreateJsonRequest(HttpMethod.Post, settings, "/knowledge-bases", payload);
        using var document = await SendForDocumentAsync(request, cancellationToken);
        return ReadKnowledgeBase(GetDataElement(document.RootElement));
    }

    public async Task<WeKnoraKnowledgeItemInfo> CreateManualKnowledgeAsync(
        AppSettings settings,
        string knowledgeBaseId,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            title,
            content,
            status = "publish"
        };

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            settings,
            $"/knowledge-bases/{Uri.EscapeDataString(knowledgeBaseId)}/knowledge/manual",
            payload);
        using var document = await SendForDocumentAsync(request, cancellationToken);
        return ReadKnowledgeItem(GetDataElement(document.RootElement));
    }

    public async Task<WeKnoraKnowledgeItemInfo> UpdateManualKnowledgeAsync(
        AppSettings settings,
        string knowledgeId,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            title,
            content,
            status = "publish"
        };

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            settings,
            $"/knowledge/manual/{Uri.EscapeDataString(knowledgeId)}",
            payload);
        using var document = await SendForDocumentAsync(request, cancellationToken);
        return ReadKnowledgeItem(GetDataElement(document.RootElement));
    }

    public async Task<WeKnoraKnowledgeItemInfo> GetKnowledgeAsync(
        AppSettings settings,
        string knowledgeId,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, settings, $"/knowledge/{Uri.EscapeDataString(knowledgeId)}");
        using var document = await SendForDocumentAsync(request, cancellationToken);
        return ReadKnowledgeItem(GetDataElement(document.RootElement));
    }

    public async Task UpdateKnowledgeBaseInitializationAsync(
        AppSettings settings,
        string knowledgeBaseId,
        string? chatModelId,
        string? multimodalModelId,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(chatModelId))
        {
            payload["chat_model_id"] = chatModelId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(multimodalModelId))
        {
            payload["multimodal_id"] = multimodalModelId.Trim();
        }

        if (payload.Count == 0)
        {
            return;
        }

        using var request = CreateJsonRequest(
            HttpMethod.Put,
            settings,
            $"/initialization/config/{Uri.EscapeDataString(knowledgeBaseId)}",
            payload);
        using var _ = await SendForDocumentAsync(request, cancellationToken);
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, AppSettings settings, string relativePath, object payload)
    {
        var request = CreateRequest(method, settings, relativePath);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonDefaults.Options), Encoding.UTF8, "application/json");
        return request;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, AppSettings settings, string relativePath)
    {
        var request = new HttpRequestMessage(method, BuildApiUri(settings.WeKnoraBaseUrl, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-Request-ID", Guid.NewGuid().ToString("D"));
        ApplyAuthentication(request, settings);
        return request;
    }

    private static Uri BuildApiUri(string? baseUrl, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Configure the WeKnora base URL before using sync features.");
        }

        var normalized = baseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("The configured WeKnora base URL is not a valid absolute URL.");
        }

        if (normalized.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalized += "/";
        }
        else if (normalized.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            normalized += "/v1/";
        }
        else if (!normalized.Contains("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalized += "/api/v1/";
        }
        else if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }

        return new Uri(normalized + relativePath.TrimStart('/'));
    }

    private static void ApplyAuthentication(HttpRequestMessage request, AppSettings settings)
    {
        var accessToken = settings.WeKnoraAccessToken?.Trim();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Configure the WeKnora access token before using sync features.");
        }

        if (settings.WeKnoraAuthMode == WeKnoraAuthMode.BearerToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return;
        }

        request.Headers.Add("X-API-Key", accessToken);
    }

    private static async Task<JsonDocument> SendForDocumentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildApiError(response.StatusCode, payload));
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("WeKnora returned an empty response.");
        }

        return JsonDocument.Parse(payload);
    }

    private static string BuildApiError(HttpStatusCode statusCode, string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var errorElement = GetNestedElement(root, "error");
            var error = errorElement.HasValue && errorElement.Value.ValueKind == JsonValueKind.Object
                ? ReadString(errorElement.Value, "message") ?? errorElement.Value.ToString()
                : ReadString(root, "error") ?? ReadString(root, "message") ?? ReadString(root, "msg");
            var code = errorElement.HasValue && errorElement.Value.ValueKind == JsonValueKind.Object
                ? ReadString(errorElement.Value, "code")
                : ReadString(root, "code");
            var details = errorElement.HasValue && errorElement.Value.ValueKind == JsonValueKind.Object
                ? ReadString(errorElement.Value, "details")
                : ReadString(root, "details");

            if (!string.IsNullOrWhiteSpace(error) && !string.IsNullOrWhiteSpace(code))
            {
                if (!string.IsNullOrWhiteSpace(details))
                {
                    return $"WeKnora request failed ({(int)statusCode} {statusCode}): {code} - {error} ({details})";
                }

                return $"WeKnora request failed ({(int)statusCode} {statusCode}): {code} - {error}";
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                return $"WeKnora request failed ({(int)statusCode} {statusCode}): {error}";
            }
        }
        catch (JsonException)
        {
        }

        return $"WeKnora request failed ({(int)statusCode} {statusCode}).";
    }

    private static JsonElement GetDataElement(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data)
            ? data
            : root;
    }

    private static JsonElement? GetNestedElement(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property;
    }

    private static WeKnoraKnowledgeBaseInfo ReadKnowledgeBase(JsonElement element)
    {
        var storageProvider = default(string);
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("storage_provider_config", out var storageProviderConfig)
            && storageProviderConfig.ValueKind == JsonValueKind.Object)
        {
            storageProvider = ReadString(storageProviderConfig, "provider");
        }

        return new WeKnoraKnowledgeBaseInfo
        {
            Id = ReadString(element, "id") ?? string.Empty,
            Name = ReadString(element, "name") ?? string.Empty,
            Description = ReadString(element, "description"),
            Type = ReadString(element, "type") ?? "document",
            IsTemporary = ReadBoolean(element, "is_temporary"),
            StorageProvider = storageProvider
        };
    }

    private static WeKnoraKnowledgeItemInfo ReadKnowledgeItem(JsonElement element)
    {
        return new WeKnoraKnowledgeItemInfo
        {
            Id = ReadString(element, "id") ?? string.Empty,
            KnowledgeBaseId = ReadString(element, "knowledge_base_id") ?? string.Empty,
            Type = ReadString(element, "type") ?? string.Empty,
            Title = ReadString(element, "title"),
            ParseStatus = ReadString(element, "parse_status"),
            EnableStatus = ReadString(element, "enable_status"),
            ErrorMessage = ReadString(element, "error_message")
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : property.ToString();
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        var raw = property.ToString();
        return bool.TryParse(raw, out var parsed) && parsed;
    }
}