import test from "node:test";
import assert from "node:assert/strict";
const listeners={}, messages=[], displays=[];
let reads=0, captures=0, clipboardText="France? A. Berlin B. Madrid C. Paris D. Rome", contexts=[], selectionCancelled=false, screenshotEnabled=true, clipboardEnabled=true, privateResponses=false, invalidPrivateAck=false, omitPrivatePreference=false;
const png=Uint8Array.from(Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhS8AAAAASUVORK5CYII=","base64"));
let closed=0;
globalThis.createImageBitmap=async()=>({close(){closed++;}});
globalThis.OffscreenCanvas=class {constructor(width,height){this.width=width;this.height=height;}getContext(){return {drawImage(){}};}async convertToBlob(){return new Blob([png],{type:"image/png"});}};
function event(name){return {addListener(fn){listeners[name]=fn;}};}
globalThis.chrome={runtime:{id:"own",getURL:p=>`chrome-extension://own/${p}`,getContexts:async()=>contexts,ContextType:{OFFSCREEN_DOCUMENT:"OFFSCREEN_DOCUMENT"},sendMessage:async r=>{if(r.action==="write")return {ok:true};reads++;return {ok:true,text:clipboardText};},onMessage:event("ui"),connectNative(){return {onMessage:event("native"),onDisconnect:event("disconnect"),disconnect(){},postMessage(m){messages.push(m);if(m.type!=="CANCEL") queueMicrotask(()=>{listeners.native({version:1,type:"PROCESSING_START",id:m.id});listeners.native({version:1,type:"PROCESSING_COMPLETE",id:m.id,payload:["TEXT_INPUT","SCREENSHOT_INPUT"].includes(m.type)?(privateResponses&&!invalidPrivateAck?{privateResponses:true,displayed:true}:{result:{content:"C"},responseSeconds:22}):{provider:"Groq",credentialSaved:true,screenshotEnabled,clipboardEnabled,...(omitPrivatePreference?{}:{privateResponses})}});});}};}},offscreen:{Reason:{CLIPBOARD:"CLIPBOARD"},async createDocument(){contexts=[{}];}},commands:{onCommand:event("command")},tabs:{async query(){return [{id:7,windowId:3}];},async sendMessage(id,m){displays.push(m);if(m.state==="select")return selectionCancelled?{cancelled:true}:{region:{x:0,y:0,width:50,height:50,viewportWidth:100,viewportHeight:100}};},async captureVisibleTab(window,options){assert.equal(window,3);assert.equal(options.format,"png");captures++;return "data:image/png;base64,"+Buffer.from(png).toString("base64");},onRemoved:event("removed"),onUpdated:event("updated")},scripting:{async executeScript(o){assert.deepEqual(o.files,["src/content/overlay.js"]);}},action:{async setBadgeText(){},async setTitle(){}}};
await import("../dist/src/background/service-worker.js");
const sender={id:"own",url:"chrome-extension://own/src/settings/index.html"};
function invoke(action,values){return new Promise(resolve=>listeners.ui({action,values},sender,resolve));}
const settle=()=>new Promise(resolve=>setTimeout(resolve,220));
test("Idle never reads clipboard or captures; only AI command reads without navigation",async()=>{
 assert.equal(reads,0);assert.equal(captures,0);assert.equal(messages.length,0);
 listeners.command("irrelevant");await settle();assert.equal(reads,0);
 listeners.command("ask-clipboard");await settle();assert.equal(reads,1);assert.equal(messages.find(m=>m.type==="TEXT_INPUT").payload.text,clipboardText);assert.equal(displays[0].state,"processing");assert.equal(displays.at(-1).text,"C");assert.equal(displays.at(-1).seconds,22);
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
test("Image shortcut selects once, crops in memory and routes SCREENSHOT_INPUT without clipboard access",async()=>{
 const before=reads;listeners.command("ask-region");await settle();assert.equal(captures,1);assert.equal(reads,before);assert.equal(closed,1);
 const m=messages.findLast(m=>m.type==="SCREENSHOT_INPUT");assert.deepEqual(Object.keys(m.payload),["imageBase64"]);assert.equal(displays.at(-1).text,"C");
});
test("Cancelled selection or disabled image privacy never captures or submits",async()=>{
 selectionCancelled=true;listeners.command("ask-region");await settle();assert.equal(captures,1);
 selectionCancelled=false;screenshotEnabled=false;listeners.command("ask-region");await settle();assert.equal(captures,1);assert.ok(displays.at(-1).text.includes("disabled"));screenshotEnabled=true;
});
test("Hide cancels current request and removes overlay",async()=>{
 clipboardText="question";listeners.command("ask-clipboard");await settle();listeners.command("hide-answer");await settle();assert.equal(displays.at(-1).state,"hide");assert.equal(messages.at(-1).type,"CANCEL");
});
test("Navigation invalidates pending request correlation",async()=>{
 listeners.command("ask-clipboard");await settle();listeners.updated(7,{status:"loading"});assert.equal(messages.at(-1).type,"CANCEL");
});

test("Disabled text privacy prevents even an explicit clipboard read",async()=>{
 const before=reads;clipboardEnabled=false;listeners.command("ask-clipboard");await settle();assert.equal(reads,before);assert.ok(displays.at(-1).text.includes("disabled"));clipboardEnabled=true;
});

test("Private text/image requests never put processing, answer, details or errors into the page",async()=>{
 privateResponses=true;const start=displays.length;
 clipboardText="question";listeners.command("ask-clipboard");await settle();
 listeners.command("ask-region");await settle();
 clipboardText="";listeners.command("ask-clipboard");await settle();
 assert.ok(displays.slice(start).every(m=>["prepare","select"].includes(m.state)));
 assert.ok(messages.findLast(m=>m.type==="SCREENSHOT_INPUT"));
 privateResponses=false;clipboardText="question";
});
test("Private display protocol failure never falls back to a browser answer",async()=>{
 privateResponses=true;invalidPrivateAck=true;const start=displays.length;
 listeners.command("ask-clipboard");await settle();
 assert.ok(displays.slice(start).every(m=>m.state==="prepare"));
 privateResponses=false;invalidPrivateAck=false;
});
test("Private display preference and hide are routed only through authorized settings",async()=>{
 assert.equal((await invoke("privacy",{privateResponses:true})).ok,true);
 assert.equal(messages.at(-1).payload.privateResponses,true);
 listeners.command("ask-clipboard");await settle();listeners.command("hide-answer");await settle();
 assert.equal(messages.at(-2).type,"HIDE_RESPONSE");
 assert.equal(messages.at(-2).payload.targetId,messages.at(-1).payload.targetId);
});

test("Missing helper privacy capability never silently selects a screen-share-visible answer",async()=>{
 omitPrivatePreference=true;const start=displays.length;
 listeners.command("ask-clipboard");await settle();
 assert.ok(displays.slice(start).every(m=>m.state==="prepare"));
 assert.equal(messages.findLast(m=>m.type==="TEXT_INPUT").payload.privateResponses,true);
 omitPrivatePreference=false;
});
