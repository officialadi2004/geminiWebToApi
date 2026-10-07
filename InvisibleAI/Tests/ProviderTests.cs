using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;



using System.Net;
using System.Net.Http;
using System.Text.Json;
using InvisibleAI.Helper.AI;
using InvisibleAI.Helper.Settings;
using InvisibleAI.Shared.Protocol;

namespace InvisibleAI.Tests;

internal static class ProviderTests
{
    public static InvisibleAI.Helper.Session FixtureSession(InvisibleAI.Helper.Display.IPrivateResponseDisplay? display = null)
    {
        // Test executable only. No simulated providers or credentials ship in the helper.
        var x = Create(); x.Http.FixtureDelay = 500; x.Worker.FixtureDelay = 500; var store = new SettingsStore(Environment.GetEnvironmentVariable("INVISIBLEAI_TEST_PROFILE") ?? Path.Combine(AppContext.BaseDirectory, "fixture-profile"));
        if (!File.Exists(store.Path)) { var settings = Config(); settings.ResponseSeconds = 2; store.Save(settings); }
        return new InvisibleAI.Helper.Session(x.Service, store, x.Vault, display);
    }
    private static string UserText(string body)
    {
        using var json = JsonDocument.Parse(body); var content = json.RootElement.GetProperty("messages")[1].GetProperty("content");
        return content.ValueKind == JsonValueKind.String ? content.GetString()! : content[0].GetProperty("text").GetString()!;
    }
    private static string FixtureAnswer(string text) => text.Contains("screenshot") ? Json(new { kind = "mcq", options = new[] { "A", "B", "C", "D" }, answers = new[] { "C" } }) : text.Contains("Write a Python") ? Json(new { kind = "code", content = "```python\ns = input(\"Enter string: \")\nprint(s[::-1])\n```" }) : text.Contains("programming languages") ? "A, C, D" : text.Contains("France") ? Json(new { kind = "mcq", options = new[] { "A", "B", "C", "D" }, answers = new[] { "C" }, explanation = "Paris is the capital of France." }) : text.Contains("Python") ? "A" : text.Contains("A-F") ? "F" : text.Contains("one sentence") ? "Binary search repeatedly halves a sorted search range." : "O(log n)";
    public static async Task SessionWorkflow()
    {
        var x = Create(); string directory = Path.Combine(Path.GetTempPath(), "InvisibleAI-session-test-" + Guid.NewGuid()); var store = new SettingsStore(directory);
        store.Save(new AppSettings { PrivateResponses = false }); // Exercise the explicitly selected browser mode.
        var session = new InvisibleAI.Helper.Session(x.Service, store, x.Vault);
        async Task<Message> Ask(string type, object? payload = null) => await session.HandleAsync(Message.Create(type, Guid.NewGuid().ToString(), payload), default);
        try
        {
            foreach (string provider in new[] { Providers.Groq, Providers.Gemini, Providers.Groq })
            {
                var response = await Ask("CONNECT", new { provider, credential = provider == Providers.Groq ? Key : Cookie, responseSeconds = 2 });
                Safe(Json(response)); Check(response.Type == "CONNECTED", "Connect failed.");
                Safe(Json(await Ask("PING")));
                var result = await Ask("TEXT_INPUT", new { text = "synthetic question" }); Safe(Json(result));
                Check(result.Payload!.Value.GetProperty("result").GetProperty("provider").GetString() == Providers.Id(provider), "Helper routing failed.");
            }
            await Rejected(() => Ask("CONNECT", new { provider = Providers.Groq, userId = "user-b" }));
            await Rejected(() => Ask("PING", new { credential = "request-read" }));
            await Rejected(() => Ask("TEXT_INPUT", new { text = "question", provider = Providers.Gemini }));
            string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhS8AAAAASUVORK5CYII=";
            await Ask("SETTINGS_UPDATE", new { screenshotEnabled = true, answerMode = "Detailed", programmingLanguage = "Python", responseSeconds = 22, responseOpacity = .94 });
            Check(store.Load().ResponseMode == ResponseMode.DETAILED && store.Load().ProgrammingLanguage == "Python", "New preferences lost.");
            await Rejected(() => Ask("SCREENSHOT_INPUT", new { imageBase64 = png })); // Current Groq text model.
            foreach (string provider in new[] { Providers.Groq, Providers.Gemini })
            {
                await Ask("CONNECT", new { provider, model = provider == Providers.Groq ? VisionModel : GeminiModel });
                var imageResult = await Ask("SCREENSHOT_INPUT", new { imageBase64 = png }); Safe(Json(imageResult));
                Check(imageResult.Payload!.Value.GetProperty("result").GetProperty("content").GetString() == "C", "Image generation did not normalize option evidence.");
            }
            await Rejected(() => Ask("SCREENSHOT_INPUT", new { imageBase64 = "not-an-image" }));
            await Rejected(() => Ask("SCREENSHOT_INPUT", new { imageBase64 = png, provider = Providers.Groq }));
            await Ask("SETTINGS_UPDATE", new { networkEnabled = false, clipboardEnabled = false });
            Check(!store.Load().NetworkEnabled && !store.Load().ClipboardEnabled, "Privacy preferences lost.");
            await Rejected(() => Ask("TEXT_INPUT", new { text = "private" }));
            await Rejected(() => Ask("SETTINGS_UPDATE", new { userId = "user-b" }));
            Safe(File.ReadAllText(store.Path));
        }
        finally { Directory.Delete(directory, true); }
    }
    public static readonly (string Name, Func<Task> Run)[] All = [
        ("Providers: explicit selection switches both directions with isolated credentials", Switching),
        ("Groq: official text and multimodal requests, discovered models", GroqRequests),
        ("Gemini: Web2API cookies and messages, models and image forwarding", GeminiRequests),
        ("Providers: privacy gates prevent all credential and network access", Privacy),
        ("Providers: cancellation and missing credentials", CancellationAndMissing),
        ("Providers: invalid credentials, rate limits, unavailable models, safe errors", Errors),
        ("Providers: unified response and credential redaction", Unified),
        ("Provider API: POST-only connection results and catalogs contain no credentials", Api),
        ("Provider API: no user/credential retrieval route or user override", UserIsolation),
        ("Gemini: cookie normalization drops unrelated cookies and rejects malformed input", Cookies),
        ("Providers: no credentials in console output or persisted settings", NoLogs),
        ("Gemini: real private subprocess loads pinned library and sanitizes bad input", WorkerProcess)
    ];
    private const string Key = "synthetic-groq-sentinel";
    private const string Sid = "synthetic-gemini-sentinel";
    private const string Cookie = "__Secure-1PSID=" + Sid + "; __Secure-1PSIDTS=synthetic-ts-sentinel";
    private const string TextModel = "account-text-model";
    private const string VisionModel = "account-vision-model";
    private const string GeminiModel = "gemini-account-model";
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static AppSettings Config(string provider = Providers.Groq, string model = TextModel) => new() { Provider = provider, Model = model, GroqModel = TextModel, GeminiModel = GeminiModel, ScreenshotEnabled = true, PrivateResponses = false };
    private static string Json(object value) => JsonSerializer.Serialize(value, Message.Json);
    private static void Safe(string output) => Check(!output.Contains(Key) && !output.Contains(Sid) && !output.Contains("synthetic-ts-sentinel"), "Credential sentinel exposed.");
    private static async Task<string> Rejected(Func<Task> action)
    { try { await action(); } catch (AIProviderException e) { Safe(e.Message); return e.Message; } throw new Exception("Expected sanitized provider error."); }
    private static (AIService Service, Vault Vault, Transport Http, Worker Worker) Create()
    {
        var vault = new Vault(); var http = new Transport(); var worker = new Worker();
        return (new AIService(new GroqProvider(new HttpClient(http), vault), new GeminiWebProvider(worker, vault)), vault, http, worker);
    }
    private static async Task Switching()
    {
        var x = Create(); var settings = Config(Providers.Gemini, GeminiModel);
        Check((await x.Service.AskTextAsync("question", settings, default)).Provider == "gemini", "Wrong Gemini route.");
        Check(x.Http.Calls == 0 && x.Vault.Groq.Reads == 0, "Gemini accessed Groq credentials.");
        int geminiReads = x.Vault.Gemini.Reads, workerCalls = x.Worker.Calls;
        settings.Provider = Providers.Groq; settings.Model = TextModel;
        Check((await x.Service.AskTextAsync("question", settings, default)).Provider == "groq", "Wrong Groq route.");
        Check(x.Vault.Gemini.Reads == geminiReads && x.Worker.Calls == workerCalls, "Groq accessed Gemini credentials.");
        settings.Provider = Providers.Gemini; settings.Model = GeminiModel;
        Check((await x.Service.AskTextAsync("question", settings, default)).Provider == "gemini", "Return switch failed.");
        x.Worker.Code = "auth"; int calls = x.Http.Calls;
        await Rejected(() => x.Service.AskTextAsync("question", settings, default));
        Check(x.Http.Calls == calls, "Authentication failure caused fallback.");
        await Rejected(() => x.Service.AskTextAsync("question", new AppSettings(), default));
    }
    private static async Task GroqRequests()
    {
        var x = Create(); var s = Config();
        var answer = await x.Service.AskTextAsync("binary search?", s, default);
        Check(answer.Text == "O(log n)" && x.Http.Calls == 2, "Text/catalog request failed.");
        using var request = JsonDocument.Parse(x.Http.Body!);
        Check(request.RootElement.GetProperty("model").GetString() == TextModel, "Configured model lost.");
        Check(!request.RootElement.TryGetProperty("compound_custom", out _), "Unnecessary search enabled.");
        Check(x.Http.Url == "https://api.groq.com/openai/v1/chat/completions" && x.Http.Auth == "Bearer " + Key, "Unofficial endpoint/auth.");
        s.Model = VisionModel;
        await x.Service.AskImageAsync([137,80,78,71,13,10,26,10], s, default);
        using var image = JsonDocument.Parse(x.Http.Body!);
        Check(image.RootElement.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString()!.StartsWith("data:image/png;base64,"), "Image not inline.");
        s.Model = TextModel; await Rejected(() => x.Service.AskImageAsync([1], s, default));
        s.Model = "missing-model"; await Rejected(() => x.Service.AskTextAsync("question", s, default));
        s.Model = TextModel; s.WebSearch = true; await Rejected(() => x.Service.AskTextAsync("question", s, default));
        s.Model = "openai/gpt-oss-20b"; await x.Service.AskTextAsync("current information?", s, default);
        using var search = JsonDocument.Parse(x.Http.Body!);
        Check(search.RootElement.GetProperty("tools")[0].GetProperty("type").GetString() == "browser_search" && search.RootElement.GetProperty("tool_choice").GetString() == "auto", "Supported search not automatic.");
    }
    private static async Task GeminiRequests()
    {
        var x = Create(); var s = Config(Providers.Gemini, GeminiModel);
        Check((await x.Service.GetModelsAsync(s, default))[0].Id == GeminiModel, "Dynamic catalog lost.");
        await x.Service.AskMultimodalAsync("diagram", [137,80,78,71,13,10,26,10], s, default);
        using var request = JsonDocument.Parse(x.Worker.Request!); var root = request.RootElement;
        Check(root.GetProperty("cookies").GetProperty("__Secure-1PSID").GetString() == Sid, "Cookie not passed to private worker.");
        Check(!x.Worker.Request!.Contains(Key), "Groq key sent to Gemini.");
        Check(root.GetProperty("messages")[0].GetProperty("role").GetString() == "system" && root.GetProperty("messages")[1].GetProperty("content").GetString() == "diagram", "Messages interface broken.");
        Check(root.GetProperty("imageBase64").GetString()!.Length > 0, "Image missing.");
        x.Worker.NoVision = true;
        Check((await Rejected(() => x.Service.AskImageAsync([137,80,78,71,13,10,26,10], s, default))).Contains("does not support images"), "Gemini capability rejection missing.");
    }
    private static async Task Privacy()
    {
        foreach (string provider in new[] { Providers.Gemini, Providers.Groq })
        foreach (string gate in new[] { "enabled", "network", "text", "image" })
        {
            var x = Create(); var s = Config(provider, provider == Providers.Groq ? TextModel : GeminiModel);
            if (gate == "enabled") s.Enabled = false;
            if (gate == "network") s.NetworkEnabled = false;
            if (gate == "text") s.ClipboardEnabled = false;
            if (gate == "image") s.ScreenshotEnabled = false;
            await Rejected(() => gate == "image" ? x.Service.AskImageAsync([1], s, default) : x.Service.AskTextAsync("private", s, default));
            Check(x.Http.Calls + x.Worker.Calls + x.Vault.Groq.Reads + x.Vault.Gemini.Reads == 0, "Privacy gate read credentials or accessed network.");
        }
    }
    private static async Task CancellationAndMissing()
    {
        var x = Create(); x.Http.Wait = true; using var ct = new CancellationTokenSource(50);
        try { await x.Service.AskTextAsync("cancel", Config(), ct.Token); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
        foreach (string name in new[] { Providers.Groq, Providers.Gemini })
        { var y = Create(); y.Vault.For(name).Delete(); Check((await Rejected(() => y.Service.AskTextAsync("question", Config(name, name == Providers.Groq ? TextModel : GeminiModel), default))).StartsWith("Missing"), "Missing credential error lost."); }
    }
    private static async Task Errors()
    {
        foreach (var (status, expected) in new[] { (HttpStatusCode.Unauthorized, "Invalid Groq API key"), (HttpStatusCode.TooManyRequests, "rate limit"), (HttpStatusCode.ServiceUnavailable, "unavailable"), (HttpStatusCode.NotFound, "model unavailable") })
        { var x = Create(); x.Http.Status = status; Check((await Rejected(() => x.Service.AskTextAsync("question", Config(), default))).Contains(expected), "Wrong Groq status error."); }
        foreach (var (code, expected) in new[] { ("auth", "Gemini session expired"), ("rate_limit", "rate limit"), ("timeout", "timed out"), ("model", "model unavailable"), ("unavailable", "unavailable") })
        { var x = Create(); x.Worker.Code = code; Check((await Rejected(() => x.Service.AskTextAsync("question", Config(Providers.Gemini, GeminiModel), default))).Contains(expected), "Wrong Gemini status error."); }
    }
    private static async Task Unified()
    {
        var x = Create(); x.Http.Echo = true; x.Worker.Echo = true;
        foreach (var s in new[] { Config(), Config(Providers.Gemini, GeminiModel) })
        {
            var answer = await x.Service.AskTextAsync("question", s, default); string output = Json(answer.ToUnifiedResponse()); Safe(output);
            using var json = JsonDocument.Parse(output);
            foreach (string field in new[] { "content", "provider", "model", "usage", "finishReason" }) Check(json.RootElement.TryGetProperty(field, out _), "Unified field missing.");
        }
        using var malformed = JsonDocument.Parse("{\"choices\":[]}");
        await Rejected(() => Task.FromResult(GroqProvider.ParseAnswer(malformed.RootElement, TextModel, 100, Key)));
        using var longAnswer = JsonDocument.Parse(Json(new { choices = new[] { new { message = new { content = new string('a', 100) }, finish_reason = "stop" } } }));
        Check(GroqProvider.ParseAnswer(longAnswer.RootElement, TextModel, 16, Key).Text.Length == 16, "Answer not bounded.");
        Check(AIInstructions.Build(new AppSettings { ResponseMode = ResponseMode.SHORT }).Contains("do not guess"), "Ambiguity rule missing.");
    }
    private static async Task Api()
    {
        var x = Create(); var api = new ProviderApi(x.Service);
        foreach (string id in new[] { "gemini", "groq" })
        {
            string result = Json(await api.InvokeAsync("POST", $"/api/ai/providers/{id}/test", Config(), default)); Safe(result);
            using var json = JsonDocument.Parse(result);
            Check(json.RootElement.GetProperty("connected").GetBoolean() && json.RootElement.GetProperty("provider").GetString() == id, "Invalid connection DTO.");
            Check(json.RootElement.EnumerateObject().Count() == 2, "Connection response included extra fields.");
            Safe(Json(await api.InvokeAsync("POST", $"/api/ai/providers/{id}/models", Config(), default)));
        }
        x.Worker.Code = "auth";
        Safe(Json(await api.InvokeAsync("POST", "/api/ai/providers/gemini/test", Config(), default)));
        await Rejected(() => api.InvokeAsync("GET", "/api/ai/providers/gemini/test", Config(), default));
    }
    private static async Task UserIsolation()
    {
        var x = Create(); var api = new ProviderApi(x.Service);
        foreach (string path in new[] { "/api/ai/providers/gemini/credentials", "/api/users/user-b/providers/gemini/test", "/api/ai/providers/user-b/test", "/api/ai/providers/groq/test?user=user-b" })
            await Rejected(() => api.InvokeAsync("POST", path, Config(), default));
        // Actual multi-account isolation is provided by Windows CurrentUserOnly and CredRead;
        // there is no client-supplied user identity or credential-target field in this API.
        Check(ExtensionIdentity.Origin.StartsWith("chrome-extension://"), "Extension origin missing.");
        bool rejected = false; try { new ProviderCredentials().For("user-b/gemini"); } catch (AIProviderException) { rejected = true; }
        Check(rejected, "Arbitrary credential target accepted.");
    }
    private static Task Cookies()
    {
        Check(GeminiCookies.Normalize("Cookie: unrelated=discard; " + Cookie) == Cookie, "Unrelated cookies persisted.");
        foreach (string input in new[] { "", "__Secure-1PSID=abc\r\ninject", "__Secure-1PSID=one; __Secure-1PSID=two", "other=value" })
        { bool rejected = false; try { GeminiCookies.Parse(input); } catch (AIProviderException) { rejected = true; } Check(rejected, "Bad cookie accepted."); }
        return Task.CompletedTask;
    }
    private static async Task NoLogs()
    {
        var x = Create(); var stdout = Console.Out; var stderr = Console.Error; using var output = new StringWriter();
        try
        { Console.SetOut(output); Console.SetError(output); await x.Service.AskTextAsync("question", Config(), default); await x.Service.AskTextAsync("question", Config(Providers.Gemini, GeminiModel), default); x.Worker.Code = "auth"; await x.Service.TestConnectionAsync(Config(Providers.Gemini, GeminiModel), default); }
        finally { Console.SetOut(stdout); Console.SetError(stderr); }
        Safe(output.ToString()); Check(output.ToString() == "", "Providers wrote logs."); Safe(Json(Config()));
    }
    private static async Task WorkerProcess()
    {
        // This loads the actual Python package without making network requests or reading a browser.
        var settings = Config(Providers.Gemini, GeminiModel);
        settings.GeminiPythonPath = Environment.GetEnvironmentVariable("INVISIBLEAI_GEMINI_PYTHON") ?? "python";
        using var result = await new GeminiWorkerProcess().InvokeAsync(new { operation = "models", cookies = new { } }, settings, default);
        Check(result.RootElement.GetProperty("errorCode").GetString() == "auth", "Pinned worker runtime unavailable or bad input unsanitized.");
        Safe(result.RootElement.GetRawText());
    }
    private sealed class Vault : IProviderCredentials
    {
        public readonly Secret Groq = new(Key), Gemini = new(Cookie);
        public ICredentialStore For(string provider) => provider switch { Providers.Groq => Groq, Providers.Gemini => Gemini, _ => throw new ArgumentException("Choose a provider.") };
    }
    private sealed class Secret(string? value) : ICredentialStore
    { public int Reads; public string? Read() { Reads++; return value; } public void Write(string secret) => value = secret; public void Delete() => value = null; }
    private sealed class Worker : IGeminiWorker
    {
        public int Calls; public int FixtureDelay; public string? Request, Code; public bool Echo, NoVision;
        public async Task<JsonDocument> InvokeAsync(object request, AppSettings settings, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Calls++; Request = Json(request); if (FixtureDelay > 0 && !Request.Contains("\"models\"")) await Task.Delay(FixtureDelay, ct); return JsonDocument.Parse(Code is not null ? Json(new { errorCode = Code, raw = Cookie }) : Request.Contains("\"models\"") ? Json(new { models = new[] { new { id = GeminiModel, name = "Account Gemini", supportsImages = !NoVision } } }) : Json(new { content = Echo ? Cookie : FixtureAnswer(JsonDocument.Parse(Request).RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!) })); }
    }
    private sealed class Transport : HttpMessageHandler
    {
        public int Calls; public int FixtureDelay; public string? Body, Url, Auth; public bool Wait, Echo;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Url = request.RequestUri!.AbsoluteUri; Auth = request.Headers.Authorization?.ToString();
            Check(!Auth!.Contains(Sid) && !Url.Contains(Key), "Credential mixed or placed in URL.");
            if (Wait) await Task.Delay(Timeout.Infinite, ct);
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            if (Body is not null) { Safe(Body); if (FixtureDelay > 0) await Task.Delay(FixtureDelay, ct); }
            string response = Status != HttpStatusCode.OK ? Key + Cookie : request.Method == HttpMethod.Get ? Json(new { data = new[] { new { id = TextModel, active = true, supports_vision = false }, new { id = VisionModel, active = true, supports_vision = true }, new { id = "openai/gpt-oss-20b", active = true, supports_vision = false } } }) : Json(new { choices = new[] { new { message = new { content = Echo ? Key : FixtureAnswer(UserText(Body!)) }, finish_reason = "stop" } }, usage = new { prompt_tokens = 1, completion_tokens = 2, total_tokens = 3 } });
            return new HttpResponseMessage(Status) { Content = new StringContent(response) };
        }
    }
}
