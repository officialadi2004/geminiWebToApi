using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InvisibleAI.Helper;
using InvisibleAI.Helper.AI;
using InvisibleAI.Helper.Display;
using InvisibleAI.Helper.Settings;
using InvisibleAI.Shared.Protocol;

namespace InvisibleAI.Tests;
internal static class PrivateDisplayTests
{
    public static readonly (string Name, Func<Task> Run)[] All = [
        ("Private display: Gemini/Groq text and images never return answers to browser", Routing),
        ("Private display: failure stops upload; cancellation and privacy gates hide safely", Failure),
        ("Private defaults: missing/legacy settings are private; explicit opt-out is preserved", Defaults)
    ];
    // Requires a real interactive Windows desktop. CI's service session is not a display test.
    public static readonly (string Name, Func<Task> Run)[] WindowsUI = [
        ("Windows private HWND: exclusion, no activation, stale IDs, hide and disposal", NativeWindow),
        ("Windows capture: excluded HWND absent from GDI crop; positive control visible", Capture),
        ("Windows private hover: compact MCQ, paused expiry, resumed remaining time", Hover),
        ("Windows private lifecycle: hidden HWND creation/recreation and exclusion loss", Lifecycle)
    ];
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static Task Defaults()
    {
        string directory = Path.Combine(Path.GetTempPath(), "InvisibleAI-private-default-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(directory);
            Check(new AppSettings().PrivateResponses && store.Load().PrivateResponses, "New settings are not private.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(store.Path, "{\"preferencesVersion\":4,\"networkEnabled\":false,\"responseSeconds\":5,\"responseOpacity\":0.5}");
            var loaded = store.Load();
            Check(loaded.PrivateResponses && !loaded.NetworkEnabled && loaded.ResponseSeconds == 5 && loaded.ResponseOpacity == .5, "Legacy default or customized privacy/appearance lost.");
            loaded.PrivateResponses = false; store.Save(loaded);
            Check(!store.Load().PrivateResponses && !store.Load().NetworkEnabled, "Explicit opt-out was overwritten.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return Task.CompletedTask;
    }
    private sealed class Provider(string name) : IAIProvider
    {
        public string Name => name;
        public int Calls;
        public bool Wait;
        public async Task<AIAnswer> GenerateAsync(string? text, byte[]? image, AppSettings settings, CancellationToken ct)
        {
            Calls++; if (Wait) await Task.Delay(Timeout.Infinite, ct);
            return new("private-answer-sentinel", [], Providers.Id(Name), "test-model", Details: "private-details-sentinel");
        }
        public Task<IReadOnlyList<AIModel>> GetModelsAsync(AppSettings settings, CancellationToken ct) => Task.FromResult<IReadOnlyList<AIModel>>([new("test-model", "Test", true, false)]);
    }
    private sealed class Vault : IProviderCredentials { public ICredentialStore For(string provider) => new Secret(); }
    private sealed class Secret : ICredentialStore { public string? Read() => null; public void Write(string value) => throw new Exception("No credential writing expected."); public void Delete() { } }
    private sealed class Display : IPrivateResponseDisplay
    {
        public bool Fail;
        public string? Current;
        public int Begins, Answers;
        public Task BeginAsync(string id, AppSettings settings, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Begins++; if (Fail) throw new AIProviderException("Private display unavailable."); Current = id; return Task.CompletedTask; }
        public Task AnswerAsync(string id, AIAnswer answer, CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (Current == id) Answers++; return Task.CompletedTask; }
        public Task HideAsync(string? id = null) { if (id is null || Current == id) Current = null; return Task.CompletedTask; }
        public void Dispose() { Current = null; }
    }
    private static async Task Routing()
    {
        string directory = Path.Combine(Path.GetTempPath(), "InvisibleAI-private-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(directory); var display = new Display();
            var gemini = new Provider(Providers.Gemini); var groq = new Provider(Providers.Groq);
            var session = new Session(new AIService(gemini, groq), store, new Vault(), display);
            foreach (string provider in new[] { Providers.Gemini, Providers.Groq })
            {
                store.Save(new() { PrivateResponses = true, Provider = provider, Model = "test-model" });
                foreach (bool image in new[] { false, true })
                {
                    var response = await session.HandleAsync(Message.Create(image ? "SCREENSHOT_INPUT" : "TEXT_INPUT", Guid.NewGuid().ToString(), image ? new { imageBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhS8AAAAASUVORK5CYII=" } : (object)new { text = "Synthetic fixture question" }), default);
                    string json = JsonSerializer.Serialize(response, Message.Json);
                    Check(!json.Contains("private-answer") && !json.Contains("private-details") && !json.Contains("content") && !json.Contains("result"), "Private content escaped native display.");
                    Check(response.Payload!.Value.GetProperty("displayed").GetBoolean(), "Missing private acknowledgment.");
                }
            }
            Check(gemini.Calls == 2 && groq.Calls == 2 && display.Answers == 4, "Provider routing or display changed.");
            await session.HandleAsync(Message.Create("HIDE_RESPONSE", "hide", new { targetId = "old" }), default);
            Check(display.Current is not null, "Stale hide removed latest answer.");
            await session.HandleAsync(Message.Create("HIDE_RESPONSE", "hide-all"), default);
            Check(display.Current is null, "Hide failed.");
            await session.HandleAsync(Message.Create("SETTINGS_UPDATE", "disable-private", new { privateResponses = false }), default);
            Check(!store.Load().PrivateResponses, "Private preference was not saved.");
            var normal = await session.HandleAsync(Message.Create("TEXT_INPUT", "normal", new { text = "Synthetic question" }), default);
            Check(normal.Payload!.Value.GetProperty("result").GetProperty("content").GetString() == "private-answer-sentinel", "Standard browser mode regressed.");
            var raced = await session.HandleAsync(Message.Create("TEXT_INPUT", "private-despite-settings-change", new { text = "Synthetic question", privateResponses = true }), default);
            Check(!JsonSerializer.Serialize(raced, Message.Json).Contains("private-answer"), "Concurrent settings change downgraded a private request.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task Failure()
    {
        string directory = Path.Combine(Path.GetTempPath(), "InvisibleAI-private-fail-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(directory); var settings = new AppSettings { PrivateResponses = true, Provider = Providers.Groq, Model = "test-model" }; store.Save(settings);
            var groq = new Provider(Providers.Groq); var display = new Display { Fail = true };
            var ai = new AIService(new Provider(Providers.Gemini), groq);
            var session = new Session(ai, store, new Vault(), display);
            async Task Invoke() => await session.HandleAsync(Message.Create("TEXT_INPUT", "cancel-me", new { text = "Synthetic" }), default);
            try { await Invoke(); throw new Exception("Unprotected display accepted."); } catch (AIProviderException) { }
            Check(groq.Calls == 0, "Uploaded despite display failure.");
            try { await new Session(ai, store, new Vault()).HandleAsync(Message.Create("TEXT_INPUT", "old-helper", new { text = "Synthetic" }), default); throw new Exception("Missing display accepted."); } catch (AIProviderException) { }
            display.Fail = false; groq.Wait = true;
            using (var cancellation = new CancellationTokenSource(100))
                try { await session.HandleAsync(Message.Create("TEXT_INPUT", "cancel-me", new { text = "Synthetic" }), cancellation.Token); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
            Check(display.Current is null && display.Answers == 0, "Cancellation leaked response.");
            int begins = display.Begins, calls = groq.Calls; settings.NetworkEnabled = false; store.Save(settings);
            try { await Invoke(); throw new Exception("Privacy gate ignored."); } catch (AIProviderException) { }
            Check(display.Begins == begins && groq.Calls == calls, "Privacy gate showed window or uploaded.");
        }
        finally { Directory.Delete(directory, true); }
    }
    private static async Task NativeWindow()
    {
        var foreground = WindowsPrivateResponseDisplay.Native.GetForegroundWindow();
        using var display = new WindowsPrivateResponseDisplay();
        await display.BeginAsync("old", new() { ResponseSeconds = 2 }, default);
        await display.AnswerAsync("old", new("synthetic fixture", []), default);
        await display.InspectAsync((window, bounds, visible) =>
        {
            Check(visible && bounds.Width <= 200 && bounds.Height <= 40, "Window is not compact.");
            Check(WindowsPrivateResponseDisplay.Native.GetWindowDisplayAffinity(window, out uint affinity) && affinity == 0x11, "Exclusion not enabled.");
            Check(WindowsPrivateResponseDisplay.Native.GetForegroundWindow() == foreground, "Display stole focus.");
            Check((WindowsPrivateResponseDisplay.Native.GetWindowLongPtr(window, -20).ToInt64() & 0x080000A0) == 0x080000A0, "No-activate/toolwindow/clickthrough styles missing.");
            Check(Rectangle.Intersect(System.Windows.Forms.Screen.FromHandle(foreground).Bounds, bounds) == bounds, "Window leaves monitor.");
        });
        await display.BeginAsync("new", new() { ResponseSeconds = 2 }, default);
        await display.AnswerAsync("old", new("stale fixture", []), default);
        await display.HideAsync("old");
        await display.InspectAsync((_, _, visible) => Check(visible, "Stale request hid new window."));
        await display.AnswerAsync("new", new("new fixture", []), default);
        await display.HideAsync("new");
        await display.InspectAsync((_, _, visible) => Check(!visible, "Window remains visible after hide."));
        await display.BeginAsync("expiry", new() { ResponseSeconds = 2 }, default);
        await display.AnswerAsync("expiry", new("expiry fixture", []), default);
        await Task.Delay(2300);
        await display.InspectAsync((_, _, visible) => Check(!visible, "Native response did not expire."));
    }
    private sealed class TestSurface : System.Windows.Forms.Form
    {
        public TestSurface() { FormBorderStyle = System.Windows.Forms.FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Color.White; AutoScaleMode = System.Windows.Forms.AutoScaleMode.None; StartPosition = System.Windows.Forms.FormStartPosition.Manual; }
        protected override bool ShowWithoutActivation => true;
        protected override System.Windows.Forms.CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }
    }
    private static async Task Capture()
    {
        using var display = new WindowsPrivateResponseDisplay();
        TestSurface? background = null; Rectangle crop = default; IntPtr handle = default;
        try
        {
            await display.BeginAsync("capture", new() { ResponseSeconds = 30, ResponseOpacity = 1 }, default);
            await display.AnswerAsync("capture", new("SYNTHETIC CONTROL", []), default);
            await display.InspectAsync((window, bounds, _) =>
            {
                crop = bounds; handle = window;
                background = new TestSurface { Bounds = Rectangle.Inflate(bounds, 12, 12) }; background.Show();
                WindowsPrivateResponseDisplay.Native.SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            });
            byte[] Snapshot()
            {
                // Only the owned opaque fixture area is read, in memory. No desktop recording/files.
                using var bitmap = new Bitmap(crop.Width, crop.Height);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var source = GetDC(IntPtr.Zero); var target = graphics.GetHdc();
                    try { Check(BitBlt(target, 0, 0, crop.Width, crop.Height, source, crop.X, crop.Y, 0x40CC0020), "GDI fixture crop failed."); }
                    finally { graphics.ReleaseHdc(target); ReleaseDC(IntPtr.Zero, source); }
                }
                var pixels = new byte[crop.Width * crop.Height * 3];
                int index = 0;
                for (int y = 0; y < crop.Height; y++) for (int x = 0; x < crop.Width; x++) { var color = bitmap.GetPixel(x, y); pixels[index++] = color.R; pixels[index++] = color.G; pixels[index++] = color.B; }
                return pixels;
            }
            await Task.Delay(200); byte[] excluded = Snapshot();
            byte[] included = [];
            await display.InspectAsync((window, _, _) =>
            {
                // Keep the synthetic positive control inside one UI turn, restoring exclusion
                // before the production protection watchdog executes. Never use real answers.
                Check(WindowsPrivateResponseDisplay.Native.SetWindowDisplayAffinity(window, 0), "Positive control unavailable.");
                try { DwmFlush(); included = Snapshot(); }
                finally { Check(WindowsPrivateResponseDisplay.Native.SetWindowDisplayAffinity(window, 0x11), "Cannot restore positive control exclusion."); }
            });
            await display.HideAsync("capture"); await Task.Delay(200); byte[] baseline = Snapshot();
            Check(System.Linq.Enumerable.All(baseline, value => value == 255), "Capture fixture did not cover its owned rectangle.");
            Check(!System.Linq.Enumerable.SequenceEqual(included, baseline), "Capture positive control did not show the synthetic answer.");
            Check(System.Linq.Enumerable.SequenceEqual(excluded, baseline), "This capture method included the excluded answer.");
        }
        finally
        {
            await display.InspectAsync((_, _, _) => { if (handle != IntPtr.Zero) WindowsPrivateResponseDisplay.Native.SetWindowDisplayAffinity(handle, 0x11); background?.Close(); background?.Dispose(); });
        }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    private static async Task Lifecycle()
    {
        using var display = new WindowsPrivateResponseDisplay();
        var foreground = WindowsPrivateResponseDisplay.Native.GetForegroundWindow();
        await display.InspectAsync((window, _, visible) =>
        {
            Check(!visible && !WindowsPrivateResponseDisplay.Native.IsWindowVisible(window), "New HWND was revealed before exclusion.");
            Check(WindowsPrivateResponseDisplay.Native.GetWindowDisplayAffinity(window, out uint affinity) && affinity == 0x11, "New hidden HWND is not excluded.");
        });
        await display.BeginAsync("lifecycle", new() { ResponseSeconds = 30 }, default);
        await display.AnswerAsync("lifecycle", new("SYNTHETIC LIFECYCLE", []), default);
        await display.InspectAsync((window, _, _) =>
        {
            var form = System.Windows.Forms.Control.FromHandle(window)!;
            typeof(System.Windows.Forms.Control).GetMethod("RecreateHandle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(form, null);
        });
        await display.InspectAsync((window, _, visible) =>
        {
            Check(visible, "Recreated HWND did not preserve the response.");
            Check(WindowsPrivateResponseDisplay.Native.GetWindowDisplayAffinity(window, out uint affinity) && affinity == 0x11, "Recreated HWND lost exclusion.");
            Check(WindowsPrivateResponseDisplay.Native.GetForegroundWindow() == foreground, "Recreated HWND stole focus.");
            Check(WindowsPrivateResponseDisplay.Native.SetWindowDisplayAffinity(window, 0), "Could not simulate exclusion loss.");
        });
        await Task.Delay(400);
        await display.InspectAsync((_, _, visible) => Check(!visible, "Exclusion loss left the answer visible."));
        await display.BeginAsync("recovery", new(), default);
        await display.InspectAsync((window, _, visible) => Check(visible && WindowsPrivateResponseDisplay.Native.GetWindowDisplayAffinity(window, out uint affinity) && affinity == 0x11, "New request did not recover exclusion safely."));
        await display.HideAsync();
    }
    private static async Task Hover()
    {
        var pointer = System.Windows.Forms.Cursor.Position;
        using var display = new WindowsPrivateResponseDisplay();
        try
        {
            await display.BeginAsync("hover", new() { ResponseSeconds = 2 }, default);
            await display.AnswerAsync("hover", new("C", []), default);
            await display.InspectAsync((_, bounds, _) => System.Windows.Forms.Cursor.Position = new Point(bounds.Right - 2, bounds.Bottom - 2));
            await Task.Delay(2400);
            await display.InspectAsync((_, bounds, visible) => { Check(visible, "Hover failed to pause expiry."); Check(bounds.Width < 50 && bounds.Height < 50, "Hover expanded a single choice into a large panel."); });
            await display.InspectAsync((_, bounds, _) => System.Windows.Forms.Cursor.Position = new Point(bounds.Left - 20, bounds.Top - 20));
            await Task.Delay(2300);
            await display.InspectAsync((_, _, visible) => Check(!visible, "Leaving did not resume expiry."));
        }
        finally { System.Windows.Forms.Cursor.Position = pointer; }
    }
}
