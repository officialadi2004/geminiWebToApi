import { invoke, node } from "../ui.js";
node("extension-id").textContent = chrome.runtime.id;
const status = node("status"), mode = node<HTMLSelectElement>("mode"), web = node<HTMLInputElement>("web");
const provider = node<HTMLSelectElement>("provider"), providerStatus = node("provider-status");
async function load(): Promise<void> {
  try { const result = await invoke("connect"); status.textContent = `Connected · ${result.model ?? "Windows companion"}`;
    if (["SHORT", "CONCISE", "DETAILED"].includes(String(result.responseMode))) mode.value = String(result.responseMode);
    web.checked = result.webSearch === true;
    if (["Gemini Web", "Groq"].includes(String(result.provider))) provider.value = String(result.provider);
  } catch (e) { status.textContent = (e as Error).message; }
}
node("connect").addEventListener("click", () => void load());
node("desktop").addEventListener("click", () => { void invoke("open-settings").catch(e => { status.textContent = e.message; }); });
node("save").addEventListener("click", () => {
  if (!provider.value) { status.textContent = "Choose Gemini Web or Groq first."; return; }
  void invoke("save-settings", { responseMode: mode.value, webSearch: web.checked, provider: provider.value }).then(() => { status.textContent = "Saved to Windows companion."; }).catch(e => { status.textContent = e.message; });
});
node("test-provider").addEventListener("click", () => {
  if (!provider.value) { providerStatus.textContent = "Choose a provider first."; return; }
  providerStatus.textContent = "Connecting…";
  void invoke("test-provider", { provider: provider.value }).then(result => { providerStatus.textContent = result.connected ? "● Connected" : String(result.error ?? "Provider unavailable."); }).catch(e => { providerStatus.textContent = e.message; });
});
void load();
