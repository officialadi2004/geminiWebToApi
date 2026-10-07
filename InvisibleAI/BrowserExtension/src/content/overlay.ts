(() => {
  const scope = globalThis as typeof globalThis & { invisibleAIInstalled?: boolean };
  if (scope.invisibleAIInstalled || window.top !== window) return;
  scope.invisibleAIInstalled = true;
  document.addEventListener("keydown", event => {
    if (!event.isTrusted || event.repeat || !event.ctrlKey || !event.shiftKey || event.altKey || event.metaKey || !["KeyV", "KeyS"].includes(event.code)) return;
    event.preventDefault();
    void chrome.runtime.sendMessage({ action: event.code === "KeyS" ? "ask-region" : "ask-clipboard" }).catch(() => {});
  }, true);
  let host: HTMLDivElement | undefined, card: HTMLDivElement | undefined;
  let current = "", timer: ReturnType<typeof setTimeout> | undefined, cancelSelection: (() => void) | undefined;
  let remaining = 0, deadline = 0, hovered = false;
  const parent = (): Element => document.fullscreenElement ?? document.documentElement;
  function hide(): void { clearTimeout(timer); timer = undefined; remaining = 0; deadline = 0; hovered = false; cancelSelection?.(); host?.remove(); host = undefined; card = undefined; }
  function resumeExpiry(): void {
    clearTimeout(timer); timer = undefined;
    if (hovered) return;
    if (remaining <= 0) { hide(); return; }
    deadline = performance.now() + remaining;
    timer = setTimeout(hide, remaining);
  }
  function pauseExpiry(): void {
    if (remaining <= 0) return;
    hovered = true;
    if (timer !== undefined) remaining = Math.max(0, deadline - performance.now());
    clearTimeout(timer); timer = undefined;
  }
  function expireAfter(seconds: number): void {
    remaining = Math.max(2, Math.min(120, seconds || 22)) * 1000;
    hovered = card?.matches(":hover") ?? false;
    resumeExpiry();
  }
  function mount(): void {
    if (host) return;
    host = document.createElement("div"); host.id = "invisible-ai-answer";
    host.style.cssText = "all:initial!important;position:fixed!important;right:20px!important;bottom:20px!important;z-index:2147483647!important;pointer-events:none!important;max-width:calc(100vw - 40px)!important;";
    const shadow = host.attachShadow({ mode: "closed" });
    const style = document.createElement("style");
    style.textContent = `:host{pointer-events:none}*{box-sizing:border-box} .card{font:12px/1.4 'Segoe UI',sans-serif;color:#727985;opacity:var(--answer-opacity,.55);background:none;border:0;border-radius:0;box-shadow:none;max-width:min(300px,calc(100vw - 40px));pointer-events:auto;cursor:default} .summary{padding:1px 2px;overflow-wrap:anywhere;white-space:pre-wrap;max-height:70px;overflow:hidden} .mcq .summary{font-size:14px;text-align:center;min-width:12px} .body{display:none;white-space:pre-wrap;overflow-wrap:anywhere;padding:4px 2px;max-height:min(420px,calc(100vh - 60px));overflow:auto} .expanded{max-width:min(480px,calc(100vw - 40px))} .expanded .body{display:block} .text{margin:0 0 6px} .code{position:relative;margin:6px 0;border:0;background:none} .code-head{display:flex;align-items:center;justify-content:space-between;padding:2px 0;color:inherit;font-size:11px} pre{margin:0;padding:2px 0;overflow:auto;white-space:pre;font:12px/1.5 Consolas,monospace;color:inherit} button{font:11px 'Segoe UI',sans-serif;color:inherit;background:none;border:0;padding:2px 4px;cursor:pointer} .processing{padding:0;width:6px;height:6px;border:0;box-shadow:none;border-radius:50%;background:#2aa881;animation:pulse 1s ease-in-out infinite;pointer-events:none} @keyframes pulse{50%{opacity:.15}} @media(prefers-reduced-motion:reduce){.processing{animation:none}}`;
    card = document.createElement("div"); card.className = "card"; card.setAttribute("role", "status"); card.setAttribute("aria-live", "polite");
    const mountedCard = card;
    card.addEventListener("mouseenter", () => { if (card !== mountedCard || card.classList.contains("processing")) return; pauseExpiry(); if (card.classList.contains("expandable")) card.classList.add("expanded"); });
    card.addEventListener("mouseleave", () => { if (card !== mountedCard || card.classList.contains("processing")) return; card.classList.remove("expanded"); hovered = false; resumeExpiry(); });
    shadow.append(style, card); parent().append(host);
  }
  function details(body: HTMLDivElement, text: string): boolean {
    const fences = /```([^\r\n`]*)\r?\n([\s\S]*?)```/g;
    let last = 0, codeFound = false;
    function prose(value: string): void { if (!value.trim()) return; const p = document.createElement("div"); p.className = "text"; p.textContent = value.trim(); body.append(p); }
    for (const match of text.matchAll(fences)) {
      prose(text.slice(last, match.index)); codeFound = true;
      const box = document.createElement("div"); box.className = "code";
      const head = document.createElement("div"); head.className = "code-head";
      const language = document.createElement("span"); language.textContent = match[1].trim() || "Code";
      const button = document.createElement("button"); button.type = "button"; button.tabIndex = -1; button.textContent = "Copy";
      // Exclude only the fence separator newline, preserving code indentation, quotes and tabs.
      const code = match[2].replace(/\r?\n$/, "");
      button.addEventListener("mousedown", e => e.preventDefault());
      button.addEventListener("click", event => {
        if (!event.isTrusted) return;
        void chrome.runtime.sendMessage({ action: "copy-code", text: code }).then(result => {
          if (!result?.ok) throw new Error("Copy failed");
          button.textContent = "Copied ✓"; setTimeout(() => { if (button.isConnected) button.textContent = "Copy"; }, 1400);
        }).catch(() => { button.textContent = "Copy failed"; });
      });
      const pre = document.createElement("pre"); const node = document.createElement("code"); node.textContent = code; pre.append(node); head.append(language, button); box.append(head, pre); body.append(box);
      last = match.index! + match[0].length;
    }
    prose(text.slice(last)); return codeFound;
  }
  function answer(text: string, explanation: string, opacity: number): void {
    mount(); card!.replaceChildren(); card!.className = "card";
    card!.style.setProperty("--answer-opacity", String(Number.isFinite(opacity) ? Math.min(1, Math.max(.5, opacity)) : .55));
    const summary = document.createElement("div"); summary.className = "summary";
    const body = document.createElement("div"); body.className = "body";
    const code = details(body, explanation || text);
    const mcq = /^[A-Z](?:, [A-Z])*$/.test(text);
    summary.textContent = mcq ? text : code ? "Code · hover to view" : text.length > 140 ? text.slice(0, 137) + "…" : text;
    card!.append(summary);
    if (mcq) card!.classList.add("mcq");
    if (explanation || code || text.length > 140) {
      card!.append(body); card!.classList.add("expandable");
    }
  }
  function select(id: string, reply: (value: unknown) => void): void {
    hide(); current = id;
    const layer = document.createElement("div"); layer.id = "invisible-ai-selection";
    layer.style.cssText = "all:initial!important;position:fixed!important;inset:0!important;z-index:2147483647!important;cursor:crosshair!important;pointer-events:auto!important;background:rgba(0,0,0,.012)!important;touch-action:none!important;user-select:none!important;";
    const shadow = layer.attachShadow({ mode: "closed" });
    const rectangle = document.createElement("div"); rectangle.style.cssText = "position:absolute;border:1px solid rgba(65,170,140,.8);background:rgba(65,170,140,.035);pointer-events:none;display:none;box-sizing:border-box;";
    shadow.append(rectangle); parent().append(layer);
    let start: { x: number; y: number } | undefined, done = false;
    const viewport = { width: innerWidth, height: innerHeight, x: scrollX, y: scrollY };
    const clamp = (x: number, limit: number): number => Math.max(0, Math.min(limit, x));
    function finish(region?: { x: number; y: number; width: number; height: number }): void {
      if (done) return; done = true;
      layer.remove(); document.removeEventListener("keydown", escape, true); window.removeEventListener("resize", abort); window.removeEventListener("blur", abort); document.removeEventListener("visibilitychange", visibility); cancelSelection = undefined;
      if (!region) { reply({ cancelled: true }); return; }
      // Let removal paint before the background captures the viewport. Safety timer covers background tabs.
      let sent = false; const send = (): void => { if (sent) return; sent = true; clearTimeout(fallback); reply({ region: { ...region, viewportWidth: viewport.width, viewportHeight: viewport.height } }); };
      const fallback = setTimeout(send, 120);
      requestAnimationFrame(() => requestAnimationFrame(send));
    }
    const abort = (): void => finish();
    const visibility = (): void => { if (document.hidden) finish(); };
    const escape = (e: KeyboardEvent): void => { if (e.key === "Escape") { e.preventDefault(); e.stopImmediatePropagation(); finish(); } };
    document.addEventListener("keydown", escape, true); window.addEventListener("resize", abort); window.addEventListener("blur", abort); document.addEventListener("visibilitychange", visibility);
    cancelSelection = abort;
    layer.addEventListener("contextmenu", e => { e.preventDefault(); finish(); });
    layer.addEventListener("pointerdown", e => {
      e.preventDefault(); e.stopPropagation();
      // Keep the layer until contextmenu so the browser menu cannot open underneath it.
      if (e.button === 2) return;
      if (e.button !== 0) { finish(); return; }
      start = { x: clamp(e.clientX, innerWidth), y: clamp(e.clientY, innerHeight) }; layer.setPointerCapture(e.pointerId);
    });
    function region(e: PointerEvent): { x: number; y: number; width: number; height: number } {
      const x = clamp(e.clientX, innerWidth), y = clamp(e.clientY, innerHeight);
      return { x: Math.min(start!.x, x), y: Math.min(start!.y, y), width: Math.abs(x - start!.x), height: Math.abs(y - start!.y) };
    }
    layer.addEventListener("pointermove", e => {
      if (!start) return; e.preventDefault(); const r = region(e);
      Object.assign(rectangle.style, { display: "block", left: `${r.x}px`, top: `${r.y}px`, width: `${r.width}px`, height: `${r.height}px` });
    });
    layer.addEventListener("pointerup", e => {
      e.preventDefault(); e.stopPropagation(); if (e.button === 2) return; if (!start) { finish(); return; }
      const r = region(e);
      finish(r.width >= 4 && r.height >= 4 && scrollX === viewport.x && scrollY === viewport.y ? r : undefined);
    });
    layer.addEventListener("pointercancel", abort);
  }
  document.addEventListener("fullscreenchange", () => { cancelSelection?.(); if (host) parent().append(host); });
  chrome.runtime.onMessage.addListener((m, sender, reply) => {
    if (sender.id !== chrome.runtime.id || m?.target !== "overlay") return false;
    if (m.state === "hide") { hide(); current = ""; return false; }
    if (m.state === "prepare") { hide(); current = m.id; return false; }
    if (m.state === "select") { select(m.id, reply); return true; }
    if (m.state === "processing") { current = m.id; hide(); mount(); card!.className = "processing"; timer = setTimeout(hide, 330000); return false; }
    if (m.id !== current) return false;
    clearTimeout(timer); answer(String(m.text ?? "").slice(0, 12000), String(m.details ?? "").slice(0, 12000), Number(m.opacity));
    expireAfter(Number(m.seconds));
    return false;
  });
})();
