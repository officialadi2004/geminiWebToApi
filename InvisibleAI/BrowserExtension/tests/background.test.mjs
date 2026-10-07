import test from "node:test";
import assert from "node:assert/strict";
const listeners={}, messages=[], displays=[];
let reads=0, clipboardText="France? A. Berlin B. Madrid C. Paris D. Rome", contexts=[];
function event(name){return {addListener(fn){listeners[name]=fn;}};}
globalThis.chrome={runtime:{id:"own",getURL:p=>`chrome-extension://own/${p}`,getContexts:async()=>contexts,ContextType:{OFFSCREEN_DOCUMENT:"OFFSCREEN_DOCUMENT"},sendMessage:async()=>{reads++;return {ok:true,text:clipboardText};},onMessage:event("ui"),connectNative(){return {onMessage:event("native"),onDisconnect:event("disconnect"),disconnect(){},postMessage(m){messages.push(m);if(m.type!=="CANCEL") queueMicrotask(()=>{listeners.native({version:1,type:"PROCESSING_START",id:m.id});listeners.native({version:1,type:"PROCESSING_COMPLETE",id:m.id,payload:m.type==="TEXT_INPUT"?{result:{content:"C"},responseSeconds:2}:{provider:"Groq",credentialSaved:true}});});}};}},offscreen:{Reason:{CLIPBOARD:"CLIPBOARD"},async createDocument(){contexts=[{}];}},commands:{onCommand:event("command")},tabs:{async query(){return [{id:7}];},async sendMessage(id,m){displays.push(m);},onRemoved:event("removed")},scripting:{async executeScript(o){assert.deepEqual(o.files,["src/content/overlay.js"]);}},action:{async setBadgeText(){},async setTitle(){}}};
await import("../dist/src/background/service-worker.js");
const sender={id:"own",url:"chrome-extension://own/src/settings/index.html"};
function invoke(action,values){return new Promise(resolve=>listeners.ui({action,values},sender,resolve));}
const settle=()=>new Promise(resolve=>setTimeout(resolve,220));
test("Idle never reads clipboard; only AI command reads and displays without navigation",async()=>{
 assert.equal(reads,0);assert.equal(messages.length,0);
 listeners.command("irrelevant");await settle();assert.equal(reads,0);
 listeners.command("ask-clipboard");await settle();assert.equal(reads,1);assert.equal(messages[0].payload.text,clipboardText);assert.equal(displays[0].state,"processing");assert.equal(displays.at(-1).text,"C");
});
test("Empty or non-text clipboard produces short UI error without provider request",async()=>{
 clipboardText="";const count=messages.filter(m=>m.type==="TEXT_INPUT").length;
 listeners.command("ask-clipboard");await settle();assert.equal(displays.at(-1).state,"error");assert.equal(messages.filter(m=>m.type==="TEXT_INPUT").length,count);
});
test("Only extension settings/popup can save; unknown fields and user override rejected",async()=>{
 assert.equal(listeners.ui({action:"save"},{id:"own",url:"https://example.com"},()=>assert.fail()),false);
 assert.equal((await invoke("save",{provider:"Groq",userId:"other"})).ok,false);
 assert.equal((await invoke("save",{provider:"Groq",credential:"synthetic-test-only"})).ok,true);
 assert.equal(messages.at(-1).type,"CONNECT");assert.equal((await invoke("status")).data.provider,"Groq");
});
test("Hide cancels current request and removes overlay",async()=>{
 clipboardText="question";listeners.command("ask-clipboard");await settle();listeners.command("hide-answer");await settle();assert.equal(displays.at(-1).state,"hide");assert.equal(messages.at(-1).type,"CANCEL");
});
