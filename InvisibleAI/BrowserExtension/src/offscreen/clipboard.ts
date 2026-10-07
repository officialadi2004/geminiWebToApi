chrome.runtime.onMessage.addListener((request, sender, reply) => {
  if (sender.id !== chrome.runtime.id || request?.target !== "clipboard" || request?.action !== "read") return false;
  const field = document.getElementById("clipboard") as HTMLTextAreaElement;
  field.value = ""; field.focus(); field.select();
  try {
    const pasted = document.execCommand("paste");
    reply({ ok: pasted, text: pasted ? field.value : "" });
  } catch { reply({ ok: false }); }
  finally { field.value = ""; field.blur(); }
  return false;
});
