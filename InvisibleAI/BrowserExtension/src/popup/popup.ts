import { invoke, node } from "../ui.js";
node("settings").addEventListener("click", () => { void chrome.runtime.openOptionsPage(); });
void invoke("status").then(async data => {
  const commands = await chrome.commands.getAll();
  node("status").textContent = !commands.find(c => c.name === "ask-clipboard")?.shortcut ? "Assign Ctrl+Shift+V in your browser's extension shortcut settings." : data.credentialSaved ? `Ready — ${String(data.provider)}` : "Open settings to connect your provider.";
}).catch((e: Error) => { node("status").textContent = e.message; });
