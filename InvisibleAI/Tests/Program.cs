using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using InvisibleAI.Companion.Core;
using InvisibleAI.Companion.AI;
using InvisibleAI.Companion.Hotkeys;
using InvisibleAI.Companion.NativeMessaging;
using InvisibleAI.Companion.Overlay;
using InvisibleAI.Companion.Settings;
using InvisibleAI.Shared.Protocol;

namespace InvisibleAI.Tests;

internal static class Program
{
    private static int passed, failed;
    private static readonly List<string> results = [];
    [STAThread]
    public static int Main(string[] args)
    {
        Run("Framing: Unicode round trip and fragmented reads", FramingRoundTrip);
        Run("Framing: reject oversized, negative and truncated frames", FramingInvalid);
        foreach (var test in ProviderTests.All) Run(test.Name, test.Run);
        Run("Settings: validate defaults, bounded values, protected shortcuts", SettingsValidation);
        Run("Settings: atomic persistence, no key field", SettingsPersistence);
        Run("Geometry: negative monitor coordinates and edge clamping", Geometry);
        Run("Windows: global hotkey registration and release", HotkeyRegistration);
        Run("Windows: async pipe handshake, framing, dispatch and isolation", PipeHandshake);
        Run("Windows: Credential Manager round trip in isolated test target", Credentials);
        if (Array.IndexOf(args, "--ui") >= 0)
        {
            Run("Windows: tiny overlay, no activation, click-through, hidden idle", Overlay);
            Run("Windows: region selection cancels and releases temporary windows", SelectionCancellation);
            Run("Windows: controller text/image workflow, request supersession and privacy", ControllerWorkflow);
            Run("Windows: provider settings, native routing and credential rejection", ProviderWorkflow);
            Run("Windows: captures only a synthetic selected rectangle", RegionCapture);
        }
        else Console.WriteLine("UI test skipped. Run with --ui on an ordinary interactive desktop.");
        Console.WriteLine($"{passed} passed; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
    private static void Run(string name, Func<Task> test)
    {
        try { test().GetAwaiter().GetResult(); passed++; results.Add("PASS " + name); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; results.Add("FAIL " + name + ": " + e.Message); Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }
    private static async Task FramingRoundTrip()
    {
        using var memory = new MemoryStream();
        var message = Message.Create("TEXT_INPUT", "unicode", new { text = "Ω — नमस्ते 🙂" });
        await Framing.WriteAsync(memory, message, default);
        memory.Position = 0;
        using var fragmented = new FragmentedStream(memory.ToArray());
        var decoded = await Framing.ReadAsync(fragmented, default);
        Assert(decoded?.Payload?.GetProperty("text").GetString() == "Ω — नमस्ते 🙂", "UTF-8 did not round trip.");
        Assert(await Framing.ReadAsync(fragmented, default) is null, "EOF must be clean.");
    }
    private static async Task FramingInvalid()
    {
        foreach (int length in new[] { -1, 0, Framing.MaxRequestBytes + 1 })
        {
            var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, length);
            await Throws<ProtocolException>(() => Framing.ReadAsync(new MemoryStream(header), default));
        }
        await Throws<EndOfStreamException>(() => Framing.ReadAsync(new MemoryStream(new byte[] {1,0}), default));
        await Throws<EndOfStreamException>(() => Framing.ReadAsync(new MemoryStream(new byte[] {4,0,0,0,1}), default));
        await Throws<ProtocolException>(() => Framing.WriteAsync(new MemoryStream(), new Message("PING", "bad", Version: 2), default));
    }
    private static Task SettingsValidation() => Sync(() =>
    {
        var s = new AppSettings(); s.Validate();
        foreach (string forbidden in new[] { "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Shift+A", "A", "Ctrl+Ctrl+V", "Ctrl+Shift+LeftCtrl" })
        {
            bool rejected = false; try { HotkeyGesture.Parse(forbidden); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, "Protected shortcut allowed: " + forbidden);
        }
        Assert(HotkeyGesture.Parse("Ctrl+Shift+V").KeyCode == 0x56, "Wrong VK.");
        foreach (Action<AppSettings> invalid in new Action<AppSettings>[] { v => v.DotSize = 100, v => v.DotOpacity = double.NaN, v => v.Model = "invalid model",
            v => v.ScreenShortcut = v.ClipboardShortcut, v => v.ClearTemporaryScreenshots = false, v => v.ResponseMode = (ResponseMode)42 })
        { var value = new AppSettings(); invalid(value); bool rejected = false; try { value.Validate(); } catch (ArgumentException) { rejected = true; } Assert(rejected, "Invalid setting accepted."); }
    });
    private static Task SettingsPersistence() => Sync(() =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "InvisibleAI-test-" + Guid.NewGuid());
        var store = new SettingsStore(directory);
        Exception? original = null;
        try
        {
            store.Save(new AppSettings { Model = "test-model", ResponseMode = ResponseMode.SHORT });
            Assert(store.Load().Model == "test-model", "Settings lost.");
            Assert(!File.ReadAllText(store.Path).Contains("apiKey", StringComparison.OrdinalIgnoreCase), "Secret schema detected.");
            Assert(!File.Exists(store.Path + ".tmp"), "Atomic write left temporary file.");
        }
        catch (Exception e) { original = e; throw; }
        finally
        {
            try { if (File.Exists(store.Path)) File.Delete(store.Path); if (File.Exists(store.Path + ".tmp")) File.Delete(store.Path + ".tmp"); if (Directory.Exists(directory)) Directory.Delete(directory); }
            catch when (original is not null) { /* Preserve the actual failure, not a cleanup exception. */ }
        }
    });
    private static Task Geometry() => Sync(() =>
    {
        var area = new Rectangle(-1920, -200, 1920, 1040);
        var anchor = MonitorService.Anchor(area, 280, 50, 16, 16);
        Assert(anchor.X == -296 && anchor.Y == 774, "Negative coordinates wrong.");
        var clamped = MonitorService.Anchor(area, 280, 50, 9999, 9999);
        Assert(clamped.X == area.Left && clamped.Y == area.Top, "Offset not clamped.");
    });
    private static Task HotkeyRegistration() => Sync(() =>
    {
        using var service = new HotkeyService();
        service.Configure(new[] { "Ctrl+Alt+Shift+F11" }, new Action[] { () => { } });
        using (var other = new HotkeyService())
        {
            var conflicts = other.ConfigureAvailable(new[] { "Ctrl+Alt+Shift+F11", "Ctrl+Alt+Shift+F12" }, new Action[] { () => { }, () => { } });
            Assert(conflicts.Length == 1 && conflicts[0] == "Ctrl+Alt+Shift+F11", "A conflict disabled unrelated shortcuts.");
        }
        service.Configure(new[] { "Ctrl+Alt+Shift+F12" }, new Action[] { () => { } });
    });
    private static Task PipeHandshake() => Sync(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var bridge = new BridgeServer(dispatcher, async (message, send, ct) => {
            Assert(dispatcher.CheckAccess(), "Handler did not run on UI dispatcher.");
            await send(Message.Create("SETTINGS_UPDATE", message.Id, new { connected = true }));
        });
        bridge.Start();
        var operation = Task.Run(async () => {
            using var pipe = new NamedPipeClientStream(".", Identity.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(5000);
            await pipe.ConnectAsync(timeout.Token);
            await Framing.WriteAsync(pipe, Message.Create("PING", "handshake"), timeout.Token);
            var reply = await Framing.ReadAsync(pipe, timeout.Token);
            Assert(reply?.Id == "handshake" && reply.Payload?.GetProperty("connected").GetBoolean() == true, "Bad pipe reply.");
            await NativeExecutable(timeout.Token);
        });
        PumpUntil(operation); operation.GetAwaiter().GetResult();
    });
    private static async Task NativeExecutable(CancellationToken ct)
    {
        var builtHost = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WindowsCompanion/NativeMessaging/Host/bin/Release/net10.0-windows"));
        Assert(File.Exists(Path.Combine(builtHost, "InvisibleAI.NativeHost.exe")), "Build the solution before native-host tests.");
        var directory = Path.Combine(Path.GetTempPath(), "InvisibleAI-host-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            foreach (string file in Directory.GetFiles(builtHost)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            const string origin = "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/";
            File.WriteAllText(Path.Combine(directory, "com.invisibleai.assistant.json"), JsonSerializer.Serialize(new { allowed_origins = new[] { origin } }));
            foreach (string caller in new[] { origin, "chrome-extension://bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/" })
            {
                var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(directory, "InvisibleAI.NativeHost.exe"))
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                info.ArgumentList.Add(caller);
                using var child = System.Diagnostics.Process.Start(info)!;
                try
                {
                    if (caller == origin) await Framing.WriteAsync(child.StandardInput.BaseStream, Message.Create("PING", "native-executable"), ct);
                    var response = await Framing.ReadAsync(child.StandardOutput.BaseStream, ct);
                    Assert(caller == origin ? response?.Type == "SETTINGS_UPDATE" && response.Id == "native-executable" : response?.Type == "ERROR", "Native host origin/framing failed.");
                    child.StandardInput.Close();
                    await child.WaitForExitAsync(ct);
                    Assert(child.ExitCode == (caller == origin ? 0 : 1), "Native host exit/disconnect failed.");
                    Assert((await child.StandardError.ReadToEndAsync(ct)).Length == 0, "Unexpected stderr output.");
                }
                finally { if (!child.HasExited) child.Kill(); }
            }
        }
        finally
        {
            foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
    private static Task SelectionCancellation() => Sync(() =>
    {
        var capture = new InvisibleAI.Companion.ScreenCapture.RegionCaptureService();
        using var cancellation = new CancellationTokenSource(150);
        var operation = Dispatcher.CurrentDispatcher.InvokeAsync(() => capture.SelectAndCaptureAsync(cancellation.Token)).Task.Unwrap();
        PumpUntil(operation);
        bool cancelled = false; try { operation.GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled, "Selection did not cancel.");
        Assert(capture.SelectedMonitor is null, "Selection captured data on cancellation.");
    });
    private static Task Credentials() => Sync(() =>
    {
        var keys = new CredentialStore("InvisibleAI.Tests/" + Guid.NewGuid());
        try { Assert(keys.Read() is null, "Test target already exists."); keys.Write("synthetic-test-secret"); Assert(keys.Read() == "synthetic-test-secret", "Credential did not round trip."); }
        finally { keys.Delete(); }
        Assert(keys.Read() is null, "Credential not deleted.");
    });
    private static Task ControllerWorkflow() => Sync(() =>
    {
        PumpUntil(Task.Delay(40));
        var directory = Path.Combine(Path.GetTempPath(), "InvisibleAI-controller-test-" + Guid.NewGuid());
        var store = new SettingsStore(directory);
        store.Save(new AppSettings {
            ClipboardShortcut = "Ctrl+Alt+Shift+F1", ScreenShortcut = "Ctrl+Alt+Shift+F2", HideShortcut = "Ctrl+Alt+Shift+F3",
            ToggleShortcut = "Ctrl+Alt+Shift+F4", ExpandShortcut = "Ctrl+Alt+Shift+F5" });
        var fake = new FakeAgent();
        var controller = new AssistantController(Dispatcher.CurrentDispatcher, store, new FakeKeys(), fake);
        var operation = Task.Run(async () =>
        {
            using var pipe = new NamedPipeClientStream(".", Identity.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(6000);
            await pipe.ConnectAsync(timeout.Token);
            await Framing.WriteAsync(pipe, Message.Create("TEXT_INPUT", "slow", new { text = "slow" }), timeout.Token);
            Assert((await Framing.ReadAsync(pipe, timeout.Token))?.Type == "PROCESSING_START", "No processing start.");
            await Framing.WriteAsync(pipe, Message.Create("TEXT_INPUT", "latest", new { text = "binary search?" }), timeout.Token);
            var replies = new List<Message>();
            while (replies.Find(m => m.Id == "latest" && m.Type == "PROCESSING_COMPLETE") is null || replies.Find(m => m.Id == "slow" && m.Type == "PROCESSING_COMPLETE") is null)
                replies.Add((await Framing.ReadAsync(pipe, timeout.Token))!);
            Assert(replies.Find(m => m.Id == "slow" && m.Type == "PROCESSING_COMPLETE")!.Payload?.GetProperty("status").GetString() == "cancelled", "Old request not cancelled.");
            Assert(replies.Find(m => m.Id == "latest" && m.Type == "PROCESSING_COMPLETE")!.Payload?.GetProperty("text").GetString() == "O(log n)", "Latest request did not complete.");
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6lS8AAAAASUVORK5CYII=");
            await Framing.WriteAsync(pipe, Message.Create("SCREENSHOT_INPUT", "image", new { imageBase64 = Convert.ToBase64String(png) }), timeout.Token);
            Assert((await Framing.ReadAsync(pipe, timeout.Token))?.Type == "PROCESSING_START", "Image not processing.");
            Assert((await Framing.ReadAsync(pipe, timeout.Token))?.Type == "PROCESSING_COMPLETE", "Image not complete.");
            await Task.Delay(20, timeout.Token);
            Assert(fake.LastImage is not null && Array.TrueForAll(fake.LastImage, b => b == 0), "Image bytes not cleared after processing.");
        });
        try
        {
            PumpUntil(operation); operation.GetAwaiter().GetResult();
            Assert(fake.Calls == 3, "Unexpected AI invocation count.");
            var disabled = controller.Settings.Clone(); disabled.NetworkEnabled = false; controller.ApplySettings(disabled);
            var blocked = Task.Run(async () =>
            {
                using var pipe = new NamedPipeClientStream(".", Identity.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var timeout = new CancellationTokenSource(2000); await pipe.ConnectAsync(timeout.Token);
                await Framing.WriteAsync(pipe, Message.Create("TEXT_INPUT", "blocked", new { text = "private" }), timeout.Token);
                Assert((await Framing.ReadAsync(pipe, timeout.Token))?.Type == "ERROR", "Disabled network accepted input.");
            });
            PumpUntil(blocked); blocked.GetAwaiter().GetResult(); Assert(fake.Calls == 3, "Privacy gate failed.");
            var settings = new SettingsWindow(new SettingsViewModel(controller, new FakeKeys())) { ShowActivated = false };
            try { settings.Show(); PumpUntil(Task.Delay(80)); RenderWindow(settings, "settings.png"); } finally { settings.Close(); }
        }
        finally { controller.Dispose(); File.Delete(store.Path); Directory.Delete(directory); }
    });
    private static Task ProviderWorkflow() => Sync(() =>
    {
        PumpUntil(Task.Delay(60));
        var operation = ProviderTests.NativeWorkflow();
        PumpUntil(operation); operation.GetAwaiter().GetResult();
    });
    private static Task Overlay() => Sync(() =>
    {
        var settings = new AppSettings();
        var window = new IndicatorWindow();
        try
        {
            Assert(!window.IsVisible, "Idle window visible.");
            var monitor = MonitorService.Active();
            window.ShowProcessing(settings, monitor);
            PumpUntil(Task.Delay(120));
            Assert(window.IsVisible && window.ActualWidth <= 16 && window.ActualHeight <= 16, $"Processing window is not tiny: visible={window.IsVisible}, {window.ActualWidth} × {window.ActualHeight} DIP.");
            nint hwnd = new WindowInteropHelper(window).Handle;
            long style = GetWindowLongPtr(hwnd, -20).ToInt64();
            Assert((style & 0x20) != 0 && (style & 0x08000000) != 0, "Window not click-through/non-activating.");
            // The user may change focus while tests run; the indicator must never be the foreground HWND.
            Assert(GetForegroundWindow() != hwnd, "Dot stole focus.");
            window.ShowAnswer(new("Binary Search — O(log n).", []), settings, monitor);
            PumpUntil(Task.Delay(80));
            Assert((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x20) != 0, "Response blocks input.");
            Assert(GetForegroundWindow() != hwnd, "Response stole focus.");
            window.HideIndicator(); Assert(!window.IsVisible, "Idle did not hide.");
            window.ShowAnswer(new("A", []), new AppSettings { InteractiveResponse = true }, monitor);
            Assert((GetWindowLongPtr(hwnd, -20).ToInt64() & 0x20) == 0, "Interactive response cannot be clicked.");
            window.ShowAnswer(new("Binary Search — O(log n).", []), new AppSettings { ResponseSeconds = 2 }, monitor);
            RenderWindow(window, "response.png");
            PumpUntil(Task.Delay(2100)); Assert(!window.IsVisible, "Response did not auto-hide.");
        }
        finally { window.Close(); }
    });
    private static Task RegionCapture() => Sync(() =>
    {
        var fixture = new Window { Title = "InvisibleAI synthetic capture test", Width = 320, Height = 140, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Topmost = true,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 136, 85)),
            WindowStartupLocation = WindowStartupLocation.CenterScreen };
        try
        {
            fixture.Show(); PumpUntil(Task.Delay(150));
            var hwnd = new WindowInteropHelper(fixture).Handle;
            var point = new NativePoint(); ClientToScreen(hwnd, ref point);
            byte[] bytes = InvisibleAI.Companion.ScreenCapture.RegionCaptureService.Capture(new Rectangle(point.X + 30, point.Y + 30, 80, 60), default);
            using var bitmap = new Bitmap(new MemoryStream(bytes));
            Assert(bitmap.Width == 80 && bitmap.Height == 60, "Capture included pixels outside the selected region.");
            var pixel = bitmap.GetPixel(40, 30);
            Assert(Math.Abs(pixel.R - 34) < 8 && Math.Abs(pixel.G - 136) < 8 && Math.Abs(pixel.B - 85) < 8, "Selected capture does not contain the synthetic fixture.");
            Array.Clear(bytes);
        }
        finally { fixture.Close(); }
    });
    private static void RenderWindow(Window window, string filename)
    {
        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/qa")); Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, filename)); encoder.Save(file);
    }
    private static void PumpUntil(Task task)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    private sealed class FakeKeys : ICredentialStore, IProviderCredentials
    { public ICredentialStore For(string provider) => this; public int Reads; public string? Read() { Reads++; return "test-key-value"; } public void Write(string secret) { } public void Delete() { } }
    private sealed class FakeAgent : IAIAgent
    {
        public int Calls; public byte[]? LastImage;
        public Task<AIAnswer> AskTextAsync(string text, AppSettings settings, CancellationToken ct) => AskMultimodalAsync(text, null, settings, ct);
        public Task<AIAnswer> AskImageAsync(byte[] image, AppSettings settings, CancellationToken ct) => AskMultimodalAsync(null, image, settings, ct);
        public async Task<AIAnswer> AskMultimodalAsync(string? text, byte[]? image, AppSettings settings, CancellationToken ct)
        { Calls++; LastImage = image; if (text == "slow") await Task.Delay(1000, ct); return new("O(log n)", []); }
    }
    private sealed class FragmentedStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], ct);
    }
}
