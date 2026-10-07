using System.Windows;
using InvisibleAI.Companion.Core;
using InvisibleAI.Companion.Settings;
using InvisibleAI.Shared.Protocol;

namespace InvisibleAI.Companion;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\" + Identity.InstanceName, out bool first);
        if (!first) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AssistantController? controller = null;
        app.DispatcherUnhandledException += (_, e) =>
        {
            controller?.Cancel(); controller?.ShowError("The action could not be completed. Try again."); e.Handled = true;
        };
        app.Startup += (_, _) =>
        {
            try
            {
                string? dataDir = null;
                int index = Array.IndexOf(args, "--data-dir");
                if (index >= 0 && index + 1 < args.Length) dataDir = Path.GetFullPath(args[index + 1]);
                controller = new AssistantController(app.Dispatcher, new SettingsStore(dataDir), new ProviderCredentials());
                if (!controller.Settings.TrayMode || (!args.Contains("--background") && !controller.Settings.StartMinimized)) controller.ShowSettings();
            }
            catch (Exception) { System.Windows.MessageBox.Show("Invisible AI could not start. Check settings folder access and close other instances.", "Invisible AI Assistant"); app.Shutdown(1); }
        };
        app.Exit += (_, _) => controller?.Dispose();
        app.Run();
        mutex.ReleaseMutex();
    }
}
