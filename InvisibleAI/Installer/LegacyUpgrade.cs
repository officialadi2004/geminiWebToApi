using System;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;

namespace InvisibleAI.Setup;

public static class LegacyUpgrade
{
    // An old tray instance keeps its own cached settings and registered global shortcuts.
    // Retire only this user's previously installed executable, preserving all credentials.
    public static void RetireCompanion(string applicationDirectory)
    {
        string executable = Path.GetFullPath(Path.Combine(applicationDirectory, "app", "InvisibleAI.Companion.exe"));
        foreach (var process in Process.GetProcessesByName("InvisibleAI.Companion"))
        {
            using (process)
            {
                string? path;
                try { if (process.HasExited) continue; path = process.MainModule?.FileName; }
                catch (System.ComponentModel.Win32Exception) { continue; } // Another OS account's process.
                catch (InvalidOperationException) { continue; } // Already exited.
                if (!MatchesExecutable(path, executable)) continue;
                if (process.MainWindowHandle != 0) throw new InvalidOperationException("Close the old companion window before upgrading.");
                process.Kill();
                if (!process.WaitForExit(5000)) throw new InvalidOperationException("The old companion has not exited.");
            }
        }
        using var startup = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (MatchesStartup(startup?.GetValue("InvisibleAI") as string, executable)) startup!.DeleteValue("InvisibleAI", false);
    }
    public static bool MatchesExecutable(string? candidate, string executable)
    {
        try { return candidate is not null && string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    public static bool MatchesStartup(string? command, string executable)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 && MatchesExecutable(command[1..end], executable);
        }
        return command.StartsWith(executable, StringComparison.OrdinalIgnoreCase) &&
            (command.Length == executable.Length || char.IsWhiteSpace(command[executable.Length]));
    }
}
