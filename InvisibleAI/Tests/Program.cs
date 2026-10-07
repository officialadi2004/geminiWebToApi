using System;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InvisibleAI.Helper.AI;
using InvisibleAI.Helper.Settings;
using InvisibleAI.Shared.Protocol;
namespace InvisibleAI.Tests;
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--native-fixture") || args.Contains(ExtensionIdentity.Origin)) {
            if (!args.Contains(ExtensionIdentity.Origin)) return 1;
            await InvisibleAI.Helper.Program.RunAsync(ProviderTests.FixtureSession(), Console.OpenStandardInput(), Console.OpenStandardOutput()); return 0;
        }
        int failed = 0;
        var tests = new List<(string Name, Func<Task> Run)>(ProviderTests.All) { ("Upgrade: retire only the installed companion and its own startup command", LegacyPaths), ("Framing: Unicode and fragmentation", FramingRoundTrip), ("Framing: invalid input", FramingInvalid), ("MCQ: arbitrary labels, true/false, ambiguity and concise text", Answers), ("Settings: persistence and validation", Settings), ("Windows: isolated Credential Manager round trip", Credentials), ("Helper: write-only credentials, routing and caller field rejection", ProviderTests.SessionWorkflow) };
        tests.AddRange(UpgradeTests.All);
        foreach (var test in tests) try { await test.Run(); Console.WriteLine("PASS " + test.Name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + e.Message); }
        Console.WriteLine($"{tests.Count - failed} passed; {failed} failed."); return failed == 0 ? 0 : 1;
    }
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static Task LegacyPaths()
    {
        string expected = @"C:\Users\Test User\AppData\Local\InvisibleAI\app\InvisibleAI.Companion.exe";
        foreach (string command in new[] { "\"" + expected + "\" --background", expected + " --background", expected.ToUpperInvariant() })
            Assert(InvisibleAI.Setup.LegacyUpgrade.MatchesStartup(command, expected), "Installed companion startup not recognized.");
        foreach (string command in new[] { "\"C:/Other/InvisibleAI.Companion.exe\" --background", expected + ".backup", "\"C:/Other/tool.exe\" \"" + expected + "\"", "", "\"" + expected })
            Assert(!InvisibleAI.Setup.LegacyUpgrade.MatchesStartup(command, expected), "Unrelated startup command would be removed.");
        Assert(!InvisibleAI.Setup.LegacyUpgrade.MatchesExecutable(expected.Replace("Companion", "Helper"), expected), "New helper would be stopped.");
        return Task.CompletedTask;
    }
    private static Task Answers()
    {
        foreach (var (question, answer, expected) in new[] {
            ("Binary search?\nA. O(n)\nB. O(n2)\nC. O(log n)\nD. O(1)", "The correct answer is C because...", "C"),
            ("Choose\nA) one\nB) two\nC) three\nD) four\nE) five\nF) six", "**F**", "F"),
            ("Python dynamic?\nA. True\nB. False", "A", "A"),
            ("Choose\nA\ntrue\nB\nfalse", "B", "B"),
            ("Choose A. one B. two C. three D. four E. five F. six G. seven", "G", "G"),
            ("Choose\nA. true\nB. false", "Z", "Uncertain"),
            ("Choose\nA. true\nB. false", "I cannot determine the answer", "Uncertain"),
            ("Compare these protocols:\nTCP\nUDP", "TCP is reliable; UDP has lower overhead.", "TCP is reliable; UDP has lower overhead."),
            ("Explain binary search in one sentence.", "Halve the search range repeatedly.", "Halve the search range repeatedly.") })
            Assert(AnswerPolicy.Normalize(question, answer) == expected, "Unexpected answer policy result.");
        return Task.CompletedTask;
    }
    private static Task Settings()
    {
        string directory = Path.Combine(Path.GetTempPath(), "InvisibleAI-tests-" + Guid.NewGuid()); var store = new SettingsStore(directory);
        try {
            new AppSettings().Validate(); var s = new AppSettings { Provider = Providers.Groq, Model = "test-model", ResponseSeconds = 2 }; store.Save(s);
            Assert(store.Load().Model == "test-model", "Settings lost.");
            Assert(!File.ReadAllText(store.Path).Contains("credential", StringComparison.OrdinalIgnoreCase), "Credential persisted.");
            Assert(Directory.GetFiles(directory).Length == 1, "Temporary file remains.");
            foreach (var value in new[] { 0, 121 }) { s.ResponseSeconds = value; bool rejected = false; try { s.Validate(); } catch (ArgumentException) { rejected = true; } Assert(rejected, "Invalid duration accepted."); }
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return Task.CompletedTask;
    }
    private static Task Credentials()
    {
        var store = new CredentialStore("InvisibleAI/test/" + Guid.NewGuid());
        try { store.Write("synthetic-test-only"); Assert(store.Read() == "synthetic-test-only", "Windows credential lost."); store.Delete(); Assert(store.Read() is null, "Credential not deleted."); }
        finally { store.Delete(); }
        return Task.CompletedTask;
    }
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
    private sealed class FragmentedStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], ct);
    }
}
