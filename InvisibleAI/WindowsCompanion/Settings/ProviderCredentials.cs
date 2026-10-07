using InvisibleAI.Companion.AI;

namespace InvisibleAI.Companion.Settings;

public interface IProviderCredentials { ICredentialStore For(string provider); }
public sealed class ProviderCredentials : IProviderCredentials
{
    public ICredentialStore For(string provider) => new CredentialStore("InvisibleAI/" + Providers.Id(provider));
}
public static class GeminiCookies
{
    // The maintained Web client needs these two cookies. Unrelated Google cookies are discarded before storage.
    public static Dictionary<string, string> Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 65536) throw new AIProviderException("Paste your own Gemini Cookie request header containing __Secure-1PSID.");
        if (input.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase)) input = input[7..];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in input.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int equal = pair.IndexOf('='); if (equal < 1) continue;
            string name = pair[..equal].Trim(), value = pair[(equal + 1)..].Trim();
            if (name is not ("__Secure-1PSID" or "__Secure-1PSIDTS")) continue;
            if (values.ContainsKey(name) || value.Length is < 1 or > 900 || value.Any(c => c <= 32 || c >= 127 || c is '"' or ','))
                throw new AIProviderException("Invalid Gemini cookie format. Paste the Cookie request header from your own account.");
            values[name] = value;
        }
        if (!values.ContainsKey("__Secure-1PSID")) throw new AIProviderException("Missing Gemini cookies. Paste a Cookie header containing __Secure-1PSID.");
        return values;
    }
    public static string Normalize(string input) => string.Join("; ", Parse(input).Select(pair => pair.Key + "=" + pair.Value));
}
public static class CredentialRedaction
{
    public static string Scrub(string text, string secret, bool cookie = false)
    {
        text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        if (cookie) foreach (string value in GeminiCookies.Parse(secret).Values) text = text.Replace(value, "[redacted]", StringComparison.Ordinal);
        return text;
    }
}
