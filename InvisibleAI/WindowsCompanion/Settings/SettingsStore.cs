using System.Text.Json;
using Microsoft.Win32;

namespace InvisibleAI.Companion.Settings;

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
        // Preserve appearance, shortcuts and privacy on upgrade. Provider choice requires explicit user action.
        if (settings.Provider == "OpenAI") { settings.Provider = ""; settings.Model = ""; }
        settings.Validate();
        return settings;
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(DirectoryPath);
        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Shared.Protocol.Message.Json));
        File.Move(temporary, Path, true);
    }
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("InvisibleAI", $"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue("InvisibleAI", false);
    }
}
