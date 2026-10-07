using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using InvisibleAI.Shared.Protocol;
namespace InvisibleAI.Setup;
internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(nint owner, string text, string title, uint flags);
    private static readonly string[] Keys = [@"Software\Google\Chrome\NativeMessagingHosts\com.invisibleai.assistant", @"Software\Microsoft\Edge\NativeMessagingHosts\com.invisibleai.assistant"];
    private static int Main(string[] args)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvisibleAI", "helper");
        try
        {
            if (args.Contains("--uninstall"))
            {
                foreach (string key in Keys) Registry.CurrentUser.DeleteSubKey(key, false);
                // Credentials and preferences deliberately remain available for reinstall.
                if (Directory.Exists(root)) Directory.Delete(root, true);
                if (!args.Contains("--quiet")) MessageBox(0, "Helper removed. Saved credentials remain in Windows Credential Manager.", "Invisible AI Assistant", 0);
                return 0;
            }
            using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("helper.zip") ?? throw new InvalidOperationException();
            LegacyUpgrade.RetireCompanion(Path.GetDirectoryName(root)!);
            Directory.CreateDirectory(root);
            using (var archive = new ZipArchive(payload)) archive.ExtractToDirectory(root, overwriteFiles: true);
            string manifest = Path.Combine(root, "com.invisibleai.assistant.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { name = ExtensionIdentity.Host, description = "Invisible AI credential and provider helper", path = Path.Combine(root, "InvisibleAI.Helper.exe"), type = "stdio", allowed_origins = new[] { ExtensionIdentity.Origin } }));
            foreach (string key in Keys) { using var entry = Registry.CurrentUser.CreateSubKey(key); entry.SetValue("", manifest); }
            if (!args.Contains("--quiet")) MessageBox(0, "Helper installed for Chrome and Edge. Restart the browser, install the extension, then open AI Provider settings. No background app needs to be started.", "Invisible AI Assistant", 0);
            return 0;
        }
        catch (Exception)
        {
            if (!args.Contains("--quiet")) MessageBox(0, "Setup could not finish. Close Chrome, Edge and any old Invisible AI settings window, then try again. Download a fresh package if the problem continues.", "Invisible AI Assistant", 0x10);
            return 1;
        }
    }
}
