// Invoked only by an explicit popup action, using activeTab; never watches selection or clipboard.
(() => {
  const focused = document.activeElement;
  if (focused instanceof HTMLInputElement && focused.type === "password") return "";
  if (focused instanceof HTMLTextAreaElement || (focused instanceof HTMLInputElement && /^(text|search|url|email|tel)$/.test(focused.type))) {
    return focused.value.slice(focused.selectionStart ?? 0, focused.selectionEnd ?? 0).slice(0, 50001);
  }
  return (window.getSelection()?.toString() ?? "").slice(0, 50001);
})();
