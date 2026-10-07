using InvisibleAI.Helper.Settings;

namespace InvisibleAI.Helper.AI;

public sealed class AIService : IAIAgent
{
    private readonly Dictionary<string, IAIProvider> providers;
    public AIService(params IAIProvider[] adapters)
    {
        if (adapters.Length != 2 || adapters.Select(p => p.Name).Order().SequenceEqual(new[] { Providers.Gemini, Providers.Groq }.Order()) == false)
            throw new ArgumentException("Exactly Gemini Web and Groq adapters are required.");
        providers = adapters.ToDictionary(p => p.Name);
    }
    public Task<AIAnswer> AskTextAsync(string text, AppSettings settings, CancellationToken ct) => AskMultimodalAsync(text, null, settings, ct);
    public Task<AIAnswer> AskImageAsync(byte[] image, AppSettings settings, CancellationToken ct) => AskMultimodalAsync(null, image, settings, ct);
    public async Task<AIAnswer> AskMultimodalAsync(string? text, byte[]? image, AppSettings settings, CancellationToken ct)
    {
        settings.Validate(); CheckNetwork(settings);
        if (!settings.Enabled) throw new AIProviderException("Assistant is disabled.");
        if (!string.IsNullOrEmpty(text) && !settings.ClipboardEnabled) throw new AIProviderException("Text processing is disabled.");
        if (image is not null && !settings.ScreenshotEnabled) throw new AIProviderException("Screenshot processing is disabled.");
        if (string.IsNullOrWhiteSpace(text) && image is null) throw new AIProviderException("Supply text or an image.");
        if (text?.Length > 50000 || image?.Length > 5 * 1024 * 1024) throw new AIProviderException("Input exceeds the allowed size.");
        if (!Providers.ValidModelId(settings.Model)) throw new AIProviderException("Open extension settings and click Connect / Save.");
        ct.ThrowIfCancellationRequested();
        var answer = await Adapter(settings.Provider).GenerateAsync(text, image, settings.Clone(), ct);
        return AnswerPolicy.Process(text, answer, settings, image is not null);
    }
    public async Task<IReadOnlyList<AIModel>> GetModelsAsync(AppSettings settings, CancellationToken ct)
    { CheckNetwork(settings); return await Adapter(settings.Provider).GetModelsAsync(settings.Clone(), ct); }
    public async Task<ConnectionResult> TestConnectionAsync(AppSettings settings, CancellationToken ct)
    {
        string id = Providers.Id(settings.Provider);
        try
        {
            var models = await GetModelsAsync(settings, ct);
            if (models.Count == 0) throw new AIProviderException("No usable models are available for this account.");
            return new(true, id);
        }
        catch (AIProviderException e) { return new(false, id, e.Message); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, id, "Provider connection timed out."); }
        catch (Exception) when (!ct.IsCancellationRequested) { return new(false, id, "Provider unavailable. Check your connection and try again."); }
    }
    private IAIProvider Adapter(string name) => providers.TryGetValue(name, out var value) ? value : throw new AIProviderException("Choose Gemini Web or Groq in extension settings.");
    private static void CheckNetwork(AppSettings settings)
    { if (!settings.NetworkEnabled) throw new AIProviderException("Network requests are disabled in extension settings (Advanced)."); }
}
