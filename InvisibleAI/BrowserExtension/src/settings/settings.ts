import { invoke, node } from "../ui.js";
const provider = node<HTMLSelectElement>("provider"), credential = node<HTMLInputElement>("credential"), model = node<HTMLSelectElement>("model"), duration = node<HTMLInputElement>("duration"), preset = node<HTMLSelectElement>("durationPreset"), status = node("status");
const privacyKeys = ["enabled", "clipboardEnabled", "screenshotEnabled", "networkEnabled"];
function busy(value: boolean): void {
  for (const control of document.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLButtonElement>("input,select,button")) control.disabled = value;
}
function durationUI(): void {
  const custom = preset.value === "custom";
  duration.hidden = node("durationLabel").hidden = !custom;
  if (!custom) duration.value = preset.value;
}
preset.addEventListener("change", durationUI);
function setPreferences(data: Record<string, unknown>): void {
  for (const key of privacyKeys) node<HTMLInputElement>(key).checked = data[key] !== false;
  duration.value = String(data.responseSeconds ?? 22);
  preset.value = ["12", "22", "30"].includes(duration.value) ? duration.value : "custom"; durationUI();
  node<HTMLSelectElement>("answerMode").value = String(data.answerMode ?? "Quick");
  node<HTMLSelectElement>("programmingLanguage").value = String(data.programmingLanguage ?? "Auto Detect");
  node<HTMLInputElement>("responseOpacity").value = String(Math.round(Number(data.responseOpacity ?? .55) * 100));
  node<HTMLInputElement>("privateResponses").checked = data.privateResponses !== false;
}
function preferences(): Record<string, unknown> {
  return { responseSeconds: Number(duration.value), answerMode: node<HTMLSelectElement>("answerMode").value, programmingLanguage: node<HTMLSelectElement>("programmingLanguage").value, responseOpacity: Number(node<HTMLInputElement>("responseOpacity").value) / 100, privateResponses: node<HTMLInputElement>("privateResponses").checked };
}
busy(true);
function appearance(): void {
  credential.value = ""; model.replaceChildren(new Option("Connect to load available models", ""));
  node("credential-label").textContent = provider.value === "Gemini Web" ? "Gemini Cookies" : provider.value === "Groq" ? "Groq API Key" : "Credential";
  node("warning").textContent = provider.value === "Gemini Web" ? "Gemini cookies are sensitive authentication credentials. Use only your own Gemini account. They stay in Windows Credential Manager and are never returned here." : "Your Groq API key stays in Windows Credential Manager and is never returned here.";
  credential.placeholder = "Leave blank to use a saved credential";
}
provider.addEventListener("change", appearance);
async function save(): Promise<void> {
  busy(true); status.textContent = "Connecting...";
  const secret = credential.value; credential.value = "";
  try {
    const data = await invoke("save", { provider: provider.value, credential: secret, model: model.value, ...preferences() });
    const settings = data.settings as Record<string, unknown>;
    model.replaceChildren(...(data.models as { id: string; name: string; supportsImages: boolean }[]).map(m => new Option(m.name + (!m.supportsImages ? " · Text only" : m.name.includes("Images") ? "" : " · Images"), m.id)));
    model.value = String(settings.model); setPreferences(settings);
    status.textContent = "● Connected — credential securely stored. Model saved.";
  } catch (error) { status.textContent = error instanceof Error ? error.message : "Could not connect to AI provider."; }
  finally { busy(false); }
}
node<HTMLFormElement>("form").addEventListener("submit", event => { event.preventDefault(); void save(); });
node("test").addEventListener("click", () => { void save(); });
void invoke("status").then(data => {
  provider.value = String(data.provider ?? ""); appearance(); setPreferences(data);
  if (data.model) model.replaceChildren(new Option(String(data.model), String(data.model)));
  status.textContent = data.credentialSaved ? "Credential saved. Test Connection to refresh available models." : "Choose a provider, enter your credential, and Connect / Save.";
}).catch((e: Error) => { status.textContent = e.message; }).finally(() => busy(false));
node("privacy").addEventListener("click", () => {
  const values = preferences(); for (const key of privacyKeys) values[key] = node<HTMLInputElement>(key).checked;
  busy(true);
  void invoke("privacy", values).then(() => { status.textContent = "Preferences saved."; }).catch((e: Error) => { status.textContent = e.message; }).finally(() => busy(false));
});
