using System.Text.Json;

namespace InvisibleAI.Helper.Settings;

public sealed class SettingsStore
{
    public string DirectoryPath { get; }
    public string Path => System.IO.Path.Combine(DirectoryPath, "settings.json");
    public SettingsStore(string? directory = null) => DirectoryPath = directory ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvisibleAI");
    public AppSettings Load()
    {
        if (!File.Exists(Path)) return new();
        string json = File.ReadAllText(Path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, Shared.Protocol.Message.Json)
            ?? throw new InvalidDataException("Settings are empty.");
        // Migrate browser-first defaults once, retaining explicitly customized durations.
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("preferencesVersion", out _))
        {
            if (settings.ResponseSeconds == 12) settings.ResponseSeconds = 22;
            settings.MaxResponseLength = 12000; settings.MaxOutputTokens = 4096;
        }
        if (!document.RootElement.TryGetProperty("preferencesVersion", out _) || settings.PreferencesVersion < 4)
        {
            // Replace the old default card opacity with subtle text; retain custom values.
            if (settings.ResponseOpacity == .94) settings.ResponseOpacity = .55;
            settings.PreferencesVersion = 4;
        }
        // Read existing provider preferences; old UI fields are ignored.
        if (settings.Provider == "OpenAI") { settings.Provider = ""; settings.Model = ""; }
        settings.Validate();
        return settings;
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(DirectoryPath);
        string temporary = Path + "." + Guid.NewGuid() + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Shared.Protocol.Message.Json));
            File.Move(temporary, Path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
