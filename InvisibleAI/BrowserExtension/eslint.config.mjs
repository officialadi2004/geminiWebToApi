import js from "@eslint/js";
import ts from "typescript-eslint";
import globals from "globals";

export default [
  { ignores: ["dist/**", "node_modules/**"] },
  js.configs.recommended,
  ...ts.configs.recommended,
  { files: ["src/**/*.ts"], languageOptions: { globals: { ...globals.browser, chrome: "readonly" } } },
  { files: ["**/*.mjs"], languageOptions: { globals: globals.node } },
  { files: ["scripts/browser*smoke.mjs"], languageOptions: { globals: { ...globals.node, ...globals.browser, chrome: "readonly" } } }
];
