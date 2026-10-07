# Invisible AI Assistant

A Windows tray companion and Chrome/Edge Manifest V3 extension. **The user's screen comes first.** Idle means no visible overlay. An explicit request shows a six-DIP pulsing dot, then a compact, click-through answer at the same bottom-right anchor. There is no permanent toolbar, chat panel, keyboard hook, clipboard watcher, or screen monitor.

## Quick start with the built files

1. Load `BrowserExtension/dist` as an unpacked extension in **Chrome → chrome://extensions** or **Edge → edge://extensions**, with Developer mode enabled. Copy its 32-character extension ID. The extension's settings page also displays the ID.
2. Open PowerShell in `artifacts/win-x64`. Run `./Install.ps1 -ExtensionId 'YOUR_EXTENSION_ID'`. It copies the app to `%LOCALAPPDATA%\InvisibleAI\app`, registers the native host for the current user in Chrome and Edge, and creates a Start menu shortcut. Administrator rights are not needed. Supply both browser IDs as an array if they differ: `-ExtensionId @('CHROME_ID','EDGE_ID')`.
3. Start `InvisibleAI.Companion.exe` or use the Start menu shortcut. It starts in the system tray. Double-click its icon → **AI** and explicitly choose **Gemini Web** or **Groq**. Enter your own credential in the password box, then **Save / Connect**. Choose an available model and **Save settings**. Blank credential input preserves the saved credential; saved values are never loaded into the UI. There is no default provider or automatic fallback.
4. For **Gemini Web**, install Python 3.11+ and run `./Setup-Gemini.ps1 -PackageDirectory "$env:LOCALAPPDATA\InvisibleAI\app"` from the package. This creates a private runtime at the final installed location. Paste your own Gemini Cookie request header containing `__Secure-1PSID` and optionally `__Secure-1PSIDTS`; unrelated cookies are discarded. Cookies are sensitive account credentials. No Google API key is used. To obtain your own cookies, inspect a Gemini request's Cookie header in your browser's developer tools while signed into your own account; never share it or put it in Git. The app does not extract browser cookies automatically.
5. Open the extension popup and reconnect. Select text on an ordinary webpage and use **Ask about selected text** or its selection context menu. For any ordinary desktop application, copy text normally, then use the clipboard shortcut.

The framework-dependent package requires the **.NET 10 Desktop Runtime** of matching architecture. The inspected host has Windows build 26300, Chrome, Edge, Node 22.19.0, desktop runtime 10.0.11, and the workspace-local SDK 10.0.401. Microsoft's registry retains a legacy product label on this host; the build number is the relevant OS identifier. See [supported Windows versions and runtime installation](https://learn.microsoft.com/dotnet/core/install/windows).

For development without copying the package, run `Scripts/Install.ps1 -PackageDirectory ./artifacts/win-x64 -ExtensionId 'YOUR_EXTENSION_ID' -RegisterOnly`. Keep that publish directory at a stable absolute path. The generated native host manifest belongs beside both executables. Re-run registration after changing the extension ID or app location. No browser registration has been performed automatically in this workspace.

## Workflows

| Shortcut (configurable) | Action |
| --- | --- |
| Ctrl+Shift+V | Read copied plain text and ask AI |
| Ctrl+Shift+S | Temporarily select a screen rectangle; release to capture |
| Ctrl+Shift+H | Hide the response, close expansion, cancel selection/request |
| Ctrl+Shift+A | Enable/disable the assistant |
| Ctrl+Shift+E | Expand the last response in a normal, selectable window |

Normal Ctrl+C/V/X/Z work as usual. The app registers only its explicit combinations using `RegisterHotKey` with no-repeat, and never installs a low-level keyboard hook. Windows or another app may reserve a shortcut; the tray reports a conflict and Settings → Keyboard lets you change it. At startup, available shortcuts keep working even if another shortcut is occupied. Registration during settings updates is transactional: a conflict preserves the prior working shortcuts.

**Text:** select → Ctrl+C → Ctrl+Shift+V → dot → concise response → auto-hide. The clipboard is read without writing to it. Empty, non-text, busy, or oversized clipboard content produces a compact error. The text limit is 50,000 characters.

**Screen:** Ctrl+Shift+S → drag → release → selection windows close → selected rectangle captured → dot → response. Escape/right-click cancels; Hide also cancels. Only this temporary mode captures mouse input. Captures span monitor boundaries in physical pixels, including negative monitor coordinates. The limits are 16 megapixels and 5 MiB of PNG data. No OCR is required.

The dot and compact response never activate a window or take keyboard focus. The default response is click-through, including scrolling/selection underneath. **Make response interactive** explicitly enables click-to-expand. Expanded responses are ordinary windows opened only by a click, shortcut, or tray action; their text can be selected/copied normally and web-search sources are available as links.

## Settings and tray

Tray actions: enable/disable, send clipboard, select screen, expand last response, settings, exit. General settings control enable state, Windows startup, minimized launch, and tray mode. Disabling tray mode opens the ordinary settings window; the normal operating mode is tray mode with minimized startup.

Appearance controls dot size/opacity/pulse speed, response opacity/font/width/duration, right/bottom offsets, monitor, and response interactivity. Sizes/offsets are device-independent pixels, scaled per monitor. **Active** uses the foreground application's monitor at text invocation or the selected region's center monitor for captures. The response retains the request's anchor even if focus moves while waiting. **Primary** and explicit monitor device names are supported; removed monitors fall back safely, and positioning is clamped to the current work area above the taskbar.

AI settings: exactly two providers, account-discovered model selection, password credential entry, Save / Connect, Test Connection, Refresh Models, Remove Credential, SHORT/CONCISE/DETAILED (default CONCISE), optional web search, answer character cap, output token budget, and timeout. The compact card shows up to 240 characters/five text lines; expansion exposes the full answer up to the configured cap. Multiple-choice labels and ambiguity are handled through the AI instructions; the assistant never chooses an option mechanically or claims calibrated confidence.

Privacy controls independently disable text/clipboard, screenshots, and all AI network requests. Saving settings cancels pending work. Screenshots are always memory-only, so clearing temporary screenshots is locked on. There is no permanent screenshot/history feature in this MVP.

## Build

Install a .NET 10 SDK, Node 22.13+ (tested on 22.19), npm, and Python 3.11+. WPF is Windows-only. The native projects have no third-party NuGet dependencies; TypeScript dependencies are pinned by `package-lock.json`.

From `InvisibleAI`:

```powershell
python -m pip install -r ./WindowsCompanion/AI/GeminiWeb/requirements.txt -r ./Scripts/requirements-dev.txt
python -m ruff check --config ./ruff.toml ./WindowsCompanion/AI/GeminiWeb ./Tests/test_gemini_worker.py
python -m unittest discover -s ./Tests -p "test_*.py" -v
dotnet build ./InvisibleAI.slnx -c Release
dotnet run --project ./Tests/InvisibleAI.Tests.csproj -c Release --no-build
Set-Location ./BrowserExtension
npm ci
npm run lint
npm run check
npm test
```

Or run `./Scripts/Build.ps1` for Python lint/tests, strict C# compilation, Windows tests, ESLint, TypeScript checks, and extension production build/tests. Install the Python requirements first. Use `-Python path/to/python.exe` for a virtual environment and `-UiTests` on an interactive desktop. In this workspace the SDK also lives at `../.tools/dotnet/dotnet.exe`: `./Scripts/Build.ps1 -Dotnet ../.tools/dotnet/dotnet.exe -Python ../.tools/gemini-venv/Scripts/python.exe -UiTests`. Run desktop tests from an ordinary interactive user session, outside restrictive sandboxes, with no other companion instance using the same pipe.

Publish from `InvisibleAI`:

```powershell
./Scripts/Publish.ps1
# Optional runtime-inclusive package:
./Scripts/Publish.ps1 -SelfContained
# Optional ARM64 target (test on ARM64 before distribution):
./Scripts/Publish.ps1 -Runtime win-arm64 -SelfContained
```

Output goes to `artifacts/<runtime>`. Both executables and the `AI/GeminiWeb` worker directory must stay together. `Setup-Gemini.ps1` is included; create the Gemini runtime after installation because virtual environments are not relocatable. Groq requires no Python runtime at application runtime. Native Messaging uses standard input/output, so the bridge is a separate console executable launched hidden by the browser; the companion is a WPF WinExe. Avoid single-file/trimming changes without retesting WPF and native-host launch. Framework-dependent x64 publish is verified here; ARM64 and self-contained builds have not been exercised.

## Testing

```powershell
# Desktop integration checks (brief synthetic UI, no paid API requests):
dotnet run --project ./Tests/InvisibleAI.Tests.csproj -c Release --no-build -- --ui
# Actual published companion + native-host smoke test; fresh publish folder only:
node ./Scripts/SmokePublished.mjs ./artifacts/win-x64
```

The dependency-free Windows test executable verifies framing with fragmented Unicode input, invalid/oversized/truncated messages, settings validation/persistence, hotkey protection/registration, official Groq request construction, Gemini Web2API worker contracts, model discovery/validation, image input, optional automatic search, privacy gates, cancellation, safe error reporting, response bounds, geometry, real named pipes, the actual native executable, caller-origin rejection, Credential Manager using an isolated disposable test credential, tiny window size, no activation, click-through, interactive opt-in, response expiry, screen-selection cancellation, controller request supersession, PNG buffer cleanup, capture of exactly an 80×60 synthetic fixture, both-direction provider switching through real Native Messaging, credential rejection, connection APIs, settings Save/Connect/model discovery and masked credential fields. UI-only renders of the settings/response are saved under `artifacts/qa`; no user desktop screenshot is saved by tests. The optional published smoke test uses synthetic text with network disabled and an isolated workspace profile.

Python tests load the pinned Gemini dependency and verify authenticated-session enforcement, sanitized errors, disabled logging/cache/browser-cookie discovery, memory-only image uploads and cleanup, temporary chats, and subprocess output without network access.

Extension tests verify the MV3 package and minimal permissions, explicit selection extraction/password exclusion, versioned envelopes, background Native Messaging/correlation, sender validation, allowed settings updates, and disconnect reporting.

**Verification:** see [VERIFICATION.md](VERIFICATION.md) for the exact executed checks and limitations. Both adapters are exercised independently with deterministic transports, including generation through the real native pipe and the actual Python subprocess. No user cookies/API keys were accessed and no authenticated/paid provider request was made. Configure your own credentials securely and perform the live checks below. GitHub Actions builds and publishes Windows/extension artifacts on push; interactive desktop checks run locally.

Manual acceptance checks:

1. At idle, check that only the tray icon is present. Select/drag/scroll/copy/paste in a browser and text editor.
2. Copy “What is the time complexity of binary search?” and invoke Ctrl+Shift+V. Confirm dot-only processing, compact O(log n), preserved focus and clipboard, and auto-hide.
3. Select a table/diagram/math question with the screen shortcut. Verify selection immediately disappears on release/cancel, and mouse control returns. Test drag across monitors and repeat at 100%, 125%, 150%, and 200% DPI.
4. Click/scroll/select under the default response. Enable interactivity and check explicit click-to-expand and the expand shortcut.
5. Invoke another request while one is processing. Confirm the previous request is cancelled and cannot replace the new answer. Hide/toggle-off also cancel work.
6. Test empty/image-only/busy clipboard, network off, missing/invalid Groq key, expired/invalid Gemini cookies, unavailable model, timeout, rate limits, inaccessible browser pages/iframes, and disconnected native host. Confirm no credential appears in errors or browser responses.
7. Connect **Gemini Web** with your own cookies, select a discovered model, and run one text and screenshot request. Switch to **Groq**, connect your own key, choose a text or vision model as appropriate, and repeat. Switch back and confirm separate credentials/models are retained. Test Connection checks authentication/catalog availability; a generation request additionally verifies model permissions and quota. A failed provider never falls back.
8. With a separate Windows account, confirm each account has its own settings/credentials and cannot access the other's pipe or Credential Manager values. This is an OS account boundary, not a shared web-user database.
9. Test maximized, borderless, browser fullscreen, monitor removal, taskbar relocation, startup, and Chrome/Edge separately. Ordinary overlays are best-effort; protected surfaces remain unavailable. Mixed-DPI/hardware/fullscreen combinations require manual coverage beyond the available desktop tests.

## Architecture

```text
InvisibleAI/
├─ BrowserExtension/       TypeScript, MV3, popup, settings, explicit content extraction
├─ WindowsCompanion/
│  ├─ Core/                Single-instance controller, cancellation, Win32 interop
│  ├─ AI/                  IAIAgent → AIService → GeminiWebProvider / GroqProvider + private Python worker
│  ├─ Clipboard/           Explicit, read-only STA clipboard workflow
│  ├─ ScreenCapture/       Temporary per-monitor selectors and region PNG capture
│  ├─ Overlay/             Tiny non-activating window, DPI/work-area placement, expansion
│  ├─ Hotkeys/             RegisterHotKey lifecycle and validation
│  ├─ NativeMessaging/     User/session-local pipe server + separate stdio Host project
│  ├─ Settings/            MVVM settings UI, atomic JSON preferences, Credential Manager
│  └─ Tray/                Background lifecycle and explicit menu actions
├─ Shared/Protocol/        Versioned JSON, schema, bounded little-endian framing, identity
├─ Tests/                  Independent subsystem and desktop integration checks
└─ Scripts/                Build, publish, install, unregister, published smoke test
```

The extension sends framed JSON to `com.invisibleai.assistant`. Its native host verifies the registered extension origin and forwards through an asynchronous, current-user-only named pipe scoped to the Windows session. A mutex permits one companion per user/session even with Chrome and Edge connected. The host launches the companion if needed. Its stdout contains only protocol frames. No localhost HTTP listener, browser API key, wildcard extension origins, or elevated/UIAccess process is used.

Messages use `{version:1,type,id,payload}` with correlation IDs. Supported types are `TEXT_INPUT` (`text`), `SCREENSHOT_INPUT` (`imageBase64`, PNG), `PROCESSING_START`, `PROCESSING_COMPLETE` (`text`, `sources`, `status`, `result`), `ERROR` (`message`), `SETTINGS_UPDATE`, `PING`, `OPEN_SETTINGS`, `PROVIDER_API`, and `PROVIDER_RESULT`. Input frames are capped at 8 MiB and output at 1 MiB; partial reads are handled correctly. Browser settings updates are restricted to provider, response mode and web search; credentials and privacy/appearance/hotkeys stay in the companion UI. See `Shared/Protocol/schema.json` and [Chromium Native Messaging registration/framing](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging).

`IAIAgent` retains its text, image and multimodal async methods with cancellation. `AIService` routes exclusively to the selected `IAIProvider`. The controller, clipboard, selection and overlay share the same result abstraction. `PROCESSING_COMPLETE.result` contains `{content, provider, model, usage, finishReason}` (provider IDs `gemini`/`groq`); Gemini usage is null because Web2API does not supply token usage. No credentials are included.

**Gemini Web2API:** no existing Gemini implementation was present, so the private Python worker uses maintained [HanaokaYuzu/Gemini-API](https://github.com/HanaokaYuzu/Gemini-API), pinned `gemini-webapi==2.1.1`. It receives cookies through private stdin, never process arguments/environment or files, and adapts an OpenAI-style messages array to the library's `generate_content`. Images are in-memory PNGs; chats are temporary. Authenticated account status is required; guest sessions cannot generate. The library's logging, browser-cookie discovery, disk cache read/write and background refresh are disabled. Small version-specific integration hooks are tested against the pinned package: review them and rerun tests before upgrading. Session renewal is manual by pasting updated cookies. The worker is not a public HTTP server. The Python executable can be configured in Settings or `INVISIBLEAI_GEMINI_PYTHON`; `.env.example` documents only this non-secret path, and no `.env` file is loaded.

**Groq:** requests use the official [Groq API](https://console.groq.com/docs/api-reference) at `https://api.groq.com/openai/v1/chat/completions` and `GET /models`, with the saved key in the Authorization header only. Available active chat models are discovered from your account; known audio-only models are excluded. [Vision](https://console.groq.com/docs/vision) and [browser-search](https://console.groq.com/docs/tool-use/built-in-tools/browser-search) capabilities are annotated for documented models and checked before requests. Search tools use automatic choice, so ordinary conceptual questions can answer directly. Retired Compound systems are excluded. New capability IDs require updating the adapter annotations; the model list itself is never an invented static dropdown. Groq model access/quotas depend on the user's account. HTTP redirects are disabled. There are no automatic retries/fallbacks.

### Provider APIs and authorization

This existing deployment is a local WPF companion with authenticated Native Messaging, **not a web application or database**. The required provider paths are local RPC routes transported over that current-user-only pipe, not HTTP endpoints. No unauthenticated localhost listener or artificial multi-user database has been added.

```json
{"version":1,"type":"PROVIDER_API","id":"connection-check","payload":{"method":"POST","path":"/api/ai/providers/gemini/test"}}
```

Routes: `POST /api/ai/providers/gemini/test`, `POST /api/ai/providers/groq/test`, and each provider's `POST .../models`. Responses use `PROVIDER_RESULT`. Successful test payload: `{"connected":true,"provider":"gemini"}`. Failure includes only `connected:false`, provider and a sanitized error. Models contain IDs, labels and capability flags. GET, credential retrieval, unknown routes, user-ID overrides and extra credential payload fields are rejected. Credential writes/removal occur only through the Windows password UI. Chrome/Edge only select the provider, test its saved connection and open that UI.

Windows Credential Manager and `PipeOptions.CurrentUserOnly`, with user SID/session identity, enforce the local account boundary. The provider credential target is fixed internally; a client cannot choose another user's target. SQL RLS does not apply because there is no database. All same-user applications share the Windows trust boundary; this does not isolate secrets from malicious software running as that same user or an administrator.

## Security, privacy and limitations

Credentials are stored separately under `InvisibleAI/gemini` and `InvisibleAI/groq` in Windows Credential Manager, encrypted by Windows for the signed-in account. Non-secret preferences alone are written to `%LOCALAPPDATA%\InvisibleAI\settings.json`. A blank password box never loads the saved secret. Cookies/key values are not sent back to the browser, settings JSON, response DTOs, logs, URLs, analytics or Git. The Gemini worker receives only its essential session cookies; Groq only receives its key. Credentials accidentally echoed by a provider in answer text are redacted before display/serialization.

Upgrades preserve appearance, hotkeys and privacy. An old OpenAI selection is cleared so the user chooses one of the two providers; the obsolete `InvisibleAI/OpenAI` credential is never read and can be removed manually. Neither provider has an app-owned shared account. There is no silent clipboard/screen capture or upload. PNGs are held in memory, cleared on success/cancellation/failure, and never saved by the app. A dependency closing an upload buffer also wipes it. The last answer remains in process memory for explicit expansion until replacement or exit.

Temporary Gemini chats do not guarantee zero provider retention; each provider's policies apply. Optional search runs at the selected provider, which can use its search partners. The desktop app does not fetch citation URLs; source links open only after an explicit click. Gemini Web uses an unofficial maintained client and can break when the web service changes; use Test Connection and update the pinned integration after testing. An expired session produces “Gemini session expired. Please update your Gemini cookies.” Invalid Groq authentication produces “Invalid Groq API key. Please check your key and try again.” Raw upstream messages/headers/traces are discarded. Memory erasure of all managed-string/runtime copies is not guaranteed; at-rest encryption and process lifetime bound persistence.

No DRM, anti-cheat, secure desktop, lockdown, protected surface, or application-security bypass is implemented. Topmost overlays are ordinary Windows windows and may be blocked/occluded by exclusive fullscreen or protected applications. Input desktop checks reject unavailable/non-default desktops. Protected captures may be blank; the AI is instructed to report ambiguity/unreadable input. The app does not attempt alternate privileged capture methods. Browser internal pages, store pages, PDFs, and inaccessible frames may block selection extraction; the normal clipboard workflow is the fallback where copying is permitted.

This is a functioning MVP with real native/AI integration, packaged for local installation. Store publication, enterprise deployment, code signing, installer signing, model availability, and a complete device compatibility matrix are release tasks beyond this build. Never distribute an API key with a package.

## Remove

Disconnect/remove the extension, exit the companion, and run `Uninstall.ps1` from the app package. It removes only this app's Chrome/Edge registration, startup entry, and Start menu shortcut. App files/settings/credentials are preserved for deliberate removal. Remove `%LOCALAPPDATA%\InvisibleAI` and the `InvisibleAI/gemini`, `InvisibleAI/groq` (and obsolete `InvisibleAI/OpenAI`, if present) generic credentials through Windows Credential Manager if desired.
