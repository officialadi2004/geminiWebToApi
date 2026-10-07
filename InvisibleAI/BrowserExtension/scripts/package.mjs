import { cp, mkdir } from "node:fs/promises";
await mkdir("dist", { recursive: true });
await cp("manifest.json", "dist/manifest.json");
for (const file of ["popup/index.html", "settings/index.html", "styles.css"])
  await cp(`src/${file}`, `dist/src/${file}`);
console.log("Load BrowserExtension/dist as the unpacked MV3 extension.");
