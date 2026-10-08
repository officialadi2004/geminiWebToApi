import assert from "node:assert/strict";
export async function run({page,worker,session,extensionContexts,connect,prepare,inspect,until,ask,results}) {
 async function hover(){const r=await page.locator("#invisible-ai-answer").boundingBox();await page.mouse.move(r.x+r.width-3,r.y+r.height-3);}
 function find(n,predicate){if(!n)return;if(predicate(n))return n;for(const c of [...(n.children??[]),...(n.shadowRoots??[])]){const match=find(c,predicate);if(match)return match;}}
 async function hide(){await worker.evaluate(()=>chrome.tabs.query({active:true,currentWindow:true}).then(([tab])=>chrome.tabs.sendMessage(tab.id,{target:"overlay",state:"hide"})));}
 await connect("Groq",{mode:"Detailed",seconds:8});
 await prepare("France?\nA. Berlin\nB. Madrid\nC. Paris\nD. Rome");await page.keyboard.press("Control+Shift+v");await until(v=>v.text==="C");
 assert.ok(!(await inspect()).state.includes("expanded"));await hover();assert.ok((await inspect()).state.includes("expanded"));
 let tree=(await inspect()).tree;
 const body=find(tree,n=>n.attributes?.includes("body"));assert.ok(body);
 const {object}=await session.send("DOM.resolveNode",{backendNodeId:body.backendNodeId});
 const style=await session.send("Runtime.callFunctionOn",{objectId:object.objectId,functionDeclaration:"function(){const s=getComputedStyle(this.parentElement);return {display:getComputedStyle(this).display,text:this.textContent,bg:s.backgroundColor,opacity:s.opacity,border:s.borderTopWidth,shadow:s.boxShadow};}",returnByValue:true});
 assert.equal(style.result.value.display,"block");assert.ok(style.result.value.text.includes("Paris"));assert.equal(style.result.value.bg,"rgba(0, 0, 0, 0)");assert.equal(style.result.value.opacity,"0.55");assert.equal(style.result.value.border,"0px");assert.equal(style.result.value.shadow,"none");
 await page.mouse.move(10,10);assert.ok(!(await inspect()).state.includes("expanded"));results.push("Detailed MCQ label first, hover expands/collapses, 55% subtle text without background/borders/shadow");
 await hide();await connect("Groq",{seconds:2});await prepare("France?\nA. Berlin\nB. Madrid\nC. Paris\nD. Rome");await page.keyboard.press("Control+Shift+v");await until(v=>v.text==="C");
 await hover();await page.waitForTimeout(2500);assert.equal((await inspect()).text,"C");assert.equal(await page.evaluate(()=>document.activeElement.id),"question");await page.mouse.move(10,10);await until(v=>!v.host);
 results.push("Short MCQ hover pauses expiry past its original deadline; leaving resumes and auto-hides without focus theft");
 await connect("Groq",{seconds:10});await prepare("Write a Python program to reverse a string.");await page.keyboard.press("Control+Shift+v");await until(v=>v.text==="Code");await hover();
 tree=(await inspect()).tree;const button=find(tree,n=>n.nodeName==="BUTTON");assert.ok(button);
 const box=await session.send("DOM.getBoxModel",{backendNodeId:button.backendNodeId}), quad=box.model.border;
 await page.mouse.click((quad[0]+quad[4])/2,(quad[1]+quad[5])/2);await page.waitForTimeout(200);
 assert.equal(await page.evaluate(()=>navigator.clipboard.readText()),'s = input("Enter string: ")\r\nprint(s[::-1])');assert.equal(await page.evaluate(()=>document.activeElement.id),"question");
 const copyNode=(await session.send("DOM.resolveNode",{backendNodeId:button.backendNodeId})).object;
 async function copyState(){return (await session.send("Runtime.callFunctionOn",{objectId:copyNode.objectId,functionDeclaration:"function(){return {label:this.textContent,aria:this.getAttribute('aria-label'),path:this.querySelector('svg path')?.getAttribute('d')};}",returnByValue:true})).result.value;}
 const copied=await copyState();assert.equal(copied.label,"Copied");assert.equal(copied.aria,"Code copied");assert.equal(copied.path,"M2 8l4 4L14 3");
 await page.waitForTimeout(1900);assert.equal((await copyState()).label,"Copy");results.push("Generated code Copy preserves exact code, shows copy/checkmark icons and Copied confirmation, then resets without page focus loss");
 await hide();
 await worker.evaluate(()=>new Promise(r=>chrome.windows.getCurrent(w=>chrome.windows.update(w.id,{state:"fullscreen"},r))));
 await connect("Groq",{seconds:2,model:"account-vision-model"});
 console.log("Image shortcut binding: "+JSON.stringify(await worker.evaluate(()=>chrome.commands.getAll().then(c=>c.find(x=>x.name==="ask-region")))));
 async function startSelection(){
  await page.locator("#question").focus();await page.keyboard.press("Control+Shift+s");
  try {await page.waitForFunction(()=>document.getElementById("invisible-ai-selection"),{},{timeout:2500});}
  catch {
   console.log("CDP image-key delivery unavailable; overlay state: "+(await inspect()).text);
   results.push("Image keyboard shortcut NOT TESTED by CDP; selection exercised via the extension content context");
   assert.ok(extensionContexts.length,"Extension isolated context missing");
   await session.send("Runtime.evaluate",{contextId:extensionContexts.at(-1),expression:'void chrome.runtime.sendMessage({action:"ask-region"})'});
   await page.waitForFunction(()=>document.getElementById("invisible-ai-selection"));
  }
 }
 for(const cancel of ["Escape","right-click"]) {
  await startSelection();
  if(cancel==="Escape")await page.keyboard.press("Escape");else await page.mouse.click(250,150,{button:"right"});
  assert.equal(await page.locator("#invisible-ai-selection").count(),0);
  assert.equal(await page.evaluate(()=>document.hasFocus()),true);
 }
 results.push("Selection mode: Escape and right-click remove temporary UI");
 for(const provider of ["Groq","Gemini Web"]) {
  await connect(provider,{seconds:2,model:provider==="Groq"?"account-vision-model":"gemini-account-model"});
  await prepare("France?\nA. Berlin\nB. Madrid\nC. Paris\nD. Rome");await startSelection();
  await page.mouse.move(50,50);await page.mouse.down();await page.mouse.move(550,270);await page.mouse.up();assert.equal(await page.locator("#invisible-ai-selection").count(),0);
  const image=await until(v=>v.text==="C"||v.text.includes("Capture unavailable"));
  if(image.text==="C")results.push(provider+": real captureVisibleTab, region cropping and fixture multimodal answer inside browser fullscreen");
  else {
   results.push(provider+": selection/cancellation passed; real capture NOT TESTED because CDP shortcut does not grant activeTab — manual browser command required");
   await hide();
   // Test-only controlled capture: exercise real browser bitmap/canvas cropping and native image routing.
   // This does not certify captureVisibleTab permission or the physical keyboard shortcut.
   const png=await page.screenshot({type:"png"});
   await worker.evaluate(data=>{globalThis.originalCapture=chrome.tabs.captureVisibleTab;chrome.tabs.captureVisibleTab=async()=>data;},"data:image/png;base64,"+png.toString("base64"));
   try {
    await startSelection();await page.mouse.move(50,50);await page.mouse.down();await page.mouse.move(550,270);await page.mouse.up();await until(v=>v.text==="C");
    results.push(provider+": controlled capture PNG → real OffscreenCanvas crop → native helper → fixture multimodal answer");
   } finally {png.fill(0);await worker.evaluate(()=>{chrome.tabs.captureVisibleTab=globalThis.originalCapture;delete globalThis.originalCapture;});}
  }
  await hide();
 }
 await page.evaluate(()=>{const button=document.createElement("button");button.id="dom-fullscreen";button.textContent="Fullscreen";button.onclick=()=>document.documentElement.requestFullscreen();document.body.append(button);});
 await page.locator("#dom-fullscreen").click();await page.waitForFunction(()=>!!document.fullscreenElement);
 await page.waitForTimeout(700);
 await ask("France?\nA. Berlin\nB. Madrid\nC. Paris\nD. Rome","C");assert.equal(await page.evaluate(()=>!!document.fullscreenElement),true);results.push("DOM fullscreen: overlay inside fullscreenElement, no fullscreen exit");
}
