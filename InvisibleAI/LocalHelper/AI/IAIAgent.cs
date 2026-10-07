using InvisibleAI.Helper.Settings;

namespace InvisibleAI.Helper.AI;

public sealed record AIAnswer(string Text, IReadOnlyList<AISource> Sources, string Provider = "", string Model = "", AIUsage? Usage = null, string FinishReason = "stop")
{
    public object ToUnifiedResponse() => new { content = Text, provider = Provider, model = Model, usage = Usage, finishReason = FinishReason };
}
public sealed record AISource(string Title, string Url);
public interface IAIAgent
{
    Task<AIAnswer> AskTextAsync(string text, AppSettings settings, CancellationToken ct);
    Task<AIAnswer> AskImageAsync(byte[] image, AppSettings settings, CancellationToken ct);
    Task<AIAnswer> AskMultimodalAsync(string? text, byte[]? image, AppSettings settings, CancellationToken ct);
}
