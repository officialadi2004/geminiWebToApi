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
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Shared.Protocol.Message.Json)
            ?? throw new InvalidDataException("Settings are empty.");
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
