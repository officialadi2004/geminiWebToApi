# Invisible AI Assistant

Browser-first Chrome/Edge Manifest V3 assistant: select a question, **Ctrl+C → Ctrl+Shift+V**, and see a small click-through answer in the page corner. MCQs return the supplied option label; ordinary questions receive a concise answer.

Exactly two providers: **Gemini Web**, using your own session cookies through the existing `gemini-webapi` client, and **Groq**, using your own API key. A silent, browser-launched Windows helper stores credentials in Windows Credential Manager and displays private answers in a tiny capture-excluded Windows window by default. There is no tray app, WPF overlay, global hotkey service or background screen monitoring.

See [installation, first-time setup, architecture and test instructions](InvisibleAI/README.md) and the [honest verification record](InvisibleAI/VERIFICATION.md). The self-contained installer and extension are produced by the Windows build workflow. No credentials belong in this repository.
