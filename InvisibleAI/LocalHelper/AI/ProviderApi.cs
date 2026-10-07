namespace InvisibleAI.Helper.AI;

// Local API routes use the existing current-user-only native pipe, never a public HTTP listener.
public sealed class ProviderApi(AIService service)
{
    public async Task<object> InvokeAsync(string method, string path, Settings.AppSettings settings, CancellationToken ct)
    {
        if (method != "POST") throw new AIProviderException("Provider operations require an explicit POST action.");
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "api" || parts[1] != "ai" || parts[2] != "providers" || parts[3] is not ("gemini" or "groq"))
            throw new AIProviderException("Unknown provider API route.");
        var scoped = settings.Clone(); scoped.Provider = Providers.Name(parts[3]);
        scoped.Model = scoped.Provider == Providers.Gemini ? settings.GeminiModel : settings.GroqModel;
        return parts[4] switch
        {
            "test" => await service.TestConnectionAsync(scoped, ct),
            "models" => new { provider = parts[3], models = await service.GetModelsAsync(scoped, ct) },
            _ => throw new AIProviderException("Unknown provider API route.")
        };
    }
}
