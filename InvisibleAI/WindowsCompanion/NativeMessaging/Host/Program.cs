using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InvisibleAI.Shared.Protocol;

namespace InvisibleAI.NativeHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // stdout is ONLY framed JSON. Credentials and user content are never logged.
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        using var lifetime = new CancellationTokenSource();
        try
        {
            string? origin = args.FirstOrDefault(a => a.StartsWith("chrome-extension://", StringComparison.Ordinal));
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "com.invisibleai.assistant.json")));
            if (origin is null || !manifest.RootElement.GetProperty("allowed_origins").EnumerateArray().Any(v => v.GetString() == origin))
                throw new ProtocolException("This extension origin is not registered. Run Install.ps1 with its extension ID.");
            using var pipe = new NamedPipeClientStream(".", Identity.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.ConnectAsync(500, lifetime.Token); }
            catch (TimeoutException)
            {
                string executable = Path.Combine(AppContext.BaseDirectory, "InvisibleAI.Companion.exe");
                if (!File.Exists(executable)) throw new ProtocolException("Windows companion is missing. Reinstall the published package.");
                Process.Start(new ProcessStartInfo(executable, "--background") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
                await pipe.ConnectAsync(10000, lifetime.Token);
            }
            async Task Pump(Stream from, Stream to, int cap)
            {
                try
                {
                    while (await Framing.ReadAsync(from, lifetime.Token, cap) is { } message)
                        await Framing.WriteAsync(to, message, lifetime.Token, cap);
                }
                finally { lifetime.Cancel(); }
            }
            var uploads = Pump(input, pipe, Framing.MaxRequestBytes);
            var downloads = Pump(pipe, output, Framing.MaxResponseBytes);
            await Task.WhenAny(uploads, downloads);
            lifetime.Cancel();
            pipe.Dispose();
            // Disposing stdin interrupts an outstanding read after the browser disconnects.
            input.Dispose();
            try { await Task.WhenAll(uploads, downloads); } catch (OperationCanceledException) { }
            return 0;
        }
        catch (Exception)
        {
            try { await Framing.WriteAsync(output, Message.Create("ERROR", "connection", new { message = "Native host connection failed. Check installation, extension ID, and companion availability." }), CancellationToken.None); } catch { }
            return 1;
        }
    }
}
