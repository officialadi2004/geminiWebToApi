chrome.runtime.onMessage.addListener((request, sender, reply) => {
  if (sender.id !== chrome.runtime.id || request?.target !== "clipboard" || !["read", "write"].includes(request?.action)) return false;
  const field = document.getElementById("clipboard") as HTMLTextAreaElement;
  field.value = ""; field.focus(); field.select();
  try {
    if (request.action === "write") {
      if (typeof request.text !== "string" || request.text.length > 12000) { reply({ ok: false }); return false; }
      field.value = request.text; field.select(); reply({ ok: document.execCommand("copy") });
    } else {
      const pasted = document.execCommand("paste"); reply({ ok: pasted, text: pasted ? field.value : "" });
    }
  } catch { reply({ ok: false }); }
  finally { field.value = ""; field.blur(); }
  return false;
});
