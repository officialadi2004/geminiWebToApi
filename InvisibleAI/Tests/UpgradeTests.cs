using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InvisibleAI.Helper.AI;
using InvisibleAI.Helper.Settings;
using InvisibleAI.Shared.Protocol;

namespace InvisibleAI.Tests;
internal static class UpgradeTests
{
    public static readonly (string Name, Func<Task> Run)[] All = [
        ("Answers: A-Z, multiple labels, ambiguity and code options", Labels),
        ("Answers: structured image MCQ, True/False, multiple labels, code and unreadable", Images),
        ("Answers: Detailed explanations, Quick suppression and code whitespace", Details),
        ("Preferences: 22-second default, custom duration, privacy-preserving migration", Preferences),
        ("Images: PNG format, dimensions and allocation limits", Limits)
    ];
    private static void Check(bool ok) { if (!ok) throw new Exception("Upgrade regression failed."); }
    private static AIAnswer Process(string? text, object data, bool image = false, ResponseMode mode = ResponseMode.CONCISE) =>
        AnswerPolicy.Process(text, new(JsonSerializer.Serialize(data, Message.Json), []), new() { ResponseMode = mode }, image);
    private static Task Labels()
    {
        string q = "Which are languages?\nA. Python\nB. Photoshop\nC. Java\nD. C++\nE. Excel";
        foreach (var raw in new[] { "A, C, D", "Answer: D, A, C", "The correct answers are A and C and D." }) Check(AnswerPolicy.Normalize(q, raw) == "A, C, D");
        foreach (var raw in new[] { "C, Z", "C or D", "C / D", "Maybe C", "D, A, C, Z", "It could be A" }) Check(AnswerPolicy.Normalize(q, raw) == "Uncertain");
        string alphabet = string.Join("\n", Enumerable.Range('A', 26).Select(c => $"{(char)c}. option"));
        Check(AnswerPolicy.Normalize(alphabet, "Z, A, G") == "A, G, Z");
        Check(AnswerPolicy.Normalize("Choose\nD. first\nA. second\nF. third", "A, F, D") == "D, A, F");
        Check(AnswerPolicy.Normalize("Output?\n```c\nint x=5;\nprintf(\"%d\", x++);\n```\nA. 4\nB. 5\nC. 6\nD. Error", "B") == "B");
        Check(AnswerPolicy.Labels("Explain this code\n```java\nA. anything\nB. anything\n```").Length == 0);
        return Task.CompletedTask;
    }
    private static Task Images()
    {
        foreach (var selected in new[] { new[] { "C" }, new[] { "B" }, new[] { "D", "A", "C" } })
            Check(Process(null, new { kind = "mcq", options = new[] { "A", "B", "C", "D" }, answers = selected }, true).Text == string.Join(", ", new[] { "A", "B", "C", "D" }.Where(selected.Contains)));
        Check(Process(null, new { kind = "mcq", options = new[] { "A", "B" }, answers = new[] { "Z" } }, true).Text == "Uncertain");
        Check(Process(null, new { kind = "mcq", options = new[] { "A" }, answers = new[] { "A" } }, true).Text == "Uncertain");
        Check(Process(null, new { kind = "mcq", options = new object[] { "A", 42, "B" }, answers = new[] { "A" } }, true).Text == "Uncertain");
        Check(Process(null, new { kind = "uncertain" }, true).Text == "Uncertain");
        Check(Process(null, new { kind = "answer", content = "x = 4" }, true).Text == "x = 4");
        Check(Process(null, new { kind = "code", content = "```python\nprint(4)\n```" }, true).Text.Contains("print(4)"));
        Check(AnswerPolicy.Process(null, new("C", []), new(), true).Text == "Uncertain");
        Check(AnswerPolicy.Process(null, new("The correct answer is C.", []), new(), true).Text == "Uncertain");
        Check(AnswerPolicy.Process("Choose\nA. one\nB. two", new("B", []), new(), true).Text == "B");
        return Task.CompletedTask;
    }
    private static Task Details()
    {
        var data = new { kind = "mcq", options = new[] { "A", "B", "C" }, answers = new[] { "C" }, explanation = "Halves the search space." };
        string q = "Complexity?\nA. O(n)\nB. O(1)\nC. O(log n)";
        Check(Process(q, data, mode: ResponseMode.DETAILED).Details == data.explanation);
        Check(Process(q, data).Details is null);
        Check(Process("Question\nA. first\nB. second", data).Text == "Uncertain"); // Provider cannot invent C.
        string code = "```python\ns = input(\"String: \")\n\tprint(s[::-1])\n```";
        Check(Process("Write code", new { kind = "code", content = code }).Text == code);
        Check(AIInstructions.Build(new() { ProgrammingLanguage = "C++", ResponseMode = ResponseMode.DETAILED }).Contains("Language: C++"));
        return Task.CompletedTask;
    }
    private static Task Preferences()
    {
        var s = new AppSettings(); Check(s.ResponseSeconds == 22 && s.ResponseOpacity == .94 && s.ScreenshotEnabled && s.ProgrammingLanguage == "Auto Detect");
        s.ResponseSeconds = 120; s.Validate();
        foreach (string language in AppSettings.Languages) { s.ProgrammingLanguage = language; s.Validate(); }
        string folder = Path.Combine(Path.GetTempPath(), "InvisibleAI-upgrade-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            var store = new SettingsStore(folder);
            File.WriteAllText(store.Path, "{\"responseSeconds\":12,\"screenshotEnabled\":false,\"networkEnabled\":false}");
            var loaded = store.Load(); Check(loaded.ResponseSeconds == 22 && !loaded.ScreenshotEnabled && !loaded.NetworkEnabled);
            store.Save(loaded); Check(store.Load().ResponseSeconds == 22);
            loaded.ResponseSeconds = 12; store.Save(loaded); Check(store.Load().ResponseSeconds == 12);
        }
        finally { Directory.Delete(folder, true); }
        return Task.CompletedTask;
    }
    private static Task Limits()
    {
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhS8AAAAASUVORK5CYII=");
        ImageInput.Validate(png);
        foreach (byte[] bytes in new[] { new byte[0], new byte[5 * 1024 * 1024 + 1], new byte[33] })
        { bool rejected = false; try { ImageInput.Validate(bytes); } catch (AIProviderException) { rejected = true; } Check(rejected); }
        png[16] = 0x7f; bool invalid = false; try { ImageInput.Validate(png); } catch (AIProviderException) { invalid = true; } Check(invalid);
        return Task.CompletedTask;
    }
}
