using InvisibleAI.Helper.AI;

namespace InvisibleAI.Helper.Settings;

public enum ResponseMode { SHORT, CONCISE, DETAILED }
public sealed class AppSettings
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string GeminiModel { get; set; } = "";
    public string GroqModel { get; set; } = "";
    public int ResponseSeconds { get; set; } = 12;
    // Provider request controls remain internal. The browser UI needs only provider, model and duration.
    public ResponseMode ResponseMode { get; set; } = ResponseMode.CONCISE;
    public int MaxResponseLength { get; set; } = 600;
    public int MaxOutputTokens { get; set; } = 1024;
    public int RequestTimeoutSeconds { get; set; } = 90;
    public bool Enabled { get; set; } = true;
    public bool ClipboardEnabled { get; set; } = true;
    public bool ScreenshotEnabled { get; set; } = false;
    public bool NetworkEnabled { get; set; } = true;
    public bool WebSearch { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string GeminiPythonPath { get; set; } = "";
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
    public void Validate()
    {
        if (Provider is null || (Provider.Length > 0 && !Providers.IsValid(Provider))) throw new ArgumentException("Choose Gemini Web or Groq.");
        foreach (string? model in new[] { Model, GeminiModel, GroqModel })
            if (model is null || (model.Length > 0 && !Providers.ValidModelId(model))) throw new ArgumentException("Choose an available model.");
        if (ResponseSeconds is < 2 or > 60 || MaxResponseLength is < 16 or > 12000 || MaxOutputTokens is < 128 or > 8192 || RequestTimeoutSeconds is < 10 or > 300 || !Enum.IsDefined(ResponseMode))
            throw new ArgumentException("Invalid assistant settings.");
    }
}
