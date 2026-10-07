# Invisible AI Assistant — browser-first v3

Daily use:

- **Text:** select the question and all options → Ctrl+C → Ctrl+Shift+V.
- **Image:** Ctrl+Shift+S → drag around the question → release. Escape or right-click cancels.
- **Result:** a six-pixel pulsing dot becomes a compact answer, 20 CSS pixels from the bottom-right. It disappears after **22 seconds** by default. Ctrl+Shift+H hides it.
- **Hover:** any answer pauses its disappearance timer while hovered; moving away resumes the remaining time. Long answers expand on hover and collapse on leave. Each complete generated code block has a **Copy** button, which copies only code with its original indentation, quotes, tabs and line breaks.
- **Appearance:** small gray text at 55% opacity by default, without panel backgrounds, borders or shadows. Details, code and Copy use the same subtle style.

Nothing is drawn at idle. Ordinary Ctrl+C/V/X/Z remain untouched. The text shortcut reads the clipboard only on explicit invocation; there is no clipboard history or page/screen monitoring.

## Architecture remains unchanged

```text
Chrome / Edge Manifest V3 extension
  → headless Native Messaging helper
  → existing AIService
  → Gemini Web (authenticated session cookies) OR Groq (official API)
  → browser viewport overlay
```

Provider code, authentication, model discovery, credential isolation, cancellation and Windows Credential Manager are reused. There is no fallback between providers or models. No WPF companion, tray, desktop overlay, global keyboard hook, desktop clipboard/capture service, startup registration, localhost server or manual Python worker setup has been restored. The existing one-click per-user installer remains necessary for private Windows credential storage and the maintained Gemini Web runtime.

The helper's unified response retains `content`, `provider`, `model`, `usage`, `finishReason` and adds optional `details` for hover explanations. Internal image MCQ responses include visible option evidence from the multimodal model; the service validates labels before presenting them. Text MCQ labels are checked against the copied question locally. No OCR service or additional provider is introduced.

## Install / upgrade the ready-built release

Requires Windows 10/11 x64 and Chrome/Edge 116+.

1. Use `InvisibleAI/artifacts/browser-first-release`, or extract the Windows build artifact from GitHub Actions. Source Git does not contain the generated executables.
2. Close Chrome/Edge before updating the helper, then double-click **InvisibleAI.Setup.exe**. It installs into `%LOCALAPPDATA%\InvisibleAI\helper` and registers Native Messaging for the current Windows user. Python and .NET runtimes are bundled. No administrator, PowerShell or Extension ID entry is required.
3. Open `chrome://extensions` or `edge://extensions`. Turn on Developer mode and **Load unpacked** the release's **BrowserExtension** folder. If already loaded, keep the same folder and press **Reload** after replacing its files. Accept the added clipboard-write permission if requested; it is used only when you click Copy on generated code.
4. Refresh existing question tabs once so their content scripts update. Open the extension's Settings explicitly; no settings tab opens during answering.
5. Visit `chrome://extensions/shortcuts` or `edge://extensions/shortcuts` and verify **Ctrl+Shift+V**, **Ctrl+Shift+S** and **Ctrl+Shift+H** are assigned to this extension. Change bindings there if the browser/OS reserves a chord.

Loading unpacked is still necessary until a signed Chrome Web Store / Edge Add-ons release is published. This repository does not claim store publication or unattended browser installation.

The installer stops only an obsolete invisible companion at this user's old installed path and removes only its matching legacy startup command. Close a visible old companion settings window before upgrading. It preserves saved credentials/preferences. Do not launch the old `InvisibleAI/app/InvisibleAI.Companion.exe` again.

## First-time provider setup

1. Choose exactly **Gemini Web** or **Groq**.
2. Paste your own Gemini cookies or Groq API key into the password field. Never paste credentials into chat, issues or logs.
3. Click **Connect / Save** to discover available models. Choose the desired model and click **Connect / Save** again to save that selection. Leave the credential blank when reusing a saved credential. Saved values are never returned to the browser.
4. Image questions require a model marked **Images**. Models marked **Text only** remain usable for text, but produce `Selected model does not support images. Please choose an image-capable model.` for screenshots. No silent switching occurs.
5. Choose **Quick** (default) or **Detailed**, and **Auto Detect** or a preferred programming language. These choices are included in every provider request. Choose 12, 22, 30 seconds or Custom (2–120 seconds).
6. Advanced preferences include 50–100% answer-text opacity (default 55%), enable/disable, copied-text, selected-image and network permissions. **Save preferences** saves these without reconnecting. Existing disabled privacy permissions stay disabled during upgrades; enable **Allow selected image processing** here if it was previously off. The previous default 94% background setting migrates to the new 55% text default; customized opacity values are retained.

Gemini Web continues to use the existing `gemini-webapi` Web2API integration and session cookies, not Google's API-key API. Only the required `__Secure-1PSID` and optional `__Secure-1PSIDTS` values are passed to its private worker. Cookies can expire; reconnect with your own current values. Groq uses `https://api.groq.com/openai/v1/` with your own key. Model availability depends on your account and current provider catalog. Known image capability mapping is checked against [Groq's vision documentation](https://console.groq.com/docs/vision); available model IDs are fetched at connection/request time.

## Answers

- Single MCQ, A–F, A–Z and labeled True/False: **only labels**, e.g. `C` or `B`.
- Multiple-correct questions: `A, C, D`, sorted in original question order. Unknown labels or ambiguous output become **Uncertain**.
- Choices copied without letters on separate lines are assigned A–Z in displayed order. For example `Earth / Jupiter / Saturn / Mars` below the planet question gives **B**, not Uncertain. Lowercase labels such as `b)` are accepted and displayed uppercase. Image prompts use the same positional rule when choices have no visible labels.
- Quick normal questions: one concise direct answer.
- Detailed MCQs: the label stays compact; the explanation appears only on hover. Detailed normal questions: focused explanation, compact preview with hover for longer content.
- Programming requests: fenced code in the chosen/inferred language, rendered as code with Copy. Code plus answer options is still an MCQ.
- Incomplete/unreadable image or missing information: **Uncertain**, rather than invented options. AI correctness is not guaranteed.

The compact processing dot and the area outside the answer remain click-through. Every answer has a small hitbox matching its text so hovering can pause the timer, including single-letter MCQs. Details expand on hover, accept scrolling and Copy, then collapse on mouse leave without taking focus automatically. Page elements immediately underneath the answer's small hitbox cannot receive that same pointer event. The timer resumes its remaining duration rather than restarting, and new requests/hiding discard the old timer.

## Capture, privacy and limits

Only an explicit image shortcut starts selection. The page is unchanged once selection finishes. The selection UI and prior answer are removed before capturing, and resize, focus loss, Escape, right-click and pointer cancellation abort selection. The current active tab is verified before and after capture.

The browser's `captureVisibleTab` API captures the visible viewport **in memory**, then the extension crops the selected rectangle using actual screenshot dimensions / CSS viewport dimensions. **Only the crop is sent to the helper/provider.** No screenshot/question/answer/code history or temporary image file is created. Windows clipboard reads may report line endings as CRLF; code line boundaries, indentation, tabs and quotes are preserved. The crop is downscaled only when needed to at most 2048 pixels on each axis and is limited to 5 MiB PNG. Viewport decoding is capped at 40 million pixels / 32 MiB PNG. Text is limited to 50,000 characters. Images retain diagrams/equations/code directly; there is no mandatory OCR.

Canvas/bitmap resources and mutable image arrays are released/cleared after use; decoded helper bytes are zeroed even on cancellation. Completed helper tasks are removed immediately. JavaScript/.NET immutable strings and provider/network-library buffers cannot be guaranteed securely overwritten; references are released for garbage collection. Screenshots are never intentionally persisted locally. Provider-side processing/retention is governed by that provider; Gemini requests continue using temporary chats.

Credentials remain encrypted in the current Windows user's Credential Manager, which is the intentional exception to transient request data: saved provider login is required for reuse. They are isolated by provider, not returned by status/model/test APIs, not placed in URLs or logged. There is no multiuser HTTP database/RLS service. The OS account is the authorization boundary; Chrome/Edge profiles under the same Windows account share helper configuration.

## Fullscreen, capture permission and sharing limitations

F11 stays active: answers are browser page content, and production code never changes fullscreen state, navigates, opens a tab/popup or focuses an answer. DOM fullscreen moves the overlay into `document.fullscreenElement`. Some replaced-element fullscreen surfaces (e.g. video), restricted/managed pages, extension stores and pages removing injected content may prevent display. No protected/DRM/secure-desktop/lockdown restriction is bypassed.

[Chrome's activeTab permission](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab) is granted by an actual assigned extension command or toolbar invocation. The trusted top-frame Ctrl+Shift+V / S page listener is a fallback for shortcut conflicts; a page key event alone **does not grant capture permission**. If image capture is unavailable, assign the command in browser shortcut settings, or explicitly click the extension toolbar icon once on the current page to grant activeTab, then retry. No broad `host_permissions`, debugger permission or automatic permission prompt was added. Focused cross-origin frames may require the actual browser command. Default page fallback chords remain V/S even if browser command bindings are changed.

**Screen-sharing invisibility is not implemented and cannot be promised by this architecture.** A browser viewport overlay can appear in a shared tab, window, screenshot or screen recording. There is no supported extension mechanism to exclude only its DOM pixels from an arbitrary sharing application. No capture-exclusion flag or desktop-overlay architecture was added.

## Exact developer build commands

With .NET SDK 10, Node 22 and Python 3.11 installed, run from the repository root:

```powershell
python -m pip install -r InvisibleAI/LocalHelper/AI/GeminiWeb/requirements.txt -r InvisibleAI/Scripts/requirements-dev.txt
./InvisibleAI/Scripts/Build.ps1
./InvisibleAI/Scripts/Publish.ps1
```

This workspace's bundled tools can instead be used as:

```powershell
./InvisibleAI/Scripts/Build.ps1 -Dotnet "$PWD/.tools/dotnet/dotnet.exe" -Python "$PWD/.tools/gemini-venv/Scripts/python.exe"
./InvisibleAI/Scripts/Publish.ps1 -Dotnet "$PWD/.tools/dotnet/dotnet.exe" -Python "$PWD/.tools/gemini-venv/Scripts/python.exe"
```

Build runs Ruff, Python tests, complete .NET build/tests, npm lint, TypeScript checking and extension unit tests. Publish creates the self-contained helper, bundled Gemini worker, installer and extension under `InvisibleAI/artifacts/browser-first-release`. Generated binaries/runtimes/profiles stay Git-ignored. CI uploads release artifacts; local success does not certify a remote CI run.

## Exact test commands / manual acceptance

```powershell
./InvisibleAI/Scripts/Test-Browser.ps1 -Browser Edge
./InvisibleAI/Scripts/Test-Browser.ps1 -Browser Chrome
```

Use `-Dotnet "$PWD/.tools/dotnet/dotnet.exe"` here when the SDK is not on PATH. The browser harness uses disposable profiles and a real test helper/session/provider abstraction with synthetic upstream responses. It temporarily registers the fixture host and restores the original registration in `finally`. Do not run two harness instances concurrently. Results are recorded under ignored `artifacts/qa` and summarized in `VERIFICATION.md`. This does not prove live AI correctness/authentication or a physical F11 keypress.

Repeat manually in **Chrome normal, Chrome F11, Edge normal, Edge F11**, for each real provider:

1. Copy `What is the capital of France?` with `A. Berlin B. Madrid C. Paris D. Rome`; invoke V. Expect **C** and auto-hide after 22 seconds. Confirm no tab/navigation/focus/fullscreen change.
2. Copy `Python is dynamically typed. A. True B. False`; expect **A**. Use a False question and expect its actual label. Test A–F and A–Z options.
3. Copy `Which are programming languages? A. Python B. Photoshop C. Java D. C++ E. Excel`; expect **A, C, D**.
4. Choose Detailed. Ask an MCQ and a long normal question. Hover: details expand. Leave: compact state. Verify mouse clicks/text selection outside the small hitbox remain normal.
5. Ask `Write a Python program to reverse a string.` Hover and click Copy. Paste into a text editor; confirm only code, exact whitespace/quotes/line breaks, no fences. Test another selected language.
6. On a webpage showing text/MCQ/True-False/multiple answers/equations/diagrams/tables/code/code+options, invoke S and drag around only the question. Expect the selected model's image answer. Test several DPI/zoom values. Escape and right-click must remove selection without sending an image. Select unreadable/incomplete text: expect Uncertain.
7. Select a text-only Groq model and invoke S: expect the image-capability error without switching models. Repeat for any provider model that explicitly lacks images. Test disabled image/text/network preferences.
8. Test empty and image-only clipboard, a long question, normal Ctrl+C/V/X/Z, new requests replacing active requests, hiding a processing request, disconnect/timeout, expired Gemini cookies/invalid Groq key, provider switching and no fallback.
9. Press physical F11 and repeat text AND image tests. Also enter DOM fullscreen on the page and repeat. Screen-shared viewers may see the answer; do not assume exclusion.
10. Restart the browser and confirm it starts the helper automatically and uses saved credentials/preferences without a tray app. Idle must have no visible UI and no uploads.

## Source changes for v3

- `BrowserExtension/manifest.json`, package metadata: region command and explicit code-copy permission.
- `src/background/service-worker.ts`, new `src/background/image.ts`: capture/crop/size limits, screenshot routing, privacy and cancellation.
- `src/content/overlay.ts`, `src/offscreen/clipboard.ts`: temporary region selector, hover card, safe code rendering/copy, fullscreen handling.
- `src/settings/*`, `src/popup/*`, `src/styles.css`: Quick/Detailed, programming language, image model labels, duration presets/custom, opacity, image privacy, shortcut status and instructions.
- `LocalHelper/AI/AnswerPolicy.cs`, `Providers.cs`, `AIService.cs`, `IAIAgent.cs`, new `ImageInput.cs`: strict structured answer policy, validated labels/details and image validation using existing providers.
- `AI/GeminiWebProvider.cs`, `AI/GroqProvider.cs`: selected-model image capability checks without provider/model fallback.
- `LocalHelper/Session.cs`, `Program.cs`, `Settings/*`, `Shared/Protocol/*`: private screenshot transport, buffer/task cleanup, validated preferences and migration.
- `Tests/UpgradeTests.cs`, existing tests, browser unit/integration tests and README/verification documentation.

No previously removed desktop subsystem was restored. Existing authentication, provider routing, secure storage, ordinary text workflow and installer remain.
