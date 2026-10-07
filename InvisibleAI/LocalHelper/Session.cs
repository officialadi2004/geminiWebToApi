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
                Fields("enabled", "clipboardEnabled", "screenshotEnabled", "networkEnabled", "responseSeconds", "answerMode", "programmingLanguage", "responseOpacity");
                await configuration.WaitAsync(ct);
                try {
                    var value = store.Load();
                    if (p is not null) {
                        if (p.Value.TryGetProperty("enabled", out var enabled)) value.Enabled = enabled.GetBoolean();
                        if (p.Value.TryGetProperty("clipboardEnabled", out var clipboard)) value.ClipboardEnabled = clipboard.GetBoolean();
                        if (p.Value.TryGetProperty("networkEnabled", out var network)) value.NetworkEnabled = network.GetBoolean();
                        if (p.Value.TryGetProperty("screenshotEnabled", out var screenshot)) value.ScreenshotEnabled = screenshot.GetBoolean();
                        if (p.Value.TryGetProperty("responseSeconds", out var duration)) value.ResponseSeconds = duration.GetInt32();
                        Preferences(value);
                    }
                    store.Save(value); return Message.Create("STATUS", message.Id, Public(value));
                } finally { configuration.Release(); }
            case "CONNECT":
                Fields("provider", "credential", "model", "responseSeconds", "answerMode", "programmingLanguage", "responseOpacity");
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
                    Preferences(s);
                    s.Validate(); store.Save(s);
                    return Message.Create("CONNECTED", message.Id, new { settings = Public(s), models, connected = true });
                }
                finally { configuration.Release(); }
            case "TEXT_INPUT":
                Fields("text");
                var settings = store.Load();
                var result = await ai.AskTextAsync(Text("text"), settings, ct);
                return Complete(result, settings);
            case "SCREENSHOT_INPUT":
                Fields("imageBase64");
                var imageSettings = store.Load();
                if (!imageSettings.Enabled || !imageSettings.NetworkEnabled || !imageSettings.ScreenshotEnabled) throw new AIProviderException("Image processing is disabled in extension settings.");
                string encoded = Text("imageBase64");
                if (encoded.Length is 0 or > 6990508) throw new AIProviderException("Could not read the selected region.");
                byte[] image;
                try { image = Convert.FromBase64String(encoded); }
                catch (FormatException) { throw new AIProviderException("Could not read the selected region."); }
                try
                {
                    ImageInput.Validate(image);
                    return Complete(await ai.AskImageAsync(image, imageSettings, ct), imageSettings);
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(image); }
            case "PROVIDER_API":
                Fields("method", "path");
                return Message.Create("PROVIDER_RESULT", message.Id, await new ProviderApi(ai).InvokeAsync(Text("method"), Text("path"), store.Load(), ct));
            default: throw new AIProviderException("Unknown request.");
        }
        Message Complete(AIAnswer answer, AppSettings settings) => Message.Create("PROCESSING_COMPLETE", message.Id, new { result = answer.ToUnifiedResponse(), responseSeconds = settings.ResponseSeconds, responseOpacity = settings.ResponseOpacity });
        void Preferences(AppSettings value)
        {
            if (p is null) return;
            if (p.Value.TryGetProperty("answerMode", out var mode)) value.ResponseMode = mode.GetString() switch { "Quick" => ResponseMode.CONCISE, "Detailed" => ResponseMode.DETAILED, _ => throw new AIProviderException("Choose Quick or Detailed.") };
            if (p.Value.TryGetProperty("programmingLanguage", out var language)) value.ProgrammingLanguage = language.GetString() ?? "";
            if (p.Value.TryGetProperty("responseOpacity", out var opacity)) value.ResponseOpacity = opacity.GetDouble();
        }
    }
    private object Public(AppSettings s) => new { provider = s.Provider, model = s.Model, responseSeconds = s.ResponseSeconds, enabled = s.Enabled, clipboardEnabled = s.ClipboardEnabled, networkEnabled = s.NetworkEnabled,
        screenshotEnabled = s.ScreenshotEnabled, answerMode = s.ResponseMode == ResponseMode.DETAILED ? "Detailed" : "Quick", programmingLanguage = s.ProgrammingLanguage, responseOpacity = s.ResponseOpacity,
        credentialSaved = s.Provider.Length != 0 && credentials.For(s.Provider).Read() is not null };
}
