import assert from "node:assert/strict";
export async function run({page,worker,context,connect,prepare,inspect,results}) {
 const url=page.url();
 for(const provider of ["Groq","Gemini Web"]) {
  await connect(provider,{privateResponses:true,seconds:2});
  await worker.evaluate(()=>new Promise(r=>chrome.windows.getCurrent(w=>chrome.windows.update(w.id,{state:"fullscreen"},r))));
  await prepare("What is the capital of France?\nA. Berlin\nB. Madrid\nC. Paris\nD. Rome");
  const count=await worker.evaluate(()=>globalThis.privateCompletions.length);
  await page.keyboard.press("Control+Shift+v");
  const deadline=Date.now()+15000;
  while(await worker.evaluate(()=>globalThis.privateCompletions.length)===count) {
   assert.equal((await inspect()).host,false,"Private response appeared in browser DOM");
   assert.ok(Date.now()<deadline,"Private native completion was not received");
   await page.waitForTimeout(50);
  }
  const response=await worker.evaluate(()=>globalThis.privateCompletions.at(-1));
  assert.deepEqual(response,{privateResponses:true,displayed:true});
  assert.equal((await inspect()).host,false);
  assert.equal(await page.evaluate(()=>document.activeElement.id),"question");
  assert.equal(page.url(),url);assert.equal(context.pages().length,1);
  assert.equal((await worker.evaluate(()=>new Promise(r=>chrome.windows.getCurrent(r)))).state,"fullscreen");
  await page.keyboard.press("Control+Shift+h");
  results.push(provider+": real private HWND completion through Native Messaging, no answer in DOM, focus/navigation/fullscreen unchanged (fixture upstream)");
 }
 await connect("Groq",{privateResponses:false,seconds:2});
 results.push("Google Meet recipient view NOT TESTED; Windows GDI exclusion tested independently");
}
