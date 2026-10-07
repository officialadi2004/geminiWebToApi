using System.Text.Json;
using InvisibleAI.Helper.AI;
using InvisibleAI.Helper.Settings;
using InvisibleAI.Shared.Protocol;
using InvisibleAI.Helper.Display;

namespace InvisibleAI.Helper;

// Only the registered extension can start this stdio service. Credentials are write-only
// across this boundary; Windows Credential Manager supplies the current user's identity.
public sealed class Session(AIService ai, SettingsStore store, IProviderCredentials credentials, IPrivateResponseDisplay? display = null)
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
            case "HIDE_RESPONSE":
                Fields("targetId");
                if (display is not null) await display.HideAsync(Text("targetId") is { Length: > 0 } target ? target : null);
                return Message.Create("STATUS", message.Id, new { hidden = true });
            case "SETTINGS_UPDATE":
                Fields("enabled", "clipboardEnabled", "screenshotEnabled", "networkEnabled", "responseSeconds", "answerMode", "programmingLanguage", "responseOpacity", "privateResponses");
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
                    store.Save(value);
                    if (display is not null) await display.HideAsync();
                    return Message.Create("STATUS", message.Id, Public(value));
                } finally { configuration.Release(); }
            case "CONNECT":
                Fields("provider", "credential", "model", "responseSeconds", "answerMode", "programmingLanguage", "responseOpacity", "privateResponses");
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
                    if (display is not null) await display.HideAsync();
                    return Message.Create("CONNECTED", message.Id, new { settings = Public(s), models, connected = true });
                }
                finally { configuration.Release(); }
            case "TEXT_INPUT":
                Fields("text", "privateResponses");
                var settings = store.Load();
                RequirePrivate(settings);
                if (!settings.Enabled || !settings.NetworkEnabled || !settings.ClipboardEnabled) throw new AIProviderException("Text processing is disabled in extension settings.");
                return await Generate(settings, () => ai.AskTextAsync(Text("text"), settings, ct));
            case "SCREENSHOT_INPUT":
                Fields("imageBase64", "privateResponses");
                var imageSettings = store.Load();
                RequirePrivate(imageSettings);
                if (!imageSettings.Enabled || !imageSettings.NetworkEnabled || !imageSettings.ScreenshotEnabled) throw new AIProviderException("Image processing is disabled in extension settings.");
                string encoded = Text("imageBase64");
                if (encoded.Length is 0 or > 6990508) throw new AIProviderException("Could not read the selected region.");
                byte[] image;
                try { image = Convert.FromBase64String(encoded); }
                catch (FormatException) { throw new AIProviderException("Could not read the selected region."); }
                try
                {
                    ImageInput.Validate(image);
                    return await Generate(imageSettings, () => ai.AskImageAsync(image, imageSettings, ct));
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(image); }
            case "PROVIDER_API":
                Fields("method", "path");
                return Message.Create("PROVIDER_RESULT", message.Id, await new ProviderApi(ai).InvokeAsync(Text("method"), Text("path"), store.Load(), ct));
            default: throw new AIProviderException("Unknown request.");
        }
        Message Complete(AIAnswer answer, AppSettings settings) => Message.Create("PROCESSING_COMPLETE", message.Id, new { result = answer.ToUnifiedResponse(), responseSeconds = settings.ResponseSeconds, responseOpacity = settings.ResponseOpacity });
        async Task<Message> Generate(AppSettings settings, Func<Task<AIAnswer>> generate)
        {
            if (!settings.PrivateResponses) return Complete(await generate(), settings);
            if (display is null) throw new AIProviderException("Update Invisible AI Setup to use private responses.");
            await display.BeginAsync(message.Id, settings, ct);
            try
            {
                var answer = await generate(); ct.ThrowIfCancellationRequested();
                await display.AnswerAsync(message.Id, answer, ct);
                // Private answers, including explanations/code, never cross into browser DOM.
                return Message.Create("PROCESSING_COMPLETE", message.Id, new { privateResponses = true, displayed = true });
            }
            catch (OperationCanceledException) { await display.HideAsync(message.Id); throw; }
            catch (Exception error)
            {
                if (ct.IsCancellationRequested) await display.HideAsync(message.Id);
                else await display.AnswerAsync(message.Id, new(error is AIProviderException safe ? safe.Message : "Could not connect to AI provider.", []), ct);
                // Errors are private too; the extension never draws them on the shared page.
                throw;
            }
        }
        void RequirePrivate(AppSettings value)
        {
            // A concurrent settings change must never downgrade an already-private request.
            if (p is not null && p.Value.TryGetProperty("privateResponses", out var flag) && flag.GetBoolean()) value.PrivateResponses = true;
        }
        void Preferences(AppSettings value)
        {
            if (p is null) return;
            if (p.Value.TryGetProperty("answerMode", out var mode)) value.ResponseMode = mode.GetString() switch { "Quick" => ResponseMode.CONCISE, "Detailed" => ResponseMode.DETAILED, _ => throw new AIProviderException("Choose Quick or Detailed.") };
            if (p.Value.TryGetProperty("programmingLanguage", out var language)) value.ProgrammingLanguage = language.GetString() ?? "";
            if (p.Value.TryGetProperty("responseOpacity", out var opacity)) value.ResponseOpacity = opacity.GetDouble();
            if (p.Value.TryGetProperty("privateResponses", out var privateResponses)) value.PrivateResponses = privateResponses.GetBoolean();
        }
    }
    private object Public(AppSettings s) => new { provider = s.Provider, model = s.Model, responseSeconds = s.ResponseSeconds, enabled = s.Enabled, clipboardEnabled = s.ClipboardEnabled, networkEnabled = s.NetworkEnabled,
        screenshotEnabled = s.ScreenshotEnabled, answerMode = s.ResponseMode == ResponseMode.DETAILED ? "Detailed" : "Quick", programmingLanguage = s.ProgrammingLanguage, responseOpacity = s.ResponseOpacity, privateResponses = s.PrivateResponses,
        credentialSaved = s.Provider.Length != 0 && credentials.For(s.Provider).Read() is not null };
}
