import { HOST, wire, validEnvelope, type WireMessage } from "../protocol.js";
import { cropImage, type Region } from "./image.js";

let port: chrome.runtime.Port | undefined;
const pending = new Map<string, { resolve: (v: Record<string, unknown>) => void; reject: (e: Error) => void; timer: ReturnType<typeof setTimeout> }>();
const active = new Map<number, string>();
const helperError = "Install Invisible AI Setup, then reopen the browser.";
function connect(): chrome.runtime.Port {
  if (port) return port;
  const current = chrome.runtime.connectNative(HOST); port = current;
  current.onDisconnect.addListener(() => {
    void chrome.runtime.lastError; port = undefined;
    for (const item of pending.values()) { clearTimeout(item.timer); item.reject(new Error(helperError)); }
    pending.clear();
  });
  current.onMessage.addListener((raw: unknown) => {
    if (!validEnvelope(raw)) { current.disconnect(); return; }
    if (raw.type === "PROCESSING_START") return;
    const item = pending.get(raw.id); if (!item) return;
    clearTimeout(item.timer); pending.delete(raw.id);
    if (raw.type === "ERROR") item.reject(new Error(String(raw.payload?.message ?? "Could not connect to AI provider.")));
    else item.resolve(raw.payload ?? {});
  });
  return current;
}
function send(m: WireMessage): Promise<Record<string, unknown>> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { pending.delete(m.id); cancel(m.id); reject(new Error("AI request timed out.")); }, 305000);
    pending.set(m.id, { resolve, reject, timer });
    try { connect().postMessage(m); } catch { clearTimeout(timer); pending.delete(m.id); reject(new Error(helperError)); }
  });
}
function cancel(id: string): void {
  try { port?.postMessage(wire("CANCEL", { targetId: id })); } catch { /* Already disconnected. */ }
}
let creating: Promise<void> | undefined;
async function clipboardDocument(): Promise<void> {
  const url = chrome.runtime.getURL("src/offscreen/index.html");
  const contexts = await chrome.runtime.getContexts({ contextTypes: [chrome.runtime.ContextType.OFFSCREEN_DOCUMENT], documentUrls: [url] });
  if (!contexts.length) {
    creating ??= chrome.offscreen.createDocument({ url: "src/offscreen/index.html", reasons: [chrome.offscreen.Reason.CLIPBOARD], justification: "Read copied text only after the explicit AI shortcut." }).finally(() => { creating = undefined; });
    await creating;
  }
}
async function clipboard(): Promise<string> {
  await clipboardDocument();
  const result = await chrome.runtime.sendMessage({ target: "clipboard", action: "read" });
  if (!result?.ok) throw new Error("Could not read clipboard text. Copy the question again.");
  if (typeof result.text !== "string" || !result.text.trim()) throw new Error("Clipboard has no plain text. Copy a question first.");
  if (result.text.length > 50000) throw new Error("Question too long (maximum 50,000 characters).");
  return result.text;
}
async function display(tabId: number, id: string, state: string, text = "", seconds = 22, details = "", opacity = 0.55): Promise<void> {
  await chrome.tabs.sendMessage(tabId, { target: "overlay", id, state, text, seconds, details, opacity });
}
const recent = new Map<number, number>();
async function ask(selectedTab?: chrome.tabs.Tab, image = false): Promise<void> {
  const tab = selectedTab ?? (await chrome.tabs.query({ active: true, currentWindow: true }))[0];
  if (!tab?.id) return;
  const tabId = tab.id, id = crypto.randomUUID();
  const now = Date.now(); if (now - (recent.get(tabId) ?? 0) < 200) return;
  recent.set(tabId, now);
  const previous = active.get(tabId); if (previous) cancel(previous);
  active.set(tabId, id);
  let imageBase64: string | undefined, screenshot: string | undefined;
  try {
    // Command invocation grants activeTab. Nothing is injected or read at idle.
    if (!selectedTab) await chrome.scripting.executeScript({ target: { tabId }, files: ["src/content/overlay.js"] });
    let text: string | undefined;
    if (image) {
      await display(tabId, id, "prepare");
      const preferences = await send(wire("PING"));
      if (active.get(tabId) !== id) return;
      if (preferences.enabled === false || preferences.networkEnabled === false || preferences.screenshotEnabled === false) throw new Error("Image processing is disabled in extension settings.");
      const selection = await chrome.tabs.sendMessage(tabId, { target: "overlay", state: "select", id }) as { cancelled?: boolean; region?: Region };
      if (active.get(tabId) !== id || selection?.cancelled) return;
      if (!selection?.region || tab.windowId === undefined) throw new Error("Could not read the selected region.");
      const [visible] = await chrome.tabs.query({ active: true, windowId: tab.windowId });
      if (visible?.id !== tabId) throw new Error("Return to the question tab and select again.");
      // UI is already removed and painted before selection replies. No processing dot in capture.
      try { screenshot = await chrome.tabs.captureVisibleTab(tab.windowId, { format: "png" }); }
      catch { throw new Error("Capture unavailable. Assign the image shortcut in extension shortcut settings, or click the extension icon once on this page."); }
      const [captured] = await chrome.tabs.query({ active: true, windowId: tab.windowId });
      if (captured?.id !== tabId) throw new Error("Return to the question tab and select again.");
      if (active.get(tabId) !== id) return;
      imageBase64 = await cropImage(screenshot, selection.region); screenshot = undefined;
    } else {
      await display(tabId, id, "processing");
      const preferences = await send(wire("PING"));
      if (active.get(tabId) !== id) return;
      if (preferences.enabled === false || preferences.networkEnabled === false || preferences.clipboardEnabled === false) throw new Error("Text processing is disabled in extension settings.");
      text = await clipboard();
    }
    if (active.get(tabId) !== id) return;
    if (image) await display(tabId, id, "processing");
    const data = await send({ version: 1, type: image ? "SCREENSHOT_INPUT" : "TEXT_INPUT", id, payload: image ? { imageBase64 } : { text } });
    if (active.get(tabId) !== id) return;
    const result = data.result as { content?: string; details?: string } | undefined;
    await display(tabId, id, "answer", String(result?.content ?? "No answer received."), Number(data.responseSeconds ?? 22), String(result?.details ?? ""), Number(data.responseOpacity ?? 0.55));
  } catch (error) {
    if (active.get(tabId) !== id) return;
    // Restricted pages cannot receive UI; action badge provides a small indication.
    try { await display(tabId, id, "error", error instanceof Error ? error.message : "Could not connect to AI provider."); }
    catch { await chrome.action.setBadgeText({ tabId, text: "!" }); await chrome.action.setTitle({ tabId, title: "Invisible AI: use an ordinary webpage; this page cannot display extension UI." }); }
  } finally {
    // Explicitly release image string references; JavaScript strings cannot be securely zeroed.
    // eslint-disable-next-line no-useless-assignment
    imageBase64 = undefined;
    // eslint-disable-next-line no-useless-assignment
    screenshot = undefined;
  }
}
chrome.commands.onCommand.addListener(command => {
  if (command === "ask-clipboard") void ask();
  else if (command === "ask-region") void ask(undefined, true);
  else if (command === "hide-answer") void (async () => {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id) return;
    const id = active.get(tab.id); if (id) { cancel(id); active.delete(tab.id); }
    try { await display(tab.id, id ?? "", "hide"); } catch { /* No overlay on this page. */ }
  })();
});
chrome.tabs.onRemoved.addListener(tabId => { const id = active.get(tabId); if (id) cancel(id); active.delete(tabId); recent.delete(tabId); });
chrome.tabs.onUpdated.addListener((tabId, changes) => { if (changes.status === "loading") { const id = active.get(tabId); if (id) cancel(id); active.delete(tabId); } });
chrome.runtime.onMessage.addListener((request: unknown, sender, reply) => {
  if (sender.id === chrome.runtime.id && sender.frameId === 0 && sender.tab?.id && /^https?:\/\//.test(sender.url ?? "") && (request as { action?: string })?.action === "copy-code") {
    const text = (request as { text?: unknown }).text;
    if (typeof text !== "string" || text.length > 12000) { reply({ ok: false }); return false; }
    void clipboardDocument().then(() => chrome.runtime.sendMessage({ target: "clipboard", action: "write", text })).then(reply).catch(() => reply({ ok: false }));
    return true;
  }
  if (sender.id === chrome.runtime.id && sender.frameId === 0 && sender.tab?.id && /^https?:\/\//.test(sender.url ?? "") && ["ask-clipboard", "ask-region"].includes(String((request as { action?: string })?.action))) {
    void ask(sender.tab, (request as { action: string }).action === "ask-region"); return false;
  }
  if (sender.id !== chrome.runtime.id || !sender.url || !["src/settings/index.html", "src/popup/index.html"].some(path => sender.url === chrome.runtime.getURL(path))) return false;
  if (!request || typeof request !== "object") return false;
  const r = request as { action?: string; values?: Record<string, unknown> };
  let response: Promise<Record<string, unknown>>;
  if (r.action === "status") response = send(wire("PING"));
  else if (r.action === "privacy") {
    const values = r.values;
    if (!values || Object.keys(values).some(k => !["enabled", "clipboardEnabled", "screenshotEnabled", "networkEnabled", "responseSeconds", "answerMode", "programmingLanguage", "responseOpacity"].includes(k))) { reply({ ok: false, error: "Invalid privacy settings." }); return false; }
    for (const [tabId, id] of active) { cancel(id); void display(tabId, id, "hide").catch(() => {}); }
    active.clear(); response = send(wire("SETTINGS_UPDATE", values));
  }
  else if (r.action === "save") {
    const v = r.values;
    if (!v || !["Gemini Web", "Groq"].includes(String(v.provider)) || Object.keys(v).some(k => !["provider", "credential", "model", "responseSeconds", "answerMode", "programmingLanguage", "responseOpacity"].includes(k))) { reply({ ok: false, error: "Invalid provider settings." }); return false; }
    // Switching configuration cancels pending generations; never route to a fallback.
    for (const [tabId, id] of active) { cancel(id); void display(tabId, id, "hide").catch(() => { /* Tab navigated. */ }); }
    active.clear(); response = send(wire("CONNECT", v));
  } else return false;
  void response.then(data => reply({ ok: true, data })).catch((e: Error) => reply({ ok: false, error: e.message }));
  return true;
});
