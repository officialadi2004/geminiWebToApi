import { cp, mkdir } from "node:fs/promises";
await mkdir("dist", { recursive: true });
await cp("manifest.json", "dist/manifest.json");
for (const file of ["popup/index.html", "settings/index.html", "offscreen/index.html", "styles.css"]) {
  await mkdir(`dist/src/${file.split("/").slice(0, -1).join("/")}`, { recursive: true });
  await cp(`src/${file}`, `dist/src/${file}`);
}
console.log("Extension built: BrowserExtension/dist");
