using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using InvisibleAI.Companion.AI;
using InvisibleAI.Companion.Core;

namespace InvisibleAI.Companion.Settings;

public sealed class RelayCommand(Action execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
public sealed class AsyncCommand(Func<Task> execute) : ICommand
{
    private bool running;
    public bool CanExecute(object? parameter) => !running;
    public event EventHandler? CanExecuteChanged;
    public async void Execute(object? parameter)
    { if (running) return; running = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty); try { await execute(); } finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); } }
}
public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AssistantController controller;
    private readonly IProviderCredentials credentials;
    private AppSettings settings;
    private string secret = "", status = "Changes take effect when saved. Credentials stay in Windows Credential Manager.", connectionStatus = "Not tested.";
    private CancellationTokenSource? operation;
    public AppSettings Settings { get => settings; private set { settings = value; Changed(); Changed(nameof(Provider)); Changed(nameof(Model)); } }
    public string Status { get => status; set { status = value; Changed(); } }
    public string ConnectionStatus { get => connectionStatus; private set { connectionStatus = value; Changed(); } }
    public string PendingCredential { set => secret = value; }
    public string Provider
    {
        get => Settings.Provider;
        set
        {
            if (value is null || value == Settings.Provider) return;
            operation?.Cancel(); StoreModel(); Settings.Provider = value;
            Settings.Model = value == Providers.Gemini ? Settings.GeminiModel : Settings.GroqModel;
            secret = ""; Models.Clear(); ConnectionStatus = "Not tested. Save / Connect or test your saved credential.";
            Changed(); Changed(nameof(Model)); Changed(nameof(IsGemini)); Changed(nameof(CredentialLabel)); Saved?.Invoke();
        }
    }
    public string Model { get => Settings.Model; set { if (value is null) return; Settings.Model = value; StoreModel(); Changed(); } }
    public bool IsGemini => Provider == Providers.Gemini;
    public string CredentialLabel => IsGemini ? "Gemini Cookies" : "Groq API Key";
    public ObservableCollection<AIModel> Models { get; } = [];
    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand RemoveKeyCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand RefreshModelsCommand { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Saved;
    public SettingsViewModel(AssistantController controller, IProviderCredentials credentials)
    {
        this.controller = controller; this.credentials = credentials; settings = controller.Settings.Clone();
        SaveCommand = new RelayCommand(() => Save(false));
        ResetCommand = new RelayCommand(() => { operation?.Cancel(); secret = ""; Settings = new(); Models.Clear(); ConnectionStatus = "Not tested."; Changed(nameof(IsGemini)); Changed(nameof(CredentialLabel)); Saved?.Invoke(); Status = "Defaults restored in this form. Save to apply."; });
        RemoveKeyCommand = new RelayCommand(() =>
        {
            try { operation?.Cancel(); credentials.For(Provider).Delete(); secret = ""; Models.Clear(); Saved?.Invoke(); ConnectionStatus = "Stored credential removed."; }
            catch { ConnectionStatus = "Could not remove the credential. Choose a provider and try again."; }
        });
        ConnectCommand = new AsyncCommand(async () => { if (Save(true)) await TestAsync(true); });
        TestCommand = new AsyncCommand(async () => { if (secret.Length > 0) { ConnectionStatus = "Save / Connect before testing a newly pasted credential."; return; } await TestAsync(true); });
        RefreshModelsCommand = new AsyncCommand(async () => await TestAsync(false));
    }
    private void StoreModel()
    { if (Provider == Providers.Gemini) Settings.GeminiModel = Model; else if (Provider == Providers.Groq) Settings.GroqModel = Model; }
    private bool Save(bool connect)
    {
        operation?.Cancel();
        try
        {
            Settings.Validate();
            if (connect && !Providers.IsValid(Provider)) throw new AIProviderException("Choose Gemini Web or Groq.");
            string? credential = null;
            if (secret.Length > 0)
            {
                if (!Providers.IsValid(Provider)) throw new AIProviderException("Choose a provider before saving its credential.");
                credential = IsGemini ? GeminiCookies.Normalize(secret) : secret.Trim();
                if (!IsGemini && (credential.Length is < 8 or > 2000 || credential.Any(c => c <= 32 || c >= 127))) throw new AIProviderException("Invalid Groq API key format.");
            }
            StoreModel(); controller.ApplySettings(Settings);
            if (credential is not null) credentials.For(Provider).Write(credential);
            Status = "Saved. Credentials are never displayed again."; return true;
        }
        catch (Exception e) { Status = e is AIProviderException or ArgumentException or System.ComponentModel.Win32Exception ? e.Message : "Could not save settings or secure credentials. Check Windows permissions."; return false; }
        finally { secret = ""; Saved?.Invoke(); }
    }
    private async Task TestAsync(bool test)
    {
        operation?.Cancel(); operation?.Dispose(); operation = new CancellationTokenSource();
        var ct = operation.Token; var snapshot = Settings.Clone(); ConnectionStatus = "Connecting…";
        try
        {
            if (!Providers.IsValid(snapshot.Provider)) throw new AIProviderException("Choose Gemini Web or Groq.");
            if (test)
            {
                var result = await controller.AI.TestConnectionAsync(snapshot, ct);
                if (!result.Connected) throw new AIProviderException(result.Error ?? "Provider unavailable.");
            }
            var models = await controller.AI.GetModelsAsync(snapshot, ct); ct.ThrowIfCancellationRequested();
            if (Provider != snapshot.Provider) return;
            Models.Clear(); foreach (var model in models) Models.Add(model);
            if (!models.Any(m => m.Id == Model)) Model = "";
            Changed(nameof(Model));
            ConnectionStatus = "● Connected. Choose an available model and Save settings.";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (Provider == snapshot.Provider) ConnectionStatus = e is AIProviderException ? e.Message : "Provider unavailable. Check the runtime and network connection."; }
    }
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    public void Dispose() { operation?.Cancel(); operation?.Dispose(); operation = null; secret = ""; }
}
