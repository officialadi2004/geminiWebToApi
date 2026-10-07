import test from "node:test";
import assert from "node:assert/strict";
import vm from "node:vm";
import { readFile } from "node:fs/promises";

class Element {
 constructor(tag){this.tag=tag;this.children=[];this.listeners={};this.style={setProperty:(k,v)=>this.style[k]=v};this.className="";}
 append(...nodes){for(const n of nodes){n.remove?.();n.parent=this;this.children.push(n);}}
 remove(){if(this.parent)this.parent.children=this.parent.children.filter(n=>n!==this);this.parent=undefined;}
 replaceChildren(...nodes){this.children.forEach(n=>n.parent=undefined);this.children=[];this.append(...nodes);}
 setAttribute(){}
 attachShadow(){this.shadow=new Element("shadow");this.shadow.parent=this;return this.shadow;}
 get isConnected(){return this.tag==="root"||this.parent?.isConnected===true;}
 addEventListener(name,fn){(this.listeners[name]??=[]).push(fn);}
 removeEventListener(name,fn){this.listeners[name]=(this.listeners[name]??[]).filter(f=>f!==fn);}
 setPointerCapture(){}
 get classList(){return {add:name=>{this.className+=" "+name;},remove:name=>{this.className=this.className.split(" ").filter(n=>n!==name).join(" ");}};}
 dispatch(name,extra={}){for(const fn of this.listeners[name]??[])fn({preventDefault(){},stopPropagation(){},stopImmediatePropagation(){},...extra});}
}
async function fixture(){
 let listener;const root=new Element("root"),document=new Element("document"),window=new Element("window"),timers=[],messages=[];
 window.top=window;document.documentElement=root;document.createElement=tag=>new Element(tag);document.activeElement={id:"question"};
 const chrome={runtime:{id:"own",onMessage:{addListener(fn){listener=fn;}},async sendMessage(m){messages.push(m);return {ok:true};}}};
 vm.runInNewContext(await readFile(new URL("../dist/src/content/overlay.js",import.meta.url),"utf8"),{document,window,chrome,innerWidth:1000,innerHeight:800,scrollX:0,scrollY:0,requestAnimationFrame:fn=>fn(),setTimeout:(fn,ms)=>{const t={fn,ms};timers.push(t);return t;},clearTimeout:t=>{if(t)t.cancelled=true;}});
 return {root,document,window,timers,messages,send:m=>listener({target:"overlay",...m},{id:"own"},m.reply??(()=>{})),card:()=>root.children[0]?.shadow?.children.find(n=>n.tag==="div")};
}
test("Overlay is absent at idle, processing is tiny, default expiry is 22 seconds and hide removes it",async()=>{
 const f=await fixture();assert.equal(f.root.children.length,0);
 f.send({state:"processing",id:"one"});assert.equal(f.card().className,"processing");assert.equal(f.document.activeElement.id,"question");
 f.send({state:"answer",id:"old",text:"wrong"});assert.equal(f.card().className,"processing");
 f.send({state:"answer",id:"one",text:"A, C, D"});assert.equal(f.card().children[0].textContent,"A, C, D");assert.equal(f.timers.at(-1).ms,22000);
 f.timers.at(-1).fn();assert.equal(f.root.children.length,0);
});
test("Details expand on hover only, collapse on leave, and code copy sends exact code",async()=>{
 const f=await fixture();f.send({state:"processing",id:"one"});
 f.send({state:"answer",id:"one",text:"C",details:"Because Paris.",opacity:.94});let card=f.card();assert.equal(card.style["--answer-opacity"],"0.94");assert.ok(card.className.includes("expandable"));assert.ok(!card.className.includes("expanded"));card.dispatch("mouseenter");assert.ok(card.className.includes("expanded"));card.dispatch("mouseleave");assert.ok(!card.className.includes("expanded"));
 f.send({state:"processing",id:"two"});const code='if (x) {\n\tprintf("hello");\n}';
 f.send({state:"answer",id:"two",text:'```c\n'+code+'\n```'});card=f.card();assert.equal(card.children[0].textContent,"Code · hover to view");
 const body=card.children[1],box=body.children[0],button=box.children[0].children[1];button.dispatch("click",{isTrusted:true});await Promise.resolve();assert.equal(f.messages[0].action,"copy-code");assert.equal(f.messages[0].text,code);assert.equal(button.textContent,"Copied ✓");
 f.timers.at(-1).fn();assert.equal(button.textContent,"Copy");assert.equal(f.document.activeElement.id,"question");
});
test("Region selection removes UI before reply, clamps coordinates, and Escape/right-click cancel",async()=>{
 const f=await fixture();let reply;
 f.send({state:"select",id:"image",reply:r=>reply=r});assert.equal(f.root.children[0].id,"invisible-ai-selection");
 let layer=f.root.children[0];layer.dispatch("pointerdown",{button:0,pointerId:1,clientX:400,clientY:300});layer.dispatch("pointermove",{clientX:200,clientY:100});layer.dispatch("pointerup",{clientX:200,clientY:100});
 assert.equal(f.root.children.length,0);assert.equal(reply.region.x,200);assert.equal(reply.region.y,100);assert.equal(reply.region.width,200);assert.equal(reply.region.height,200);
 for(const mode of ["Escape","right"]){reply=undefined;f.send({state:"select",id:mode,reply:r=>reply=r});layer=f.root.children[0];if(mode==="Escape")f.document.dispatch("keydown",{key:"Escape"});else {layer.dispatch("pointerdown",{button:2});layer.dispatch("pointerup",{button:2});assert.equal(f.root.children.length,1);layer.dispatch("contextmenu");}assert.equal(reply.cancelled,true);assert.equal(f.root.children.length,0);}
});
test("DOM fullscreen moves an existing overlay without exiting fullscreen or stealing focus",async()=>{
 const f=await fixture();f.send({state:"processing",id:"one"});const host=f.root.children[0],fullscreen=new Element("fullscreen");f.document.fullscreenElement=fullscreen;f.document.dispatch("fullscreenchange");assert.equal(host.parent,fullscreen);assert.equal(f.document.fullscreenElement,fullscreen);assert.equal(f.document.activeElement.id,"question");
});
