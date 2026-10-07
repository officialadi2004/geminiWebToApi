# Verification record — browser-first update, 7 October 2026

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
