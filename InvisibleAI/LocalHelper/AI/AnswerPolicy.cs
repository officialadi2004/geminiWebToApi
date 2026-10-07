using System.Text.Json;
using System.Text.RegularExpressions;
using InvisibleAI.Helper.Settings;

namespace InvisibleAI.Helper.AI;

public static class AnswerPolicy
{
    public static string[] Labels(string input)
    {
        string prose = Regex.Replace(input, @"(?s)```.*?(?:```|\z)", "");
        var matches = Regex.Matches(prose, @"(?m)(?<![\p{L}\p{N}_])(?<label>[A-Z])[.)\]:]\s+|^[ \t]*(?<label>[A-Z])[ \t]*$");
        var labels = matches.Select(m => m.Groups["label"].Value).Distinct().ToArray();
        return labels.Length >= 2 ? labels : [];
    }
    private static string[] Extract(string answer)
    {
        string clean = answer.Trim().Trim('`', '*', '_').Trim();
        if (Regex.IsMatch(clean, @"\b(?:uncertain|unsure|ambiguous|cannot determine|might|maybe|possibly|either|or)\b|[A-Z]\s*/\s*[A-Z]", RegexOptions.IgnoreCase)) return [];
        clean = Regex.Replace(clean, @"\A(?:(?:the\s+)?correct\s+(?:answers?|options?)(?:\s+(?:is|are))?|answers?|options?)\s*:?\s*", "", RegexOptions.IgnoreCase);
        var match = Regex.Match(clean, @"\A[\[(]?(?<labels>[A-Z](?:\s*(?:,|and|&)\s*[A-Z])*)[\])\.:]?(?=\s|$)", RegexOptions.IgnoreCase);
        if (!match.Success) return [];
        return Regex.Matches(match.Groups["labels"].Value, @"\b[A-Z]\b", RegexOptions.IgnoreCase).Select(m => m.Value.ToUpperInvariant()).Distinct().ToArray();
    }
    private static string Ordered(string[] available, string[] answers) =>
        answers.Length == 0 || answers.Any(a => !available.Contains(a)) ? "Uncertain" : string.Join(", ", available.Where(answers.Contains));
    public static string Normalize(string input, string answer)
    {
        string[] labels = Labels(input);
        return labels.Length == 0 ? answer.Trim() : Ordered(labels, Extract(answer));
    }
    public static AIAnswer Process(string? input, AIAnswer answer, AppSettings settings, bool image)
    {
        string raw = answer.Text.Trim();
        string[] known = Labels(input ?? "");
        string json = Regex.Replace(raw, @"\A```(?:json)?\s*|\s*```\z", "", RegexOptions.IgnoreCase);
        if (json.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string Read(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
                string[] Array(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array
                    ? value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : "").ToArray() : [];
                string kind = Read("kind");
                if (kind == "uncertain") return answer with { Text = "Uncertain", Details = null };
                if (kind == "mcq" || known.Length > 0)
                {
                    // Image options are read by the multimodal model; text options are verified locally.
                    string[] available = known.Length > 0 ? known : image ? Array("options").Distinct().ToArray() : [];
                    string[] selected = Array("answers").Distinct().ToArray();
                    string normalized = available.Length >= 2 && available.All(x => Regex.IsMatch(x, @"\A[A-Z]\z")) ? Ordered(available, selected) : "Uncertain";
                    string details = Read("explanation").Trim();
                    return answer with { Text = normalized, Details = normalized != "Uncertain" && settings.ResponseMode == ResponseMode.DETAILED && details.Length > 0 ? details : null };
                }
                if (kind is "answer" or "code")
                {
                    string content = Read("content").Trim();
                    return answer with { Text = content.Length > 0 ? content : "Uncertain", Details = null };
                }
                return answer with { Text = "Uncertain", Details = null };
            }
            catch (JsonException) { return answer with { Text = "Uncertain", Details = null }; }
        }
        // Compatibility with models returning plain text. A naked image label has no option evidence.
        if (image && known.Length == 0 && (Regex.IsMatch(raw, @"\A(?:[A-Z](?:,\s*)?)+\z") || Regex.IsMatch(raw, @"\A(?:(?:the\s+)?correct\s+(?:answers?|options?)|answers?\s*:|options?\s*:)", RegexOptions.IgnoreCase)))
            return answer with { Text = "Uncertain", Details = null };
        return answer with { Text = input is null ? raw : Normalize(input, raw), Details = null };
    }
}
