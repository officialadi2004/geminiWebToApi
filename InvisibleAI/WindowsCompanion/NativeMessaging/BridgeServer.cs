using System.IO.Pipes;
using System.Windows.Threading;
using InvisibleAI.Shared.Protocol;

namespace InvisibleAI.Companion.NativeMessaging;

public sealed class BridgeServer(Dispatcher dispatcher, Func<Message, Func<Message, Task>, CancellationToken, Task> handler) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim slots = new(8);
    private Task? listener;
    public void Start() => listener = ListenAsync(lifetime.Token);
    private async Task ListenAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await slots.WaitAsync(ct);
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(Identity.PipeName, PipeDirection.InOut, 8, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(ct);
                    _ = ServeAsync(pipe, ct); pipe = null;
                }
                catch { pipe?.Dispose(); slots.Release(); throw; }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* No recovery that bypasses Windows access restrictions. */ }
    }
    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken lifetimeToken)
    {
        using (pipe)
        using (var connection = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken))
        using (var writeLock = new SemaphoreSlim(1))
        {
            var tasks = new List<Task>();
            var outstandingIds = new HashSet<string>();
            var idLock = new object();
            async Task Send(Message message)
            {
                await writeLock.WaitAsync(connection.Token);
                try { await Framing.WriteAsync(pipe, message, connection.Token); }
                finally { writeLock.Release(); }
            }
            async Task Dispatch(Message message)
            {
                try { await dispatcher.InvokeAsync(() => handler(message, Send, connection.Token)).Task.Unwrap(); }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { connection.Cancel(); }
                finally { lock (idLock) outstandingIds.Remove(message.Id); }
            }
            try
            {
                while (await Framing.ReadAsync(pipe, connection.Token) is { } message)
                {
                    lock (idLock)
                    {
                        if (outstandingIds.Count >= 16 || !outstandingIds.Add(message.Id)) throw new ProtocolException("Too many requests or duplicate request ID.");
                    }
                    tasks.RemoveAll(t => t.IsCompleted);
                    tasks.Add(Dispatch(message));
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ProtocolException or System.Text.Json.JsonException) { }
            finally
            {
                connection.Cancel();
                try { await Task.WhenAll(tasks); } catch (Exception) { }
                slots.Release();
            }
        }
    }
    public void Dispose() { lifetime.Cancel(); /* Listener and connection tasks own their asynchronous lifetimes. */ }
}
