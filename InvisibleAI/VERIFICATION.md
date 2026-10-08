# Verification record — browser-first update, 7 October 2026

## Follow-up: working code Copy controls and visible feedback, 8 October 2026

Native code controls previously painted their padding with the window transparency key, so mouse clicks in those pixels passed through to the underlying application. Only the small Copy controls now have a faint, non-color-key backing. Copy/checkmark icons, per-block `Copied` feedback for 1.8 seconds and short `Retry` failure feedback were added to the native window and explicit browser-overlay mode. Native clipboard contention retries asynchronously without blocking the hover/protection timer. Code is copied without Markdown fences, preserving indentation, quotes, tabs and line breaks; request replacement/hiding stops pending retries. No provider or capture-exclusion implementation changed.

Executed checks:

- Ruff and **9 Python tests** passed; .NET Release build had zero warnings/errors. Final .NET suite including interactive native tests: **33 passed**. The new Copy regression used OS hit testing at control padding and real mouse clicks; both code blocks copied exactly. It verified per-block success state/reset, unchanged foreground HWND and affinity 0x11, deliberately locked the clipboard, verified failure left its prior synthetic contents intact, then verified successful retry. The original clipboard object and pointer were restored afterward. The test executable now establishes per-monitor DPI awareness for consistent physical mouse/capture coordinates across its threads.
- Extension ESLint, TypeScript checking, production build and **27 tests** passed. New failure assertions ensure an unsuccessful copy never shows a success checkmark; untrusted clicks still do nothing.
- Actual disposable **Edge upgrade integration** passed. A click on the generated code control wrote the expected code (including Windows clipboard CRLF), displayed the SVG checkmark and `Copied` accessible label, reset to `Copy`, and retained the page's focus. Existing browser hover/expiry, controlled image-provider paths, DOM fullscreen and both providers' native private completion checks also passed. Upstreams were synthetic fixtures; physical F11, real image-command capture permission, Chrome integration and Meet recipient video were not tested in this copy-fix run.
- Production publish and quiet installer passed. Installed helper DLL matched the published DLL. Saved settings remained byte-for-byte unchanged; private mode stayed enabled. Updated extension files were built in `BrowserExtension/dist` and copied into the existing release folder. Browser reload/page refresh remains the user's final activation step.

As above, build/publish used an ignored clean validation copy, preserving and excluding the three unrelated unfinished provider/test working-tree edits. No credentials were printed or added to source; the tracked-file credential pattern scan passed.

## Follow-up: capture-excluded answers enabled and hardened, 8 October 2026

This supersedes the optional/default-browser behavior recorded below. New settings and old settings without an explicit privacy choice now default to private Windows responses. An explicit existing opt-out is retained. The installed user profile was explicitly updated through Native Messaging to enable private responses; all other saved provider, appearance and privacy preferences were preserved (apart from the existing preferences-version migration).

The attachment's supported Windows API technique is reused in the existing C# Native Messaging helper. No Python desktop app, WPF companion, tray, startup service, global polling hooks or sharing-app patches were added. Every new/recreated response HWND starts hidden, applies and verifies `WDA_EXCLUDEFROMCAPTURE` before display, checks DWM/affinity while visible and hides on protection loss. Lost topmost state is restored only during its browser workflow without activation. The production helper now uses the Windows GUI executable subsystem, preserving framed stdin/stdout without a console launcher. Browser answers require an explicit privacy opt-out; missing settings cannot silently downgrade a private request.

Executed locally on Windows x64 build 26300:

- Full build/check pipeline passed: Ruff, **9 Python tests**, ESLint, TypeScript checking, **26 extension tests**, and production extension build. Final .NET Release/native UI suite: **32 passed, zero failures**; build had zero warnings/errors. This includes protected HWND creation/recreation, exclusion-loss hiding/recovery, hover pause/resume, no activation, privacy routing and an opaque owned GDI positive-control capture fixture.
- **Actual Chrome and Edge entire-screen `getDisplayMedia` capture**, in disposable fullscreen test profiles: the owned synthetic answer contributed **285 dark pixels** with exclusion disabled, and **0** with exclusion enabled, **0** after restoring exclusion, and **0** in the hidden-window baseline. Capture dimensions were 1920×1080 and the sampled synthetic answer rectangle was 139×25 pixels. The local HWND stayed visible in protected mode with affinity 0x11. Only the owned opaque white fixture area was sampled in memory; no screenshot/video files or provider inputs were saved. Production does not start capture or use the browser's test-only picker switches. This is browser capture evidence, not a Google Meet recipient-video test.
- Actual disposable **Edge upgrade integration** passed: native answer delivery through Native Messaging for both fixture provider paths, no response/processing DOM, unchanged focus/URL/tab count and retained browser fullscreen state; existing hover/code Copy, selection cancellation, controlled image crop and DOM fullscreen checks also passed. Physical F11 and actual screenshot-command capture permission were not verified by this harness.
- Production publish passed for the self-contained GUI helper, bundled Gemini worker, installer and extension. Quiet installer exited successfully and preserved saved settings; the installed helper DLL matched the published DLL. The installed production **WinExe apphost** passed framed Native Messaging settings/status requests with empty stderr. Its private-response preference is enabled.
- **Live installed-helper generation through both Groq and Gemini Web** passed using existing secure credentials and the synthetic France MCQ. Each private completion returned exactly `{privateResponses:true, displayed:true}`, without answer content or credentials. Explicit hide succeeded. The selected provider and all preferences were restored byte-for-byte to the private-enabled baseline; stderr was empty. This tests authenticated generation/private delivery, not the correctness of the hidden answer text.

Validation/publish used an ignored clean copy of committed source plus this feature. The three pre-existing unrelated edits (`LocalHelper/AI/AnswerPolicy.cs`, `LocalHelper/AI/Providers.cs`, `Tests/UpgradeTests.cs`) were preserved and excluded from this release/commit. Their unfinished `Numbered` test reference still prevents the unrelated working-tree variant from building. The release source passes; provider implementations and stored credentials were not changed.

**NOT VERIFIED:** received Google Meet entire-screen video, physical F11, mixed-DPI multiple monitors, other capture/driver/GPU paths, protected/exclusive surfaces or remote CI. Windows capture exclusion does not guarantee protection against every recorder or a camera. README contains the exact browser capture command and the separate second-device Meet acceptance steps. No universal invisibility claim is made.

## Follow-up: optional private Windows answers, 8 October 2026

An optional native response window was added inside the existing Native Messaging helper. Standard browser mode remains the default. Private text/image results return only `privateResponses` and `displayed`, never content/details/code; the extension does not render private processing/answer/error UI in the page and refuses browser fallback. A per-request private flag prevents a concurrent preference change from downgrading an existing private request. Windows exclusion is set/verified while hidden before display/upload. No WPF companion, tray, hooks, capture-monitoring service or sharing-app modification was restored.

Executed on the interactive Windows desktop:

- Full `Build.ps1 -NativeDisplay`: Ruff, nine Python worker tests, **30 .NET tests**, ESLint, TypeScript checking, production extension build and **25 extension tests** passed; zero .NET warnings/errors. Default CI runs the 27 non-interactive .NET tests; three desktop UI/capture tests require the explicit flag.
- Native HWND: `WDA_EXCLUDEFROMCAPTURE` read back as 0x11; compact bounds stayed on the active monitor; no-activate/toolwindow/click-through styles and unchanged foreground HWND verified. Stale answer/hide IDs could not replace/hide the newer answer. Explicit hide and automatic expiry passed.
- Native hover: pointer moved to the synthetic MCQ, response stayed visible past the original two-second expiry, expanded bounds stayed below 50px, and leaving resumed expiry. Original pointer position was restored.
- Real Windows **GDI BitBlt + CAPTUREBLT**: an opaque synthetic fixture covered the captured rectangle. An unexcluded positive-control answer changed the captured pixels; the excluded answer's pixels exactly matched the fixture-only baseline. This captured only owned synthetic pixels in memory and produced no screenshot file.
- Real disposable **Edge** upgrade integration: existing hover styling/timer, code Copy, selection cancellation, controlled image cropping/provider routing and DOM fullscreen passed. Both Gemini and Groq fixture upstreams produced real native-window completions through Native Messaging; no answer/processing overlay appeared in the page, focus/URL/tab count remained unchanged, and fullscreen entered with the browser windows API stayed active. Physical F11 and real screenshot-command capture permission were not verified by this harness.
- Production publish succeeded: self-contained Windows helper with desktop UI runtime, bundled Gemini worker, installer and extension. Updated helper installed quietly; installed helper DLL matched published DLL; saved settings were unchanged.
- **Live installed-helper requests to both Groq and Gemini Web** succeeded using existing securely stored credentials and a synthetic France MCQ. Each private completion contained exactly `{privateResponses:true, displayed:true}`. Explicit native hide succeeded. Original provider/preferences were restored byte-for-byte; Native Messaging stderr was empty. This confirms live generation/private delivery, not the correctness of the private answer's text.

Validation/build/publish used an ignored isolated copy of committed source plus this change. Three pre-existing unrelated working-tree edits (`AI/AnswerPolicy.cs`, `AI/Providers.cs`, `Tests/UpgradeTests.cs`) were preserved and excluded from this commit/release. The existing UpgradeTests edit references a missing `Numbered` method, so that uncommitted working tree cannot currently build; the isolated release source passes. Existing provider implementations/credentials were not changed by this feature.

**NOT VERIFIED:** Google Meet recipient-side entire-screen capture, Chrome integration for this change, physical F11, multiple monitors with mixed DPI, alternate GPU/driver/capture paths, protected/exclusive-fullscreen surfaces or the remote CI run. Microsoft does not guarantee exclusion against all capture methods. The local GDI result is not proof of Google Meet invisibility. Follow README's second-device recipient-view checklist in Chrome and Edge before relying on private mode.

## Earlier browser-first verification

Test environment: Windows x64 build 26300, .NET SDK 10.0.401/runtime 10.0.11, Node 22.19.0, Python 3.11.9, gemini-webapi 2.1.1, PyInstaller 6.19.0. Installed Edge 154.0.4258.62 and Chrome 154.0.8037.98 were identified before testing. This record supersedes the previous WPF/tray/capture verification record.

| Check actually executed | Result |
| --- | --- |
| .NET Release build/type checks | Passed; zero warnings/errors |
| TypeScript checking and ESLint | Passed |
| Python Ruff | Passed |
| .NET provider/helper/protocol/security tests | 18 passed, 0 failed |
| Python Gemini worker contract/privacy tests | 9 passed, 0 failed |
| Extension unit tests | 7 passed, 0 failed |
| Extension production build | Passed |
| Self-contained Windows helper publish, bundled Gemini worker, single-file installer | Passed |
| Actual installer execution with --quiet | Exit 0; installed for current Windows user |
| Installed helper's actual Native Messaging stdio PING | Passed: framed metadata-only response; stderr empty |
| Installed bundled Gemini worker with missing cookies | Passed: sanitized auth error; stderr empty; no network request |
| Actual Edge extension + fixture helper + reused provider adapters | Passed, six integration groups below |
| Physical F11 keypress in Chrome/Edge | NOT TESTED |
| Full Chrome integration | NOT VERIFIED; test environment blocked loading/startup paths |
| Authenticated Gemini/Groq generation with real user credentials | NOT TESTED |
| Actual second Windows account credential access | NOT TESTED |
| Remote GitHub Actions run | Separate from local results; must be checked independently |

## Real Edge browser integration

`Scripts/Test-Browser.ps1 -Browser Edge` loaded the built extension into a disposable Edge profile and registered a test-only Native Messaging executable. It restored the prior registration on completion. The actual extension copied/read synthetic question text, called the helper's Session/AIService and real provider adapters, and rendered responses. The upstream HTTP/worker transports used deterministic fixtures; no Google/Groq request was made.

Passed groups:

1. Groq settings, discovered models, cleared password field, MCQ A–D, A–F, labeled True/False, normal and long copied questions.
2. Gemini Web settings, discovered models, cleared password field and the same five copied-text cases.
3. Browser fullscreen **entered through the extension windows API**: answer displayed inside the same viewport, current window state remained fullscreen, URL/tab count/focus stayed unchanged.
4. Empty and image-only clipboard: sanitized short errors; response expiry.
5. Normal Ctrl+C/V/X/Z in editable fields.
6. Request replacement/cancellation: newer answer won and an old request did not overwrite it.

Every question flow checked a processing dot, 20-pixel corner placement (within DPI rounding), pointer-events:none, underlying click hit testing/clicking, no answer focus theft, one unchanged page tab/URL, and automatic disappearance. Closed-shadow DOM was inspected through the browser testing protocol; it was not exposed by production page code.

The fixture executable and synthetic credentials are test source only and are not shipped in the helper package. Its configuration/profile/output files are Git-ignored. A normal Windows Credential Manager test used a disposable unique target and removed it afterward. The installed production helper has no fixture mode.

## Limits of the evidence

Computer Use access to Microsoft Edge was denied, so no physical F11 keypress was automated. The extension API fullscreen state was exercised; this is not a claim that the exact keyboard F11 scenario was tested. Complete manual F11 acceptance steps are in README.md.

Installed Chrome ignored command-line unpacked extension loading; its extension debug loading method was unavailable. A downloaded Chrome for Testing 145.0.7632.6 failed to start with a Windows side-by-side assembly error. Neither attempt is counted as a passing Chrome integration test. Chrome unit API mocks and the shared MV3 code do not substitute for that missing browser test.

Both provider paths are independently covered with production adapters and mocked upstreams, including switching both ways, no fallback, credential isolation/redaction, clean missing/invalid auth errors, timeouts/rate limits/model availability, unified results and no console logs. These tests cannot establish a real account's session validity, quota, model access or live answer correctness. No personal credentials were supplied or accessed through browser-cookie extraction. Only synthetic text/image clipboard fixtures were used.

Windows Credential Manager encrypts per-user credentials. Fixed targets and rejection of caller user IDs/credential targets are tested, but a second real Windows login was not exercised. There is no database or public HTTP server, so SQL RLS does not apply. Saved credentials are never returned to extension UI, inserted into URLs or stored in browser localStorage.

The installer was successfully run locally. It leaves the production helper registered under the current user's Chrome/Edge registry keys; no companion/tray/startup process runs. Test browser processes were closed and test registrations were restored before the production installer smoke. The installer is unsigned; store publication and managed-browser deployment remain outstanding distribution work.

## Follow-up: legacy shortcut conflict

The user's report of the old "Settings → AI" message was traced to a running, previously installed `InvisibleAI.Companion.exe`. Its cached settings and global hotkeys coexisted with the new helper. The saved helper configuration already had Groq and a valid model. The obsolete invisible process was stopped without modifying credentials or settings. No legacy startup entry was present on this machine.

After retirement, the configured **real Groq account** passed the backend connection/catalog test and answered a synthetic France MCQ with **C** using `openai/gpt-oss-20b`. The result used the unified structure, and helper stderr remained empty. No real clipboard text was read for this diagnostic, and no credential was printed or returned. This adds authenticated Groq evidence; authenticated Gemini and physical F11 browser tests remain unverified. The earlier table describes the initial update's test run.

The installer now retires only the current user's installed old companion path and matching startup command. It leaves unrelated executables/startup values and the new helper alone, and asks for an old visible window to be closed rather than discarding unsaved UI. A regression test verifies these path/command boundaries. The .NET suite now has 19 passing tests.
## 2026-10-08 — browser-first v3 upgrade verification

The browser-first architecture remains intact. No WPF/tray/desktop overlay/global hook/startup service was restored. New production paths are region selection/cropping in the extension, private SCREENSHOT_INPUT framing, existing provider image methods, structured MCQ/code answers, hover details and validated preferences.

Executed locally:

- Full `Scripts/Build.ps1` with this workspace's .NET 10 SDK and Python 3.11 virtual environment: Ruff passed, **9 Python tests**, **24 .NET test groups**, extension lint/type checking and **18 extension unit tests** passed (**51 automated tests/groups**). Solution build completed with zero warnings/errors. The final answer-policy edge cases were followed by another 24/24 .NET run.
- Production `Scripts/Publish.ps1`: self-contained Windows x64 helper, pinned Gemini worker bundle, installer and MV3 extension produced under `artifacts/browser-first-release`. Installer upgrade was not run against the currently open user browsers; the new release must be installed/reloaded using README steps.
- Complete Edge disposable-profile browser smoke run passed: both provider choices with synthetic upstream transports; MCQ/A-F/True-False/multiple-answer/normal/long text, empty/non-text clipboard, Ctrl+C/V/X/Z, request replacement, processing indicator, expiry, viewport position, click-through short answers and page focus, browser fullscreen state via extension API, Detailed hover expansion/collapse, 94% background, generated code and Copy, Escape/right-click cancellation and DOM fullscreen. The right-click selector was fixed to consume contextmenu before removal; cancellation does not open an underlying browser menu.
- Additional Edge upgrade run passed the **controlled capture** path independently for Groq and Gemini: a synthetic page PNG held in memory replaced captureVisibleTab *only in the disposable test worker*, then the production OffscreenCanvas crop, Native Messaging image handler, existing provider adapters with fixture upstreams and overlay returned C. The capture function was restored in finally; no screenshot file was created. This is not a successful test of captureVisibleTab permission itself.
- Real authenticated provider tests were also executed independently, using the existing Windows Credential Manager and synthetic inputs only. **Both Groq and Gemini Web** returned C for France, A for labeled True/False, A, C, D for multiple answers, C plus a nonempty Detailed explanation, a Python fenced code block and C for a generated PNG MCQ. Groq text used the saved openai/gpt-oss-20b; its synthetic image test explicitly used an available qwen/qwen3.8-27b in a cloned test configuration. Gemini used an account-discovered Web model. These checks did not change the saved provider/model/credentials, read real clipboard questions, capture user screens, write image files or print credential values. Gemini used the maintained Web session client, not an official Gemini API key.
- Browser code-copy test verified code/indentation/quotes and line boundaries. Windows clipboard reads canonicalized LF to CRLF; tests now assert the Windows result explicitly. The offscreen unit test verifies exact supplied text without an echoed code payload. Copy confirmation returns to Copy after its short timer.
- Security/provider tests verify write-only credentials, sanitized errors/output, provider isolation, no fallback, caller field rejection, privacy gates, PNG/size validation and buffer cleanup paths. The extension test verifies disabled text permission prevents even an explicit clipboard read. Native input byte arrays are zeroed and decoded image arrays are zeroed in finally. Immutable runtime strings cannot be guaranteed securely overwritten; README states this limitation.

Not certified / remaining manual acceptance:

- **Physical F11 keypresses in Chrome and Edge** were not executed. API-entered browser fullscreen and real DOM fullscreen were tested in Edge; they are not reported as physical F11 tests.
- **Physical Ctrl+Shift+S → captureVisibleTab** remains a manual acceptance test. Edge reported the command assigned to Ctrl+Shift+S, but CDP key injection missed delivery / did not grant activeTab. Selection was then exercised through the real extension content context. The real capture API returned the documented capture-permission error. No broad host permission or production test bypass was introduced; README explains actual assigned browser command / toolbar activation requirements.
- **Chrome for Testing** was attempted with the complete smoke harness and failed before startup with `browserType.launchPersistentContext: spawn UNKNOWN`. No Chrome integration assertion passed. The fixture Native Messaging registration was restored by the wrapper's finally block. Installed Chrome normal/F11 must be tested manually; this environment failure is not a passing Chrome test.
- Actual selected-region image semantics for MCQ/True-False/multiple answers/equations/diagrams/tables/code/code+options, unreadable images and varied real DPI/zoom values require the manual matrix in README. Automated tests cover the structured formats, crop math and provider transports; real provider image verification covered the generated France MCQ.
- **Screen-sharing exclusion is not implemented.** DOM overlays can be visible in shared browser tabs/screens. No supported general-purpose extension mechanism excludes those pixels; no desktop/capture-exclusion architecture was restored.
- Existing disabled screenshot/network/clipboard preferences stay disabled. Old default 12-second duration migrates to 22; v3 saved explicit durations, including 12 seconds, remain unchanged. Saved credentials remain intentionally encrypted in Windows Credential Manager; question/image/answer/code history is not persisted by the application.

See README for changed file groups, exact build/install steps and the four-browser-mode manual acceptance matrix. Browser fixture JSON under ignored artifacts/qa identifies live providers as NOT TESTED *within that fixture run*; the separate real-provider checks above were executed and passed.

Release-binary follow-up: launched the freshly published self-contained `artifacts/helper/InvisibleAI.Helper.exe` with the registered extension origin and private framed POST provider-test messages. Both Groq and Gemini returned connected:true. The Gemini path used the packaged GeminiWorker.exe rather than a separately installed Python interpreter. Stdout contained only protocol responses, stderr was empty, and saved settings were not changed. The tracked-source credential-pattern scan passed for all 72 scanned files without printing credential values; staged whitespace checks passed.

## Follow-up: correct answers rejected for unlabeled choices

Reproduced the user's exact synthetic largest-planet example with the configured real Gemini Web provider. Gemini returned a valid structured MCQ with answer B, but the prior answer policy found no literal A/B/C/D labels in `Earth / Jupiter / Saturn / Mars` and changed B to Uncertain. This was local validation failure, not failed authentication.

The policy now recognizes a separate unlabeled choice block after a question/options heading and validates positionally assigned A–Z labels. Lowercase and parenthesized labels are normalized, answer footers are excluded from option detection, unlabeled True/False is supported, and an ordinary structured answer is not forced into MCQ merely because it has a list. Unknown labels still fail validation. The shared Gemini/Groq prompt explicitly instructs positional labeling for unlabeled text and image choices.

Executed: full lint/type checking/build and 52 automated tests/groups (9 Python, 25 .NET, 18 extension), followed by another 25/25 .NET run after the final parser changes. Both real providers returned B for the user's unlabeled planet question, A for unlabeled True/False, A, C, D for unlabeled multiple-correct choices, a Detailed label/explanation, Python code, and B for an in-memory generated unlabeled planet PNG. The Groq image model was explicitly selected in cloned test settings; saved settings/credentials were unchanged. No real clipboard contents or user screenshots were accessed. Browser/F11/physical capture limitations above still apply.

Production publish succeeded. The fixed release was installed for the current user by stopping only that installed headless helper and running the rebuilt installer with --quiet; user browsers were left open. Installed Helper.dll was verified to match the new publish. The installed, packaged Native Messaging executable then returned B for the unlabeled planet text and A, C, D for unlabeled programming-language choices using the saved Gemini selection. Stderr remained empty; tests made no configuration/credential changes. The extension reconnects automatically on its next invocation, so this backend-only fix needs no extension reload on this machine. Credential scan and whitespace checks passed.

## Follow-up: hover pauses expiry; subtle borderless answers

Answer expiry now tracks its remaining duration using a monotonic clock. Hovering any answer, including Quick MCQ labels, pauses the timer; leaving resumes the remaining time instead of restarting. Details/code still expand only on hover. Replacement/hide discard old timers and detached card events, while the processing watchdog continues normally. Only the answer's text-sized hitbox accepts pointer events; the processing dot and surrounding page remain click-through.

Answer, explanation, code and Copy styling now uses small gray text, no card/code/button background, no borders/separators or shadows, with default text opacity 55%. Advanced opacity controls text visibility. Preference version 4 migrates the prior default 94% card setting to 55% text while retaining custom opacity, duration and privacy preferences.

Executed: full Build.ps1 passed Ruff, Python (9), .NET (25), extension lint/type checking and extension tests (21): **55 automated tests/groups**, zero build warnings/errors. New virtual-clock tests verify repeated hover, remaining-duration resume, replacement/hide, detached events and watchdog separation. Migration tests verify default and custom opacity preservation. A real disposable Edge upgrade smoke passed: short MCQ held past its two-second original expiry while hovered and auto-hidden on leave without focus theft; computed text opacity was 0.55, background transparent, borders 0px and shadow none; details hover/collapse, code Copy, cancellation, controlled image routing and DOM fullscreen also passed. Native fixture registry registration was restored by finally. Physical F11, real capture permission and Chrome limitations above still apply; no new live AI requests were required for this presentation change.

Publish.ps1 succeeded and refreshed the release extension/installer. The rebuilt helper was installed quietly for the current user; its DLL hash matched the published helper and the saved settings file hash stayed unchanged. Installed Native Messaging PING verified the user's customized opacity/duration were preserved and stderr was empty. Existing browser tabs must be refreshed after reloading the extension to replace their old content script; no browser was closed automatically.

## Follow-up: smaller collapsed preview

The collapsed browser answer now uses 10px text, one line, and a maximum width of 180px. Normal text is previewed at up to 40 characters with an ellipsis and code uses the single-word Code cue. The full answer/code remains in the hover body; hover font sizes, timer pause/resume, opacity and borderless styling remain. Physically clipped short text also enables hover expansion, so wide characters or narrow viewports do not make an answer inaccessible.

Executed extension ESLint, TypeScript checking, production extension build and all 22 extension tests: passed. The new behavioral regression verifies truncation does not lose the complete answer and multiple-choice labels retain their content. The built extension was copied to the browser-first release folder. This extension-only update did not rebuild/install the helper or alter existing unrelated provider edits. No new physical browser/F11 test was executed for this size change.
