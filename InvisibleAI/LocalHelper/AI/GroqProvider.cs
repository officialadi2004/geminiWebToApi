using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InvisibleAI.Helper.Settings;

namespace InvisibleAI.Helper.AI;

public sealed class GroqProvider(HttpClient http, IProviderCredentials credentials) : IAIProvider
{
    private const string Root = "https://api.groq.com/openai/v1/";
    public string Name => Providers.Groq;
    private string Key()
    {
        string? key = credentials.For(Name).Read();
        if (string.IsNullOrWhiteSpace(key)) throw new AIProviderException("Missing Groq API key. Add your key in extension settings.");
        if (key.Length > 2000 || key.Any(c => c <= 32 || c >= 127)) throw new AIProviderException("Invalid Groq API key. Please check your key and try again.");
        return key;
    }
    public async Task<IReadOnlyList<AIModel>> GetModelsAsync(AppSettings settings, CancellationToken ct)
    {
        string key = Key();
        using var request = new HttpRequestMessage(HttpMethod.Get, Root + "models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await SendAsync(request, settings, ct);
        if (!response.RootElement.TryGetProperty("data", out var data)) throw new AIProviderException("Groq returned no model catalog.");
        var models = new List<AIModel>();
        foreach (var item in data.EnumerateArray())
        {
            string? id = item.TryGetProperty("id", out var value) ? value.GetString() : null;
            if (!Providers.ValidModelId(id) || id!.Contains(key, StringComparison.Ordinal)) continue;
            if (item.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.False) continue;
            // Audio-only and retired systems cannot generate this application's chat responses.
            if (id.StartsWith("whisper-", StringComparison.Ordinal) || id.Contains("orpheus", StringComparison.Ordinal) ||
                id.StartsWith("playai-", StringComparison.Ordinal) || id is "groq/compound" or "groq/compound-mini") continue;
            // Availability comes from /models. Vision/search capability is documented separately by Groq.
            bool vision = id is "qwen/qwen3.6-27b" or "qwen/qwen3.8-27b";
            if (item.TryGetProperty("supports_vision", out var capability)) vision = capability.ValueKind == JsonValueKind.True;
            bool search = id is "openai/gpt-oss-20b" or "openai/gpt-oss-120b" or "openai/gpt-oss-safeguard-20b";
            models.Add(new(id, id + (vision ? " · Images" : "") + (search ? " · Search" : ""), vision, search));
        }
        return models;
    }
    public async Task<AIAnswer> GenerateAsync(string? text, byte[]? image, AppSettings settings, CancellationToken ct)
    {
        string key = Key();
        var catalog = await GetModelsAsync(settings, ct);
        var selected = catalog.FirstOrDefault(m => m.Id == settings.Model) ?? throw new AIProviderException("Groq model unavailable. Refresh models and choose another model.");
        if (image is not null && !selected.SupportsImages) throw new AIProviderException("This Groq model does not support images. Choose a vision model in extension settings.");
        if (settings.WebSearch && !selected.SupportsSearch) throw new AIProviderException("This Groq model does not support web search. Choose a model labeled Search or turn off web search.");
        var content = new List<object> { new { type = "text", text = text ?? "Analyze the selected screenshot. Answer its question or briefly describe it." } };
        if (image is not null) content.Add(new { type = "image_url", image_url = new { url = "data:image/png;base64," + Convert.ToBase64String(image) } });
        var body = new Dictionary<string, object>
        {
            ["model"] = selected.Id,
            ["messages"] = new object[] { new { role = "system", content = AIInstructions.Build(settings) }, new { role = "user", content } },
            ["max_completion_tokens"] = settings.MaxOutputTokens,
            ["stream"] = false
        };
        if (settings.WebSearch)
        {
            body["tools"] = new[] { new { type = "browser_search" } };
            body["tool_choice"] = "auto";
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, Root + "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key); request.Content = JsonContent.Create(body);
        using var json = await SendAsync(request, settings, ct);
        return ParseAnswer(json.RootElement, selected.Id, settings.MaxResponseLength, key);
    }
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, AppSettings settings, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new AIProviderException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Invalid Groq API key. Please check your key and try again.",
                HttpStatusCode.TooManyRequests => "Groq rate limit reached. Wait before trying again.",
                HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "Groq model unavailable or unsupported request. Refresh models and check the model's capabilities.",
                _ => "Groq is unavailable. Try again later."
            });
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            // Bound transport allocation; raw upstream responses/errors are never logged or returned.
            using var memory = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            { if (memory.Length + count > 2 * 1024 * 1024) throw new AIProviderException("Groq response exceeded the size limit."); memory.Write(buffer, 0, count); }
            return JsonDocument.Parse(memory.ToArray());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AIProviderException("Groq request timed out. Try again."); }
        catch (HttpRequestException) { throw new AIProviderException("Could not reach Groq. Check your network connection."); }
        catch (JsonException) { throw new AIProviderException("Groq returned an invalid response. Try again later."); }
    }
    public static AIAnswer ParseAnswer(JsonElement root, string model, int max, string key)
    {
        try
        {
            var choice = root.GetProperty("choices")[0];
            string text = choice.GetProperty("message").GetProperty("content").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(text)) throw new AIProviderException("Groq returned no answer. Increase the token budget or try another model.");
            string finish = choice.TryGetProperty("finish_reason", out var reason) ? reason.GetString() ?? "unknown" : "unknown";
            if (finish is not ("stop" or "length" or "content_filter" or "tool_calls")) finish = "unknown";
            var sources = new List<AISource>();
            if (root.TryGetProperty("citations", out var citations) && citations.ValueKind == JsonValueKind.Array)
                foreach (var item in citations.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is string url && url.Length < 2048 && !url.Contains(key, StringComparison.Ordinal) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https") sources.Add(new(uri.Host, uri.AbsoluteUri));
            AIUsage? usage = null;
            if (root.TryGetProperty("usage", out var value))
            {
                static int? Token(JsonElement item, string name) => item.TryGetProperty(name, out var v) && v.TryGetInt32(out int n) && n >= 0 ? n : null;
                usage = new(Token(value, "prompt_tokens"), Token(value, "completion_tokens"), Token(value, "total_tokens"));
            }
            return new(AIInstructions.Limit(CredentialRedaction.Scrub(text.Trim(), key), max), sources.Take(10).ToArray(), "groq", model, usage, finish);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        { if (e is AIProviderException) throw; throw new AIProviderException("Groq returned an invalid response. Try again later."); }
    }
}
