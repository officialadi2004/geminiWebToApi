import test from "node:test";
import assert from "node:assert/strict";
const listeners = {};
let connection, messages = [];
function event(name) { return { addListener(fn) { listeners[name] = fn; } }; }
globalThis.chrome = {
  runtime: {
    id: "extension-id", lastError: undefined,
    getURL: path => `chrome-extension://extension-id/${path}`,
    connectNative(host) {
      assert.equal(host, "com.invisibleai.assistant");
      connection = {
        onMessage: event("nativeMessage"), onDisconnect: event("disconnect"),
        postMessage(m) {
          messages.push(m);
          queueMicrotask(() => {
            if (m.type === "TEXT_INPUT") listeners.nativeMessage({ version: 1, type: "PROCESSING_START", id: m.id });
            listeners.nativeMessage({ version: 1, type: m.type === "TEXT_INPUT" ? "PROCESSING_COMPLETE" : m.type === "PROVIDER_API" ? "PROVIDER_RESULT" : "SETTINGS_UPDATE", id: m.id,
              payload: m.type === "TEXT_INPUT" ? { text: "A", status: "complete" } : m.type === "PROVIDER_API" ? { connected: true, provider: m.payload.path.split("/")[4] } : { connected: true, enabled: true, model: "test-model" } });
          });
        }, disconnect() {}
      }; return connection;
    },
    onInstalled: event("installed"), onMessage: event("uiMessage")
  },
  contextMenus: { onClicked: event("contextClick"), removeAll(fn) { fn(); }, create() {} },
  tabs: { async query() { return [{ id: 7, url: "https://example.com" }]; } },
  scripting: { async executeScript(options) { assert.deepEqual(options.files, ["src/content/selection.js"]); return [{ result: "Which option? A) one B) two" }]; } }
};
await import("../dist/src/background/service-worker.js");
const sender = { id: "extension-id", url: "chrome-extension://extension-id/src/popup/index.html" };
function invoke(action, values) { return new Promise(resolve => listeners.uiMessage({ action, values }, sender, resolve)); }
test("Background sends no inputs on connection; correlates status and explicit selection", async () => {
  assert.equal(messages.length, 0);
  const connected = await invoke("connect"); assert.equal(connected.ok, true); assert.equal(messages[0].type, "PING");
  const answer = await invoke("ask-selection"); assert.equal(answer.data.text, "A");
  assert.equal(messages[1].payload.text, "Which option? A) one B) two");
});
test("Background rejects webpage senders and unapproved settings", async () => {
  let called = false;
  assert.equal(listeners.uiMessage({ action: "ask-selection" }, { id: "extension-id", url: "https://example.com" }, () => { called = true; }), false);
  assert.equal(called, false);
  assert.equal((await invoke("save-settings", { responseMode: "FAKE", webSearch: false })).ok, false);
  const result = await invoke("save-settings", { responseMode: "SHORT", webSearch: true, apiKey: "do-not-send" });
  assert.equal(result.ok, true);
  assert.deepEqual(messages.at(-1).payload, { responseMode: "SHORT", webSearch: true });
});
test("Provider choices and test routes never forward credential fields", async () => {
  for (const provider of ["Gemini Web", "Groq"]) {
    const result = await invoke("save-settings", { provider, responseMode: "CONCISE", webSearch: false,
      cookies: "synthetic-cookie-sentinel", apiKey: "synthetic-key-sentinel", userId: "user-b" });
    assert.equal(result.ok, true);
    assert.deepEqual(messages.at(-1).payload, { provider, responseMode: "CONCISE", webSearch: false });
    const connected = await invoke("test-provider", { provider, cookies: "synthetic-cookie-sentinel", apiKey: "synthetic-key-sentinel" });
    assert.equal(connected.data.connected, true);
    assert.equal(messages.at(-1).type, "PROVIDER_API");
    assert.deepEqual(messages.at(-1).payload, { method: "POST", path: `/api/ai/providers/${provider === "Groq" ? "groq" : "gemini"}/test` });
    assert.deepEqual(Object.keys(connected.data).sort(), ["connected", "provider"]);
  }
  assert.equal((await invoke("save-settings", { provider: "OpenAI", responseMode: "CONCISE", webSearch: false })).ok, false);
  assert.equal((await invoke("test-provider", { provider: "user-b/gemini" })).ok, false);
  assert.equal(JSON.stringify(messages).includes("synthetic-cookie-sentinel"), false);
  assert.equal(JSON.stringify(messages).includes("synthetic-key-sentinel"), false);
});
test("Disconnect returns meaningful connection status", async () => {
  globalThis.chrome.runtime.lastError = { message: "Native host is not registered" };
  listeners.disconnect();
  assert.equal((await invoke("status")).data.connected, false);
  assert.match((await invoke("status")).data.error, /not registered/);
});
