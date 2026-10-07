using System.Text.Json;
using InvisibleAI.Helper.AI;
using InvisibleAI.Helper.Settings;
using InvisibleAI.Shared.Protocol;

namespace InvisibleAI.Helper;

// Only the registered extension can start this stdio service. Credentials are write-only
// across this boundary; Windows Credential Manager supplies the current user's identity.
public sealed class Session(AIService ai, SettingsStore store, IProviderCredentials credentials)
{
    private readonly SemaphoreSlim configuration = new(1, 1);
    public async Task<Message> HandleAsync(Message message, CancellationToken ct)
    {
        message.Validate();
        var p = message.Payload;
        void Fields(params string[] allowed)
        {
            if (p is null) return;
            if (p.Value.ValueKind != JsonValueKind.Object || p.Value.EnumerateObject().Any(x => !allowed.Contains(x.Name)))
                throw new AIProviderException("Invalid request.");
        }
        string Text(string name) => p is not null && p.Value.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        switch (message.Type)
        {
            case "PING":
                Fields(); return Message.Create("STATUS", message.Id, Public(store.Load()));
            case "SETTINGS_UPDATE":
                Fields("enabled", "clipboardEnabled", "networkEnabled", "responseSeconds");
                await configuration.WaitAsync(ct);
                try {
                    var value = store.Load();
                    if (p is not null) {
                        if (p.Value.TryGetProperty("enabled", out var enabled)) value.Enabled = enabled.GetBoolean();
                        if (p.Value.TryGetProperty("clipboardEnabled", out var clipboard)) value.ClipboardEnabled = clipboard.GetBoolean();
                        if (p.Value.TryGetProperty("networkEnabled", out var network)) value.NetworkEnabled = network.GetBoolean();
                        if (p.Value.TryGetProperty("responseSeconds", out var duration)) value.ResponseSeconds = duration.GetInt32();
                    }
                    store.Save(value); return Message.Create("STATUS", message.Id, Public(value));
                } finally { configuration.Release(); }
            case "CONNECT":
                Fields("provider", "credential", "model", "responseSeconds");
                await configuration.WaitAsync(ct);
                try
                {
                    var s = store.Load(); string provider = Text("provider"); Providers.Id(provider);
                    string secret = Text("credential");
                    if (!string.IsNullOrEmpty(secret))
                    {
                        if (provider == Providers.Gemini) secret = GeminiCookies.Normalize(secret);
                        else if (secret.Length is < 8 or > 1000 || secret.Any(char.IsWhiteSpace) || secret.Any(char.IsControl))
                            throw new AIProviderException("Invalid Groq API key.");
                        credentials.For(provider).Write(secret);
                    }
                    s.Provider = provider;
                    var models = await ai.GetModelsAsync(s, ct);
                    if (models.Count == 0) throw new AIProviderException("No usable models available.");
                    string selected = Text("model");
                    if (selected.Length == 0) selected = provider == Providers.Groq ? s.GroqModel : s.GeminiModel;
                    if (!models.Any(x => x.Id == selected))
                    {
                        if (Text("model").Length != 0) throw new AIProviderException("Model unavailable. Choose an available model.");
                        selected = models[0].Id;
                    }
                    s.Model = selected;
                    if (provider == Providers.Groq) s.GroqModel = selected; else s.GeminiModel = selected;
                    if (p is not null && p.Value.TryGetProperty("responseSeconds", out var duration)) s.ResponseSeconds = duration.GetInt32();
                    s.Validate(); store.Save(s);
                    return Message.Create("CONNECTED", message.Id, new { settings = Public(s), models, connected = true });
                }
                finally { configuration.Release(); }
            case "TEXT_INPUT":
                Fields("text");
                var settings = store.Load();
                var result = await ai.AskTextAsync(Text("text"), settings, ct);
                return Message.Create("PROCESSING_COMPLETE", message.Id, new { result = result.ToUnifiedResponse(), responseSeconds = settings.ResponseSeconds });
            case "PROVIDER_API":
                Fields("method", "path");
                return Message.Create("PROVIDER_RESULT", message.Id, await new ProviderApi(ai).InvokeAsync(Text("method"), Text("path"), store.Load(), ct));
            default: throw new AIProviderException("Unknown request.");
        }
    }
    private object Public(AppSettings s) => new { provider = s.Provider, model = s.Model, responseSeconds = s.ResponseSeconds, enabled = s.Enabled, clipboardEnabled = s.ClipboardEnabled, networkEnabled = s.NetworkEnabled,
        credentialSaved = s.Provider.Length != 0 && credentials.For(s.Provider).Read() is not null };
}
