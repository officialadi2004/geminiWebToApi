using System.Diagnostics;
using System.Text;
using System.Text.Json;
using InvisibleAI.Helper.Settings;

namespace InvisibleAI.Helper.AI;

public interface IGeminiWorker
{ Task<JsonDocument> InvokeAsync(object request, AppSettings settings, CancellationToken ct); }
public sealed class GeminiWorkerProcess : IGeminiWorker
{
    public async Task<JsonDocument> InvokeAsync(object request, AppSettings settings, CancellationToken ct)
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "GeminiWorker", "GeminiWorker.exe");
        string worker = Path.Combine(AppContext.BaseDirectory, "AI", "GeminiWeb", "worker.py");
        if (!File.Exists(bundled) && !File.Exists(worker)) throw new AIProviderException("Gemini Web worker is missing. Reinstall the helper.");
        string localPython = Path.Combine(AppContext.BaseDirectory, "gemini-runtime", "Scripts", "python.exe");
        string python = !string.IsNullOrWhiteSpace(settings.GeminiPythonPath) ? settings.GeminiPythonPath : File.Exists(localPython) ? localPython : Environment.GetEnvironmentVariable("INVISIBLEAI_GEMINI_PYTHON") ?? "python";
        var info = new ProcessStartInfo(File.Exists(bundled) && string.IsNullOrEmpty(settings.GeminiPythonPath) ? bundled : python)
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
          StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        if (info.FileName != bundled) { info.ArgumentList.Add("-I"); info.ArgumentList.Add("-B"); info.ArgumentList.Add(worker); }
        // No secret in arguments, environment, disk files, URLs, browser messages, or stderr.
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        Process child;
        try { child = Process.Start(info) ?? throw new AIProviderException("Gemini worker could not start."); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException)
        { throw new AIProviderException("Gemini runtime missing. Reinstall Invisible AI Setup."); }
        using (child)
        {
            using var cancellation = timeout.Token.Register(() => { try { if (!child.HasExited) child.Kill(true); } catch (InvalidOperationException) { } });
            async Task<byte[]> ReadBounded(Stream stream, int limit)
            {
                using var data = new MemoryStream(); byte[] bytes = new byte[8192]; int n;
                while ((n = await stream.ReadAsync(bytes, timeout.Token)) > 0)
                { if (data.Length + n > limit) throw new AIProviderException("Gemini worker response exceeded its size limit."); data.Write(bytes, 0, n); }
                return data.ToArray();
            }
            // stderr is drained and discarded: never inspect or echo library traces.
            async Task Drain() { var buffer = new byte[4096]; while (await child.StandardError.BaseStream.ReadAsync(buffer, timeout.Token) > 0) { } }
            var output = ReadBounded(child.StandardOutput.BaseStream, 2 * 1024 * 1024);
            var errors = Drain();
            try
            {
                await child.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, Shared.Protocol.Message.Json).AsMemory(), timeout.Token);
                child.StandardInput.Close();
                await child.WaitForExitAsync(timeout.Token); await errors;
                byte[] bytes = await output;
                timeout.Token.ThrowIfCancellationRequested();
                return JsonDocument.Parse(bytes);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AIProviderException("Gemini request timed out. Try again."); }
            catch (Exception e) when (e is JsonException or IOException or InvalidOperationException)
            { if (e is AIProviderException) throw; ct.ThrowIfCancellationRequested(); throw new AIProviderException("Gemini Web worker unavailable. Check its runtime installation."); }
            finally
            {
                try { if (!child.HasExited) child.Kill(true); } catch (InvalidOperationException) { }
                try { await Task.WhenAll(output, errors); } catch (Exception) { }
            }
        }
    }
}
public sealed class GeminiWebProvider(IGeminiWorker worker, IProviderCredentials credentials) : IAIProvider
{
    public string Name => Providers.Gemini;
    private string Cookie() => credentials.For(Name).Read() ?? throw new AIProviderException("Missing Gemini cookies. Add your own Gemini cookies in Settings → AI.");
    public async Task<IReadOnlyList<AIModel>> GetModelsAsync(AppSettings settings, CancellationToken ct)
    {
        string cookie = Cookie(); var values = GeminiCookies.Parse(cookie);
        using var json = await worker.InvokeAsync(new { operation = "models", cookies = values, timeout = settings.RequestTimeoutSeconds }, settings, ct);
        Check(json.RootElement);
        var result = new List<AIModel>();
        foreach (var model in json.RootElement.GetProperty("models").EnumerateArray())
        {
            string id = model.GetProperty("id").GetString() ?? "";
            if (!Providers.ValidModelId(id) || values.Values.Any(v => id.Contains(v, StringComparison.Ordinal))) continue;
            string name = model.TryGetProperty("name", out var value) ? value.GetString() ?? id : id;
            result.Add(new(id, AIInstructions.Limit(CredentialRedaction.Scrub(name, cookie, true), 120), true, true));
        }
        return result;
    }
    public async Task<AIAnswer> GenerateAsync(string? text, byte[]? image, AppSettings settings, CancellationToken ct)
    {
        string cookie = Cookie();
        using var json = await worker.InvokeAsync(new { operation = "generate", cookies = GeminiCookies.Parse(cookie), model = settings.Model,
            messages = new object[] { new { role = "system", content = AIInstructions.Build(settings) }, new { role = "user", content = text ?? "Analyze this screenshot. Answer its question or describe it briefly." } },
            imageBase64 = image is null ? null : Convert.ToBase64String(image), timeout = settings.RequestTimeoutSeconds }, settings, ct);
        Check(json.RootElement);
        string result = json.RootElement.GetProperty("content").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(result)) throw new AIProviderException("Gemini returned no answer. Try again.");
        return new(AIInstructions.Limit(CredentialRedaction.Scrub(result, cookie, true), settings.MaxResponseLength), [], "gemini", settings.Model, null, "stop");
    }
    private static void Check(JsonElement root)
    {
        if (!root.TryGetProperty("errorCode", out var error)) return;
        throw new AIProviderException(error.GetString() switch
        {
            "auth" => "Gemini session expired. Please update your Gemini cookies.",
            "rate_limit" => "Gemini rate limit reached. Wait before trying again.",
            "timeout" => "Gemini request timed out. Try again.",
            "model" => "Gemini model unavailable. Refresh models and choose an available model.",
            "dependency" => "Gemini runtime missing. Reinstall Invisible AI Setup.",
            "format" => "Invalid Gemini cookie or request format.",
            _ => "Gemini Web is unavailable. Check your connection and try again."
        });
    }
}
