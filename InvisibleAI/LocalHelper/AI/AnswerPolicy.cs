using System.Text.RegularExpressions;

namespace InvisibleAI.Helper.AI;

public static class AnswerPolicy
{
    public static string[] Labels(string input)
    {
        var matches = Regex.Matches(input, @"(?m)(?:^\s*|(?<![\p{L}\p{N}_]))(?:(?<label>[A-Z]{1,3})[.)\]:]\s+|(?<label>[A-Z])\s*$)");
        var labels = matches.Select(m => m.Groups["label"].Value).Distinct().ToArray();
        return labels.Length >= 2 ? labels : [];
    }
    public static string Normalize(string input, string answer)
    {
        string[] labels = Labels(input);
        if (labels.Length == 0) return answer.Trim();
        string clean = answer.Trim().Trim('`', '*', '_').Trim();
        var result = Regex.Match(clean, @"\A(?:(?:the\s+correct\s+(?:answer|option)\s+is|answer|option)\s*:?\s*)?[\[(]?(?<label>[A-Z]{1,3})(?:[\])\.:]|\s|$)", RegexOptions.IgnoreCase);
        if (result.Success && labels.Contains(result.Groups["label"].Value.ToUpperInvariant())) return result.Groups["label"].Value.ToUpperInvariant();
        // Never pick a label just because it happens to be mentioned in an explanation.
        return "Uncertain";
    }
}
