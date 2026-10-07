using System.Runtime.InteropServices;

namespace InvisibleAI.Companion.Clipboard;

public sealed class ClipboardService
{
    public async Task<string> ReadTextAsync(CancellationToken ct)
    {
        // Called on the STA dispatcher only after invocation. Never writes or watches the clipboard.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!System.Windows.Clipboard.ContainsText()) throw new InvalidOperationException("Copy plain text first; this clipboard format is not supported.");
                var text = System.Windows.Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("The clipboard is empty. Copy text first.");
                if (text.Length > 50000) throw new InvalidOperationException("Select less text (maximum 50,000 characters).");
                return text;
            }
            catch (COMException) { if (attempt == 4) break; await Task.Delay(60, ct); }
        }
        throw new InvalidOperationException("The clipboard is busy. Try again.");
    }
}
