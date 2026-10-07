import { rm } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
const root = fileURLToPath(new URL("../", import.meta.url));
const target = resolve(root, "dist");
if (dirname(target) !== resolve(root)) throw new Error("Build output is outside the extension directory.");
await rm(target, { recursive: true, force: true });
