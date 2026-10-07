# Verification record — 7 October 2026

Tested on the supplied Windows desktop, build 26300, x64, with .NET SDK 10.0.401, desktop runtime 10.0.11, Node 22.19.0 and Python 3.11.9. Existing companion/extension were modified in place; overlay, capture, clipboard, shortcuts and tray remain in their original components.

| Check | Result |
| --- | --- |
| .NET Release solution compilation/type checking | Passed, zero warnings/errors |
| ESLint / Python Ruff | Passed |
| TypeScript type checking | Passed |
| Windows provider/subsystem/UI tests | 25 passed, 0 failed |
| Gemini worker contract/privacy tests | 9 passed, 0 failed |
| Browser MV3 tests | 7 passed, 0 failed |
| Extension production build | Passed |
| Framework-dependent Windows x64 publish | Passed |
| Packaged Setup-Gemini.ps1 and pinned Python dependency | Installed successfully; actual published worker returned sanitized auth error for empty input, without network access |
| Actual published companion + native executable | Framed handshake, both provider test routes and disabled-network gates passed |
| Synthetic region capture | Exactly 80×60 PNG; fixture pixels verified |
| Tiny overlay | Tiny size, no activation, default click-through, optional interaction, hidden idle/expiry verified |
| Gemini/Groq settings layouts | WPF-generated UI renders visually inspected; models and masked credential entry exercised |
| Installer scripts | All PowerShell scripts parsed; browser registry installation not executed |
| Credential scan | Tracked source scanned for real-key/cookie/private-key patterns; no matches; local secret/runtime/build files ignored |

Both production provider adapters are exercised independently with deterministic HTTP/worker transports. Tests cover switching both ways, no automatic fallback, provider-specific credential reads, missing/invalid credentials, sanitized failures, discovered models, multimodal payloads, automatic Groq browser-search payloads, response structure/redaction, POST-only routes and credential/user-override rejection. A real native-pipe controller test routes generation through AIService and each real adapter with mocked upstream transports, rather than only testing UI labels.

The actual pinned gemini-webapi package is imported in worker tests and in real private subprocesses. Tests verify its API contract, disabled cookie-cache hooks/logging/browser discovery, rejection of unauthenticated sessions, temporary chats, PNG uploads in memory and cleanup when the library closes a buffer or fails. Worker tests make no Google requests. The packaged runtime/setup script is tested separately from the development environment.

No user cookies, API keys, real clipboard contents or user screenshots were accessed. No authenticated/paid Gemini or Groq generation was performed: live account/model/quota behavior must be verified after the user securely configures their own credentials. Connection testing verifies authentication and catalog availability; generation additionally tests model permission/quota. No app-owned account exists.

Windows Credential Manager and current-user-only pipes provide OS-account isolation; tests verify fixed provider targets, user/session pipe identity and rejection of client-supplied user/credential targets. Cross-account tests with a second actual Windows login were not executed. There is no web server/database, so SQL RLS is not applicable; provider POST paths are authenticated local Native Messaging RPC routes.

Native tests used an isolated disposable Credential Manager entry. Screen tests captured only their own synthetic fixture. UI renders are ignored local QA artifacts. The browser service worker was exercised with mocked Chrome APIs and valid production build output; the unpacked extension was not loaded into the user's browser.

Mixed-DPI monitor arrays, physical cross-monitor dragging, fullscreen/protected surfaces, startup registration, ARM64, self-contained distribution and authenticated provider calls require the README's manual acceptance checks. GitHub Actions is configured for Windows build/tests/publish; its remote run is separate from these locally verified results. No protected-surface bypass is present.

Test/smoke processes exited. No companion was left running and no native-host registry entries were created. The development SDK/venvs and local published package are excluded from Git.
