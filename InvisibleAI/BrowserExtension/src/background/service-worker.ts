import { HOST, wire, validEnvelope, type Status } from "../protocol.js";

let port: chrome.runtime.Port | undefined;
let status: Status = { connected: false, processing: false };
const pending = new Map<string, { resolve: (value: unknown) => void; reject: (error: Error) => void; timer: ReturnType<typeof setTimeout> }>();

function disconnect(message: string): void {
  port = undefined; status = { connected: false, processing: false, error: message };
  for (const item of pending.values()) { clearTimeout(item.timer); item.reject(new Error(message)); }
  pending.clear();
}
function connect(): chrome.runtime.Port {
  if (port) return port;
  const current = chrome.runtime.connectNative(HOST);
  port = current;
  current.onDisconnect.addListener(() => {
    const message = chrome.runtime.lastError?.message ?? "Windows companion disconnected.";
    disconnect(message);
  });
  current.onMessage.addListener((raw: unknown) => {
    if (!validEnvelope(raw)) { current.disconnect(); disconnect("Invalid companion response."); return; }
    const m = raw;
    if (m.id === "connection" && m.type === "ERROR") { current.disconnect(); disconnect(String(m.payload?.message ?? "Native connection failed.")); return; }
    status.connected = true; status.error = undefined;
    if (m.type === "PROCESSING_START") { status.processing = true; return; }
    if (m.type === "PROCESSING_COMPLETE") status.processing = false;
    if (m.type === "ERROR") { status.processing = false; status.error = String(m.payload?.message ?? "AI request failed."); }
    if (m.type === "SETTINGS_UPDATE") {
      if (typeof m.payload?.enabled === "boolean") status.enabled = m.payload.enabled;
      if (typeof m.payload?.model === "string") status.model = m.payload.model;
    }
    const waiting = pending.get(m.id);
    if (waiting) {
      clearTimeout(waiting.timer); pending.delete(m.id);
      if (m.type === "ERROR") waiting.reject(new Error(status.error));
      else waiting.resolve(m.payload ?? {});
    }
  });
  return current;
}
async function send(type: string, payload?: Record<string, unknown>): Promise<unknown> {
  const m = wire(type, payload);
  const p = connect();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { pending.delete(m.id); status.processing = false; reject(new Error("Companion timed out. Check the tray application.")); }, type === "PING" ? 15000 : 315000);
    pending.set(m.id, { resolve, reject, timer });
    try { p.postMessage(m); } catch { clearTimeout(timer); pending.delete(m.id); reject(new Error("Could not reach the Windows companion.")); }
  });
}
async function selectedText(): Promise<string> {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id || !tab.url || !/^https?:\/\//.test(tab.url)) throw new Error("Select text on an ordinary webpage. Browser-internal pages are restricted; use the clipboard shortcut instead.");
  try {
    const results = await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ["src/content/selection.js"] });
    const text = String(results[0]?.result ?? "").trim();
    if (!text) throw new Error("Select text on the page first.");
    if (text.length > 50000) throw new Error("Select less text (maximum 50,000 characters).");
    return text;
  } catch (error) { throw error instanceof Error ? error : new Error("The page blocked selection access. Use the clipboard shortcut."); }
}
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => chrome.contextMenus.create({ id: "ask-selection", title: "Ask Invisible AI about selection", contexts: ["selection"], documentUrlPatterns: ["http://*/*", "https://*/*"] }));
});
chrome.contextMenus.onClicked.addListener((info) => {
  if (info.menuItemId !== "ask-selection" || !info.selectionText) return;
  const text = info.selectionText.trim();
  if (text.length > 50000) { status.error = "Select less text (maximum 50,000 characters)."; return; }
  void send("TEXT_INPUT", { text }).catch(error => { status.error = String(error.message); });
});
chrome.runtime.onMessage.addListener((request: unknown, sender, reply) => {
  // No content-script message listener or externally_connectable surface. Only our extension UI can invoke actions.
  if (sender.id !== chrome.runtime.id || !sender.url?.startsWith(chrome.runtime.getURL("src/")) || !request || typeof request !== "object") return false;
  const action = (request as { action?: string }).action;
  if (action === "status") { reply({ ok: true, data: status }); return false; }
  void (async () => {
    switch (action) {
      case "connect": return send("PING");
      case "ask-selection": return send("TEXT_INPUT", { text: await selectedText() });
      case "open-settings": return send("OPEN_SETTINGS");
      case "save-settings": {
        const values = (request as { values?: Record<string, unknown> }).values;
        if (!values || !["SHORT", "CONCISE", "DETAILED"].includes(String(values.responseMode)) || typeof values.webSearch !== "boolean") throw new Error("Invalid settings.");
        if (values.provider !== undefined && !["Gemini Web", "Groq"].includes(String(values.provider))) throw new Error("Choose Gemini Web or Groq.");
        return send("SETTINGS_UPDATE", { responseMode: values.responseMode, webSearch: values.webSearch, ...(values.provider ? { provider: values.provider } : {}) });
      }
      case "test-provider": {
        const selected = (request as { values?: Record<string, unknown> }).values?.provider;
        if (selected !== "Gemini Web" && selected !== "Groq") throw new Error("Choose Gemini Web or Groq.");
        return send("PROVIDER_API", { method: "POST", path: `/api/ai/providers/${selected === "Groq" ? "groq" : "gemini"}/test` });
      }
      default: throw new Error("Unknown action.");
    }
  })().then(data => reply({ ok: true, data })).catch((error: Error) => reply({ ok: false, error: error.message }));
  return true;
});
