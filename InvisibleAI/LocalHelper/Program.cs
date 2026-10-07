using System.Net.Http;
using System.Collections.Concurrent;
using InvisibleAI.Helper.AI;
using InvisibleAI.Helper.Settings;
using InvisibleAI.Shared.Protocol;

namespace InvisibleAI.Helper;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Native Messaging supplies an origin argument. No generic local HTTP endpoint.
        if (args.Length == 0 || args[0] != ExtensionIdentity.Origin) return;
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var credentials = new ProviderCredentials();
        var service = new AIService(new GroqProvider(http, credentials), new GeminiWebProvider(new GeminiWorkerProcess(), credentials));
        await RunAsync(new Session(service, new SettingsStore(), credentials), Console.OpenStandardInput(), Console.OpenStandardOutput());
    }
    public static async Task RunAsync(Session session, Stream input, Stream output)
    {
        using var lifetime = new CancellationTokenSource();
        using var writes = new SemaphoreSlim(1, 1);
        var running = new ConcurrentDictionary<string, CancellationTokenSource>();
        var tasks = new ConcurrentDictionary<long, Task>();
        long sequence = 0;
        async Task Write(Message m)
        {
            await writes.WaitAsync(lifetime.Token);
            try { await Framing.WriteAsync(output, m, lifetime.Token); }
            finally { writes.Release(); }
        }
        async Task Dispatch(Message m, CancellationTokenSource request)
        {
            try
            {
                if (m.Type is "TEXT_INPUT" or "SCREENSHOT_INPUT") await Write(Message.Create("PROCESSING_START", m.Id));
                await Write(await session.HandleAsync(m, request.Token));
            }
            catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) await Write(Message.Create("ERROR", m.Id, new { message = "Request cancelled." })); }
            catch (AIProviderException e) { await Write(Message.Create("ERROR", m.Id, new { message = e.Message })); }
            catch (Exception) { if (!lifetime.IsCancellationRequested) await Write(Message.Create("ERROR", m.Id, new { message = "Could not connect to AI provider. Check settings and try again." })); }
            finally { running.TryRemove(m.Id, out _); request.Dispose(); }
        }
        try
        {
            while (await Framing.ReadAsync(input, lifetime.Token) is Message m)
            {
                if (m.Type == "CANCEL")
                {
                    if (m.Payload is { } p && p.TryGetProperty("targetId", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String && running.TryGetValue(id.GetString()!, out var request)) { try { request.Cancel(); } catch (ObjectDisposedException) { } }
                    continue;
                }
                if (running.Count >= 16 || running.ContainsKey(m.Id)) { await Write(Message.Create("ERROR", m.Id, new { message = "Too many requests." })); continue; }
                var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                cancel.CancelAfter(TimeSpan.FromSeconds(300));
                running[m.Id] = cancel;
                long taskId = ++sequence;
                var task = Dispatch(m, cancel); tasks[taskId] = task;
                // Drop completed state machines promptly rather than retaining request images until another message.
                _ = task.ContinueWith(completed => { tasks.TryRemove(taskId, out _); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception) { /* Never print protocol or upstream data to stdout/stderr. */ }
        finally { lifetime.Cancel(); foreach (var request in running.Values) { try { request.Cancel(); } catch (ObjectDisposedException) { } } try { await Task.WhenAll(tasks.Values); } catch (Exception) { } }
    }
}
