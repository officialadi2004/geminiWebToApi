import { invoke, node } from "../ui.js";
const provider = node<HTMLSelectElement>("provider"), credential = node<HTMLInputElement>("credential"), model = node<HTMLSelectElement>("model"), duration = node<HTMLInputElement>("duration"), status = node("status");
function busy(value: boolean): void {
  for (const control of document.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLButtonElement>("input,select,button")) control.disabled = value;
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
  busy(true);
  status.textContent = "Connecting...";
  const secret = credential.value; credential.value = "";
  try {
    const data = await invoke("save", { provider: provider.value, credential: secret, model: model.value, responseSeconds: Number(duration.value) });
    const settings = data.settings as { model: string; responseSeconds: number };
    model.replaceChildren(...(data.models as { id: string; name: string }[]).map(m => new Option(m.name, m.id)));
    model.value = settings.model; duration.value = String(settings.responseSeconds);
    status.textContent = "● Connected — credential securely stored. Model saved.";
  } catch (error) { status.textContent = error instanceof Error ? error.message : "Could not connect to AI provider."; }
  finally { busy(false); }
}
node<HTMLFormElement>("form").addEventListener("submit", event => { event.preventDefault(); void save(); });
node("test").addEventListener("click", () => { void save(); });
void invoke("status").then(data => {
  for (const key of ["enabled", "clipboardEnabled", "networkEnabled"]) node<HTMLInputElement>(key).checked = data[key] !== false;
  provider.value = String(data.provider ?? ""); appearance(); duration.value = String(data.responseSeconds ?? 12);
  if (data.model) model.replaceChildren(new Option(String(data.model), String(data.model)));
  status.textContent = data.credentialSaved ? "Credential saved. Test Connection to refresh available models." : "Choose a provider, enter your credential, and Connect / Save.";
}).catch((e: Error) => { status.textContent = e.message; }).finally(() => busy(false));

node("privacy").addEventListener("click", () => {
  const values: Record<string, unknown> = { responseSeconds: Number(duration.value) };
  for (const key of ["enabled", "clipboardEnabled", "networkEnabled"]) values[key] = node<HTMLInputElement>(key).checked;
  void invoke("privacy", values).then(() => { status.textContent = "Advanced preferences saved."; }).catch((e: Error) => { status.textContent = e.message; });
});
