import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import vm from "node:vm";
import { validEnvelope, wire, HOST } from "../dist/src/protocol.js";

test("Protocol uses native host and versioned correlation IDs", () => {
  assert.equal(HOST, "com.invisibleai.assistant");
  const m = wire("TEXT_INPUT", { text: "Ω" }); assert.equal(validEnvelope(m), true);
  assert.equal(validEnvelope({ version: 2, type: "PING", id: "x" }), false);
  assert.equal(validEnvelope({ version: 1, type: "PING", id: "" }), false);
  assert.notEqual(wire("PING").id, wire("PING").id);
});
test("Selection script reads only selection when explicitly injected; excludes passwords", async () => {
  const code = await readFile(new URL("../dist/src/content/selection.js", import.meta.url), "utf8");
  class HTMLInputElement { constructor(type, value) { this.type = type; this.value = value; this.selectionStart = 1; this.selectionEnd = 4; } }
  class HTMLTextAreaElement {}
  function read(activeElement, text) {
    return vm.runInNewContext(code, { document: { activeElement }, window: { getSelection: () => ({ toString: () => text }) }, HTMLInputElement, HTMLTextAreaElement });
  }
  assert.equal(read(null, "selected page text"), "selected page text");
  assert.equal(read(new HTMLInputElement("text", "abcdef"), ""), "bcd");
  assert.equal(read(new HTMLInputElement("password", "secret"), "secret"), "");
  assert.equal(read(null, "x".repeat(60000)).length, 50001);
});
test("MV3 permissions have no background page/screen/clipboard access", async () => {
  const manifest = JSON.parse(await readFile(new URL("../dist/manifest.json", import.meta.url)));
  assert.equal(manifest.manifest_version, 3);
  assert.deepEqual(manifest.permissions.sort(), ["activeTab", "contextMenus", "nativeMessaging", "scripting"].sort());
  assert.equal(manifest.content_scripts, undefined); assert.equal(manifest.host_permissions, undefined);
  assert.equal(manifest.externally_connectable, undefined);
  for (const file of [manifest.background.service_worker, manifest.action.default_popup, manifest.options_ui.page]) await readFile(new URL(`../dist/${file}`, import.meta.url));
});
