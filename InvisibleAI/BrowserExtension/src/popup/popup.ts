import { invoke, node } from "../ui.js";
node("settings").addEventListener("click", () => { void chrome.runtime.openOptionsPage(); });
void invoke("status").then(async data => {
  const commands = await chrome.commands.getAll();
  const missing = [["ask-clipboard", "Ctrl+Shift+V"], ["ask-region", "Ctrl+Shift+S"]].filter(([name]) => !commands.find(c => c.name === name)?.shortcut).map(([, key]) => key);
  node("status").textContent = missing.length ? `Assign ${missing.join(" / ")} in your browser's extension shortcut settings.` : data.credentialSaved ? `Ready — ${String(data.provider)}` : "Open settings to connect your provider.";
}).catch((e: Error) => { node("status").textContent = e.message; });
