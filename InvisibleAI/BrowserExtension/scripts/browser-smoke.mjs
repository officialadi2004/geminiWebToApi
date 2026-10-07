// Test-only fixture host must be registered by Scripts/Test-Browser.ps1.
import assert from "node:assert/strict";
import { createServer } from "node:http";
import { mkdtemp, mkdir, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { chromium } from "playwright";
const edge = process.argv.includes("--edge");
const qa = resolve("../artifacts/qa"), extension = resolve("dist");
await mkdir(qa, { recursive: true });
const server = createServer((req,res) => { res.setHeader("Content-Type","text/html"); res.end(`<html><head><title>Invisible AI Smoke</title></head><body><textarea id="question" style="width:600px;height:220px"></textarea><textarea id="paste"></textarea><button id="under" style="position:fixed;right:20px;bottom:20px;width:100px;height:50px" onclick="this.textContent='Clicked'">Underlying button</button></body></html>`); });
await new Promise(r=>server.listen(0,"127.0.0.1",r));
let context;
const results=[];
try {
 context = await chromium.launchPersistentContext(await mkdtemp(qa+"/browser-"), { ...(edge?{executablePath:"C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"}:{}),headless:false,viewport:null,args:["--disable-extensions-except="+extension,"--load-extension="+extension] });
 const worker=context.serviceWorkers()[0]||await context.waitForEvent("serviceworker",{timeout:10000});
 const page=context.pages()[0];const url=`http://127.0.0.1:${server.address().port}`;await page.goto(url);
 // This grant belongs only to the disposable test profile and synthetic localhost page.
 await context.grantPermissions(["clipboard-read","clipboard-write"],{origin:url});
 const session=await context.newCDPSession(page);
 async function inspect() {
  const doc=await session.send("DOM.getDocument",{depth:-1,pierce:true});
  function find(n) {if(n.attributes?.includes("invisible-ai-answer")) return n;for(const child of [...(n.children??[]),...(n.shadowRoots??[])]){const hit=find(child);if(hit)return hit;}}
  const host=find(doc.root),label=host?.shadowRoots?.[0]?.children?.find(n=>n.nodeName==="DIV");
  return {text:label?.children?.map(n=>n.nodeValue).join("")??"",state:label?.attributes?.[label.attributes.indexOf("class")+1],host:!!host};
 }
 async function until(check) {for(let i=0;i<360;i++){const value=await inspect();if(check(value))return value;await page.waitForTimeout(50);}throw new Error("Overlay did not reach expected state");}
 async function prepare(text) {await page.locator("#question").fill(text);await page.locator("#question").selectText();await page.keyboard.press("Control+c");}
 async function ask(text, expected) {
  await prepare(text); await page.keyboard.press("Control+Shift+v");
  await until(v=>v.state==="processing"); await until(v=>v.text===expected);
  assert.equal(page.url(),url+"/");assert.equal(context.pages().length,1);
  const ui=await page.evaluate(()=>{const host=document.getElementById("invisible-ai-answer"),r=host.getBoundingClientRect();return {focus:document.activeElement.id,pointer:getComputedStyle(host).pointerEvents,right:innerWidth-r.right,bottom:innerHeight-r.bottom,hit:document.elementFromPoint(r.right-2,r.bottom-2).id};});
  assert.equal(ui.focus,"question");assert.equal(ui.pointer,"none");assert.equal(ui.hit,"under");assert.ok(Math.abs(ui.right-20)<1 && Math.abs(ui.bottom-20)<1);
  await page.locator("#under").click();assert.equal(await page.locator("#under").textContent(),"Clicked");
  await until(v=>!v.host);
 }
 async function connect(provider) {
  const settings=await context.newPage();await settings.goto(worker.url().replace("src/background/service-worker.js","src/settings/index.html"));await settings.locator("#provider").selectOption(provider);
  await settings.locator("#credential").fill(provider==="Groq"?"synthetic-groq-sentinel":"__Secure-1PSID=synthetic-gemini-sentinel; __Secure-1PSIDTS=synthetic-ts-sentinel");
  await settings.locator("summary").click(); await settings.locator("#duration").fill("2"); await settings.locator("#save").click();await settings.waitForFunction(()=>document.getElementById("status").textContent.includes("Connected"));assert.equal(await settings.locator("#credential").inputValue(),"");assert.ok(await settings.locator("#model option").count()>0);await settings.close();await page.bringToFront();
 }
 await page.waitForTimeout(200);assert.equal((await inspect()).host,false);
 for(const provider of ["Groq","Gemini Web"]) {
  await connect(provider);
  await ask("What is the capital of France?\nA. Berlin\nB. Madrid\nC. Paris\nD. Rome","C");
  await ask("Python is dynamically typed.\nA. True\nB. False","A");
  await ask("A-F question\nA. first\nB. second\nC. third\nD. fourth\nE. fifth\nF. sixth","F");
  await ask("Explain binary search in one sentence.","Binary search repeatedly halves a sorted search range.");
  await ask("Long question: "+"context ".repeat(2500)+"France?\nA. Berlin\nB. Madrid\nC. Paris\nD. Rome","C");
  results.push(provider+": settings, MCQ, A-F, True/False, normal and long copied text");
 }
 // Browser fullscreen state is equivalent UI state; a physical F11 key remains a manual check.
 await worker.evaluate(()=>new Promise(r=>chrome.windows.getCurrent(w=>chrome.windows.update(w.id,{state:"fullscreen"},r))));
 await ask("France?\nA. Berlin\nB. Madrid\nC. Paris\nD. Rome","C");
 const windowState=await worker.evaluate(()=>new Promise(r=>chrome.windows.getCurrent(r)));assert.equal(windowState.state,"fullscreen");results.push("Browser fullscreen via extension API: answer, no navigation/new tabs/focus theft, stays fullscreen");
 for(const mode of ["empty","image"]) {
  if(mode==="empty") await page.evaluate(()=>navigator.clipboard.writeText(""));
  else await page.evaluate(async()=>{const canvas=document.createElement("canvas");canvas.width=canvas.height=1;const png=await new Promise(resolve=>canvas.toBlob(resolve,"image/png"));await navigator.clipboard.write([new ClipboardItem({"image/png":png})]);});
  await page.locator("#question").focus();await page.keyboard.press("Control+Shift+v");await until(v=>v.text.includes("Clipboard has no plain text") || v.text.includes("Could not read clipboard text"));await until(v=>!v.host);
 }
 results.push("Empty and non-text clipboard: clean error");
 await prepare("ordinary clipboard");await page.locator("#paste").focus();await page.keyboard.press("Control+v");assert.equal(await page.locator("#paste").inputValue(),"ordinary clipboard");await page.keyboard.press("Control+a");await page.keyboard.press("Control+x");assert.equal(await page.locator("#paste").inputValue(),"");await page.keyboard.press("Control+z");assert.equal(await page.locator("#paste").inputValue(),"ordinary clipboard");results.push("Normal Ctrl+C/V/X/Z unchanged");
 await prepare("France?\nA. Berlin\nB. Madrid\nC. Paris\nD. Rome");await page.keyboard.press("Control+Shift+v");await until(v=>v.state==="processing");await page.waitForTimeout(220);await prepare("Python?\nA. True\nB. False");await page.keyboard.press("Control+Shift+v");await until(v=>v.text==="A");await page.waitForTimeout(700);assert.equal((await inspect()).text,"A");results.push("Request replacement/cancellation ignores superseded result");
 await writeFile(qa+`/browser-${edge?"edge":"chrome"}.json`,JSON.stringify({browser:edge?"Edge":"Chrome for Testing",results,physicalF11:"NOT TESTED",liveProviders:"NOT TESTED"},null,2));
 for(const result of results)console.log("PASS "+result);
} finally {if(context)await context.close();await new Promise(r=>server.close(r));}
