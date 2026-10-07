using InvisibleAI.Companion.Hotkeys;

namespace InvisibleAI.Companion.Settings;

public enum ResponseMode { SHORT, CONCISE, DETAILED }
public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool TrayMode { get; set; } = true;
    public double DotSize { get; set; } = 6;
    public double DotOpacity { get; set; } = 0.8;
    public int PulseMilliseconds { get; set; } = 900;
    public double ResponseOpacity { get; set; } = 0.94;
    public double ResponseFontSize { get; set; } = 12;
    public double ResponseWidth { get; set; } = 280;
    public int ResponseSeconds { get; set; } = 12;
    public int OffsetX { get; set; } = 16;
    public int OffsetY { get; set; } = 16;
    public string Monitor { get; set; } = "Active";
    public bool InteractiveResponse { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public string GeminiModel { get; set; } = "";
    public string GroqModel { get; set; } = "";
    public string GeminiPythonPath { get; set; } = "";
    public ResponseMode ResponseMode { get; set; } = ResponseMode.CONCISE;
    public bool WebSearch { get; set; }
    public int MaxResponseLength { get; set; } = 600;
    public int MaxOutputTokens { get; set; } = 1024;
    public int RequestTimeoutSeconds { get; set; } = 90;
    public string ClipboardShortcut { get; set; } = "Ctrl+Shift+V";
    public string ScreenShortcut { get; set; } = "Ctrl+Shift+S";
    public string HideShortcut { get; set; } = "Ctrl+Shift+H";
    public string ToggleShortcut { get; set; } = "Ctrl+Shift+A";
    public string ExpandShortcut { get; set; } = "Ctrl+Shift+E";
    public bool ClipboardEnabled { get; set; } = true;
    public bool ScreenshotEnabled { get; set; } = true;
    public bool NetworkEnabled { get; set; } = true;
    // No disk capture path exists: screenshots are always ephemeral, even on failure/cancellation.
    public bool ClearTemporaryScreenshots { get; set; } = true;
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
    [System.Text.Json.Serialization.JsonIgnore]
    public string[] Shortcuts => [ClipboardShortcut, ScreenShortcut, HideShortcut, ToggleShortcut, ExpandShortcut];
    public void Validate()
    {
        static void Range(double value, double min, double max, string name)
        { if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentException($"{name}: choose {min}–{max}."); }
        Range(DotSize, 3, 16, "Dot size"); Range(DotOpacity, 0.1, 1, "Dot opacity");
        Range(PulseMilliseconds, 300, 3000, "Pulse speed"); Range(ResponseOpacity, 0.2, 1, "Response opacity");
        Range(ResponseFontSize, 9, 24, "Font size"); Range(ResponseWidth, 100, 600, "Response width");
        Range(ResponseSeconds, 2, 120, "Response duration"); Range(OffsetX, 0, 2000, "X offset"); Range(OffsetY, 0, 2000, "Y offset");
        Range(MaxResponseLength, 16, 12000, "Maximum response length"); Range(MaxOutputTokens, 128, 8192, "Output token budget");
        Range(RequestTimeoutSeconds, 10, 300, "Request timeout");
        if (Provider.Length > 0 && !AI.Providers.IsValid(Provider)) throw new ArgumentException("Choose Gemini Web or Groq.");
        foreach (var model in new[] { Model, GeminiModel, GroqModel })
            if (model.Length > 0 && !AI.Providers.ValidModelId(model)) throw new ArgumentException("Choose a valid model ID.");
        if (GeminiPythonPath.Length > 1024) throw new ArgumentException("Python executable path is too long.");
        if (!Enum.IsDefined(ResponseMode)) throw new ArgumentException("Invalid response mode.");
        if (string.IsNullOrWhiteSpace(Monitor) || Monitor.Length > 128) throw new ArgumentException("Invalid monitor selection.");
        var keys = Shortcuts.Select(HotkeyGesture.Parse).ToArray();
        if (keys.Distinct().Count() != keys.Length) throw new ArgumentException("Each shortcut must be unique.");
        if (!ClearTemporaryScreenshots) throw new ArgumentException("Screenshots are always kept only in memory in this build.");
    }
}
