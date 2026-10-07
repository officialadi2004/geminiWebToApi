import { spawn } from "node:child_process";
import { access, mkdir, writeFile, unlink } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const folder = path.resolve(process.argv[2] ?? path.join(root, "artifacts/win-x64"));
const manifest = path.join(folder, "com.invisibleai.assistant.json");
try { await access(manifest); throw new Error("Run against a fresh publish folder, not a registered installation."); }
catch (e) { if (e.code !== "ENOENT") throw e; }
const profile = path.join(root, "artifacts/smoke-profile");
await mkdir(profile, { recursive: true });
await writeFile(path.join(profile, "settings.json"), JSON.stringify({ networkEnabled: false,
  clipboardShortcut: "Ctrl+Alt+Shift+F1", screenShortcut: "Ctrl+Alt+Shift+F2", hideShortcut: "Ctrl+Alt+Shift+F3",
  toggleShortcut: "Ctrl+Alt+Shift+F4", expandShortcut: "Ctrl+Alt+Shift+F5" }));
const origin = "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/";
await writeFile(manifest, JSON.stringify({ name: "com.invisibleai.assistant", type: "stdio", path: path.join(folder, "InvisibleAI.NativeHost.exe"), allowed_origins: [origin] }));
const companion = spawn(path.join(folder, "InvisibleAI.Companion.exe"), ["--background", "--data-dir", profile], { windowsHide: true, stdio: "ignore" });
const host = spawn(path.join(folder, "InvisibleAI.NativeHost.exe"), [origin], { windowsHide: true, stdio: ["pipe", "pipe", "pipe"] });
let buffer = Buffer.alloc(0);
const waiting = new Map();
host.stdout.on("data", data => {
  buffer = Buffer.concat([buffer, data]);
  while (buffer.length >= 4 && buffer.length >= 4 + buffer.readUInt32LE(0)) {
    const length = buffer.readUInt32LE(0);
    const response = JSON.parse(buffer.subarray(4, 4 + length).toString("utf8"));
    buffer = buffer.subarray(4 + length);
    waiting.get(response.id)?.(response);
  }
});
function ask(type, payload) {
  const id = crypto.randomUUID();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => { waiting.delete(id); reject(new Error("Published bridge timed out.")); }, 12000);
    waiting.set(id, response => { clearTimeout(timer); waiting.delete(id); resolve(response); });
    const body = Buffer.from(JSON.stringify({ version: 1, id, type, payload }));
    const header = Buffer.alloc(4); header.writeUInt32LE(body.length); host.stdin.write(Buffer.concat([header, body]));
  });
}
try {
  const handshake = await ask("PING");
  if (handshake.type !== "SETTINGS_UPDATE" || handshake.payload?.connected !== true) throw new Error("Published companion handshake failed.");
  const blocked = await ask("TEXT_INPUT", { text: "synthetic input; network must remain disabled" });
  if (blocked.type !== "ERROR" || !blocked.payload?.message.includes("Network requests are disabled")) throw new Error("Published privacy gate failed.");
  for (const provider of ["gemini", "groq"]) {
    const checked = await ask("PROVIDER_API", { method: "POST", path: `/api/ai/providers/${provider}/test` });
    if (checked.type !== "PROVIDER_RESULT" || checked.payload?.provider !== provider || checked.payload?.connected !== false || !checked.payload?.error.includes("Network requests are disabled")) throw new Error("Published provider route/privacy gate failed.");
    if (Object.keys(checked.payload).sort().join(",") !== "connected,error,provider") throw new Error("Unexpected provider API fields.");
  }
  console.log("PASS published companion → native executable → framed JSON handshake, both provider APIs and network privacy gates");
} finally {
  host.stdin.end(); host.kill(); companion.kill(); await unlink(manifest);
}
