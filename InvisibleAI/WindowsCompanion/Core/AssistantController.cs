using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using InvisibleAI.Companion.AI;
using InvisibleAI.Companion.Clipboard;
using InvisibleAI.Companion.Hotkeys;
using InvisibleAI.Companion.NativeMessaging;
using InvisibleAI.Companion.Overlay;
using InvisibleAI.Companion.ScreenCapture;
using InvisibleAI.Companion.Settings;
using InvisibleAI.Companion.Tray;
using InvisibleAI.Shared.Protocol;

namespace InvisibleAI.Companion.Core;

public sealed class AssistantController : IDisposable
{
    private readonly Dispatcher dispatcher;
    private readonly SettingsStore store;
    private readonly IProviderCredentials credentials;
    private readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly IAIAgent agent;
    public AIService AI { get; }
    private readonly ClipboardService clipboard = new();
    private readonly RegionCaptureService capture = new();
    private readonly IndicatorWindow overlay = new();
    private readonly HotkeyService hotkeys = new();
    private readonly TrayService tray;
    private readonly BridgeServer bridge;
    private CancellationTokenSource? current;
    private CancellationTokenSource providerOperations = new();
    private SettingsWindow? settingsWindow;
    private Window? expandedWindow;
    private AIAnswer? lastAnswer;
    private bool disposed;
    public AppSettings Settings { get; private set; }
    public event Action? SettingsChanged;
    public AssistantController(Dispatcher dispatcher, SettingsStore store, IProviderCredentials credentials, IAIAgent? agentOverride = null, AIService? serviceOverride = null)
    {
        this.dispatcher = dispatcher; this.store = store; this.credentials = credentials;
        string? warning = null;
        try { Settings = store.Load(); } catch { Settings = new(); warning = "Saved settings could not be loaded. Defaults are active; review Settings."; }
        AI = serviceOverride ?? new AIService(new GeminiWebProvider(new GeminiWorkerProcess(), credentials), new GroqProvider(client, credentials));
        agent = agentOverride ?? AI;
        tray = new TrayService(this);
        overlay.ExpandRequested += Expand;
        bridge = new BridgeServer(dispatcher, HandleNativeAsync);
        var conflicts = hotkeys.ConfigureAvailable(Settings.Shortcuts, HotkeyActions);
        if (conflicts.Length > 0) warning = "Shortcuts unavailable: " + string.Join(", ", conflicts) + ". Other shortcuts work. Change conflicts in Settings → Keyboard.";
        bridge.Start();
        if (warning is not null) tray.Notify(warning);
    }
    private Action[] HotkeyActions => [() => RunSafely(SendClipboardAsync), () => RunSafely(SelectScreenAsync), Hide, Toggle, ExpandLast];
    private void ConfigureHotkeys(AppSettings value) => hotkeys.Configure(value.Shortcuts, HotkeyActions);
    public async void RunSafely(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception e) { ShowError(UserError(e)); }
    }
    private static string UserError(Exception e) => e is InvalidOperationException or ArgumentException or Win32Exception or ProtocolException
        ? e.Message : "The action could not be completed. Try again or review Settings.";
    public void ApplySettings(AppSettings next)
    {
        next.Validate(); var old = Settings;
        ConfigureHotkeys(next);
        try
        {
            if (next.StartWithWindows != old.StartWithWindows) SettingsStore.SetStartup(next.StartWithWindows);
            store.Save(next);
        }
        catch
        {
            ConfigureHotkeys(old);
            if (next.StartWithWindows != old.StartWithWindows) SettingsStore.SetStartup(old.StartWithWindows);
            throw;
        }
        Cancel(); providerOperations.Cancel(); providerOperations.Dispose(); providerOperations = new();
        Settings = next.Clone(); SettingsChanged?.Invoke();
        if (!Settings.TrayMode) ShowSettings();
    }
    public void Toggle()
    {
        try { var next = Settings.Clone(); next.Enabled = !next.Enabled; ApplySettings(next); }
        catch (Exception e) { tray.Notify(UserError(e)); }
    }
    public void Cancel() { current?.Cancel(); overlay.HideIndicator(); }
    public void Hide() { Cancel(); expandedWindow?.Close(); }
    private CancellationTokenSource BeginRequest(CancellationToken external = default)
    { Cancel(); var next = CancellationTokenSource.CreateLinkedTokenSource(external); current = next; return next; }
    private void Guard(bool screenshot)
    {
        if (!Settings.Enabled) throw new InvalidOperationException("Assistant disabled. Enable it from the tray or your toggle shortcut.");
        if (!Settings.NetworkEnabled) throw new InvalidOperationException("Network requests are disabled in Settings → Privacy.");
        if (screenshot ? !Settings.ScreenshotEnabled : !Settings.ClipboardEnabled)
            throw new InvalidOperationException(screenshot ? "Screenshot processing is disabled." : "Text/clipboard processing is disabled.");
        if (!NativeMethods.IsOrdinaryDesktop()) throw new InvalidOperationException("This action is unavailable on the current Windows desktop.");
    }
    public async Task SendClipboardAsync()
    {
        Guard(false); var monitor = MonitorService.Active(Settings.Monitor);
        using var cancellation = BeginRequest();
        try { var text = await clipboard.ReadTextAsync(cancellation.Token); await AskAsync(text, null, monitor, cancellation, null); }
        finally { if (ReferenceEquals(current, cancellation)) current = null; }
    }
    public async Task SelectScreenAsync()
    {
        Guard(true);
        using var cancellation = BeginRequest();
        byte[]? image = null;
        try
        {
            image = await capture.SelectAndCaptureAsync(cancellation.Token);
            if (image is null) return;
            var monitor = Settings.Monitor == "Active" ? capture.SelectedMonitor ?? MonitorService.Active() : MonitorService.Active(Settings.Monitor);
            await AskAsync(null, image, monitor, cancellation, null);
        }
        finally { if (image is not null) Array.Clear(image); if (ReferenceEquals(current, cancellation)) current = null; }
    }
    private async Task<AIAnswer> AskAsync(string? text, byte[]? image, MonitorSnapshot monitor,
        CancellationTokenSource cancellation, Func<Message, Task>? send, string id = "desktop")
    {
        var options = Settings.Clone();
        overlay.ShowProcessing(options, monitor);
        if (send is not null) await send(Message.Create("PROCESSING_START", id));
        try
        {
            var answer = await agent.AskMultimodalAsync(text, image, options, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (ReferenceEquals(current, cancellation)) { lastAnswer = answer; overlay.ShowAnswer(answer, options, monitor); }
            if (send is not null) await send(Message.Create("PROCESSING_COMPLETE", id, new { text = answer.Text, sources = answer.Sources, status = "complete", result = answer.ToUnifiedResponse() }));
            return answer;
        }
        catch
        {
            if (ReferenceEquals(current, cancellation)) overlay.HideIndicator();
            cancellation.Token.ThrowIfCancellationRequested();
            throw;
        }
    }
    public void ShowError(string text) => overlay.ShowAnswer(new AIAnswer(text, []), Settings, MonitorService.Active(Settings.Monitor));
    public void ShowSettings()
    {
        overlay.HideIndicator();
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow(new SettingsViewModel(this, credentials));
        settingsWindow.Closed += (_, _) => { settingsWindow = null; };
        settingsWindow.Show();
    }
    public void ExpandLast() { if (lastAnswer is not null) Expand(lastAnswer); }
    private void Expand(AIAnswer answer)
    {
        expandedWindow?.Close();
        expandedWindow = new ExpandedResponseWindow(answer);
        expandedWindow.Closed += (_, _) => expandedWindow = null;
        expandedWindow.Show();
    }
    private async Task HandleNativeAsync(Message message, Func<Message, Task> send, CancellationToken ct)
    {
        try
        {
            switch (message.Type)
            {
                case "PING":
                    await send(Message.Create("SETTINGS_UPDATE", message.Id, new { connected = true, enabled = Settings.Enabled,
                        responseMode = Settings.ResponseMode.ToString(), webSearch = Settings.WebSearch, model = Settings.Model, provider = Settings.Provider })); break;
                case "OPEN_SETTINGS": ShowSettings(); await send(Message.Create("SETTINGS_UPDATE", message.Id, new { opened = true })); break;
                case "SETTINGS_UPDATE":
                    var next = Settings.Clone();
                    var patch = message.Payload ?? throw new ProtocolException("Settings payload required.");
                    foreach (var property in patch.EnumerateObject())
                        switch (property.Name)
                        {
                            case "responseMode": next.ResponseMode = Enum.Parse<ResponseMode>(property.Value.GetString() ?? ""); break;
                            case "webSearch": next.WebSearch = property.Value.GetBoolean(); break;
                            case "provider":
                                next.Provider = property.Value.GetString() ?? "";
                                if (!Providers.IsValid(next.Provider)) throw new AIProviderException("Choose Gemini Web or Groq.");
                                next.Model = next.Provider == Providers.Gemini ? next.GeminiModel : next.GroqModel; break;
                            default: throw new ProtocolException("This setting must be changed in the Windows companion.");
                        }
                    ApplySettings(next);
                    await send(Message.Create("SETTINGS_UPDATE", message.Id, new { responseMode = Settings.ResponseMode.ToString(), webSearch = Settings.WebSearch })); break;
                case "PROVIDER_API":
                {
                    var api = message.Payload ?? throw new ProtocolException("Provider request required.");
                    if (api.EnumerateObject().Any(p => p.Name is not ("method" or "path"))) throw new ProtocolException("Only method and path are accepted. Save credentials in Windows Settings.");
                    using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, providerOperations.Token);
                    var apiResult = await new ProviderApi(AI).InvokeAsync(api.GetProperty("method").GetString() ?? "", api.GetProperty("path").GetString() ?? "", Settings, providerCancellation.Token);
                    await send(Message.Create("PROVIDER_RESULT", message.Id, apiResult)); break;
                }
                case "TEXT_INPUT": case "SCREENSHOT_INPUT":
                    bool isImage = message.Type == "SCREENSHOT_INPUT";
                    Guard(isImage);
                    var payload = message.Payload ?? throw new ProtocolException("Input payload required.");
                    string? text = null; byte[]? image = null;
                    if (!isImage)
                    {
                        text = payload.GetProperty("text").GetString();
                        if (string.IsNullOrWhiteSpace(text) || text.Length > 50000) throw new ProtocolException("Text must contain 1–50,000 characters.");
                    }
                    else
                    {
                        image = Convert.FromBase64String(payload.GetProperty("imageBase64").GetString() ?? "");
                        if (image.Length < 8 || image.Length > 5 * 1024 * 1024 || !image.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
                            throw new ProtocolException("Provide a PNG image of at most 5 MiB.");
                    }
                    var monitor = MonitorService.Active(Settings.Monitor);
                    using (var cancellation = BeginRequest(ct))
                    {
                        try { await AskAsync(text, image, monitor, cancellation, send, message.Id); }
                        finally { if (image is not null) Array.Clear(image); if (ReferenceEquals(current, cancellation)) current = null; }
                    }
                    break;
                default: throw new ProtocolException("Unsupported message type.");
            }
        }
        catch (OperationCanceledException) { if (!ct.IsCancellationRequested) await send(Message.Create("PROCESSING_COMPLETE", message.Id, new { status = "cancelled" })); }
        catch (Exception e)
        {
            string error = UserError(e);
            // A malformed input is reported to its caller, not displayed over the user's application.
            if (e is InvalidOperationException && !ct.IsCancellationRequested) ShowError(error);
            if (!ct.IsCancellationRequested) await send(Message.Create("ERROR", message.Id, new { message = error }));
        }
    }
    public void Dispose()
    { if (disposed) return; disposed = true; Cancel(); providerOperations.Cancel(); providerOperations.Dispose(); bridge.Dispose(); tray.Dispose(); hotkeys.Dispose(); overlay.Close(); expandedWindow?.Close(); settingsWindow?.Close(); client.Dispose(); }
}
