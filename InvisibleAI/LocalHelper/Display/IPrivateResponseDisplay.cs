using InvisibleAI.Helper.AI;
using InvisibleAI.Helper.Settings;

namespace InvisibleAI.Helper.Display;

public interface IPrivateResponseDisplay : IDisposable
{
    Task BeginAsync(string id, AppSettings settings, CancellationToken ct);
    Task AnswerAsync(string id, AIAnswer answer, CancellationToken ct);
    Task HideAsync(string? id = null);
}
