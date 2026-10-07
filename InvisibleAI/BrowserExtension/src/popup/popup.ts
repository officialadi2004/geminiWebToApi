import { invoke, node } from "../ui.js";
const status = node("status");
async function run(action: string, button?: HTMLButtonElement): Promise<void> {
  if (button) button.disabled = true;
  try {
    if (action === "ask-selection") status.textContent = "Processing · look for the tiny desktop dot";
    const result = await invoke(action);
    status.textContent = action === "ask-selection" ? (result.status === "cancelled" ? "Request cancelled." : "Answer displayed by the Windows companion.")
      : `Companion connected${result.enabled === false ? " · disabled" : ""}`;
  } catch (e) { status.textContent = (e as Error).message; }
  finally { if (button) button.disabled = false; }
}
for (const [id, action] of [["ask", "ask-selection"], ["connect", "connect"], ["desktop", "open-settings"]]) {
  const button = node<HTMLButtonElement>(id); button.addEventListener("click", () => void run(action, button));
}
node("options").addEventListener("click", () => void chrome.runtime.openOptionsPage());
void run("connect");
