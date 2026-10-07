(() => {
  const scope = globalThis as typeof globalThis & { invisibleAIInstalled?: boolean };
  if (scope.invisibleAIInstalled) return;
  scope.invisibleAIInstalled = true;
  // Page fallback for browser command conflicts; only this explicit chord is handled.
  document.addEventListener("keydown", event => {
    if (!event.isTrusted || event.repeat || !event.ctrlKey || !event.shiftKey || event.altKey || event.metaKey || event.code !== "KeyV") return;
    event.preventDefault();
    void chrome.runtime.sendMessage({ action: "ask-clipboard" }).catch(() => { /* Extension reloaded. */ });
  }, true);
  let host: HTMLDivElement | undefined, label: HTMLDivElement | undefined;
  let current = "", timer: ReturnType<typeof setTimeout> | undefined;
  function hide(): void { clearTimeout(timer); host?.remove(); host = undefined; label = undefined; }
  function mount(): void {
    if (host) return;
    host = document.createElement("div"); host.id = "invisible-ai-answer";
    host.style.cssText = "all:initial!important;position:fixed!important;right:20px!important;bottom:20px!important;z-index:2147483647!important;pointer-events:none!important;max-width:calc(100vw - 40px)!important;contain:content!important;";
    const shadow = host.attachShadow({ mode: "closed" });
    const style = document.createElement("style");
    style.textContent = `:host{pointer-events:none}div{box-sizing:border-box;font:13px/1.4 'Segoe UI',sans-serif;color:#fff;background:rgba(30,42,36,.93);border-radius:6px;padding:8px 11px;max-width:min(320px,calc(100vw - 40px));max-height:min(180px,calc(100vh - 40px));overflow:hidden;overflow-wrap:anywhere;white-space:pre-wrap;pointer-events:none}div.mcq{font-size:16px;min-width:30px;text-align:center;padding:5px 9px}div.processing{padding:0;width:6px;height:6px;border-radius:50%;background:#2aa881;animation:pulse 1s ease-in-out infinite} @keyframes pulse{50%{opacity:.15}} @media(prefers-reduced-motion:reduce){div.processing{animation:none}}`;
    label = document.createElement("div"); label.setAttribute("role", "status"); label.setAttribute("aria-live", "polite");
    shadow.append(style, label); (document.fullscreenElement ?? document.documentElement).append(host);
  }
  // Also support page-initiated DOM fullscreen where the top layer excludes siblings.
  document.addEventListener("fullscreenchange", () => { if (host) (document.fullscreenElement ?? document.documentElement).append(host); });
  chrome.runtime.onMessage.addListener((m, sender) => {
    if (sender.id !== chrome.runtime.id || m?.target !== "overlay") return false;
    if (m.state === "hide") { hide(); current = ""; return false; }
    if (m.state === "processing") { current = m.id; hide(); mount(); label!.className = "processing"; timer = setTimeout(hide, 330000); return false; }
    if (m.id !== current) return false;
    mount(); clearTimeout(timer);
    const text = String(m.text ?? "").slice(0, 600);
    label!.textContent = text; label!.className = /^[A-Z]{1,3}$/.test(text) ? "mcq" : "";
    timer = setTimeout(hide, Math.max(2, Math.min(60, Number(m.seconds) || 12)) * 1000);
    return false;
  });
})();
