import test from "node:test";
import assert from "node:assert/strict";
import vm from "node:vm";
import { readFile } from "node:fs/promises";

class Element {
 constructor(tag){this.tag=tag;this.children=[];this.listeners={};this.style={setProperty:(k,v)=>this.style[k]=v};this.className="";}
 append(...nodes){for(const n of nodes){n.remove?.();n.parent=this;this.children.push(n);}}
 remove(){if(this.parent)this.parent.children=this.parent.children.filter(n=>n!==this);this.parent=undefined;}
 replaceChildren(...nodes){this.children.forEach(n=>n.parent=undefined);this.children=[];this.append(...nodes);}
 setAttribute(name,value){(this.attributes??={})[name]=value;}
 attachShadow(){this.shadow=new Element("shadow");this.shadow.parent=this;return this.shadow;}
 get isConnected(){return this.tag==="root"||this.parent?.isConnected===true;}
 addEventListener(name,fn){(this.listeners[name]??=[]).push(fn);}
 removeEventListener(name,fn){this.listeners[name]=(this.listeners[name]??[]).filter(f=>f!==fn);}
 setPointerCapture(){}
 get classList(){return {add:name=>{this.className+=" "+name;},remove:name=>{this.className=this.className.split(" ").filter(n=>n!==name).join(" ");},contains:name=>this.className.split(" ").includes(name)};}
 matches(selector){return selector===":hover"&&this.hovered===true;}
 dispatch(name,extra={}){if(name==="mouseenter")this.hovered=true;if(name==="mouseleave")this.hovered=false;for(const fn of this.listeners[name]??[])fn({preventDefault(){},stopPropagation(){},stopImmediatePropagation(){},...extra});}
}
async function fixture(copyOk=true){
 let listener,now=0;const root=new Element("root"),document=new Element("document"),window=new Element("window"),timers=[],messages=[];
 window.top=window;document.documentElement=root;document.createElement=tag=>new Element(tag);document.createElementNS=(_,tag)=>new Element(tag);document.activeElement={id:"question"};
 const chrome={runtime:{id:"own",onMessage:{addListener(fn){listener=fn;}},async sendMessage(m){messages.push(m);return {ok:copyOk};}}};
 vm.runInNewContext(await readFile(new URL("../dist/src/content/overlay.js",import.meta.url),"utf8"),{document,window,chrome,performance:{now:()=>now},innerWidth:1000,innerHeight:800,scrollX:0,scrollY:0,requestAnimationFrame:fn=>fn(),setTimeout:(fn,ms)=>{const t={fn,ms,at:now+ms};timers.push(t);return t;},clearTimeout:t=>{if(t)t.cancelled=true;}});
 function advance(ms){const end=now+ms;for(;;){const next=timers.filter(t=>!t.cancelled&&t.at<=end).sort((a,b)=>a.at-b.at)[0];if(!next)break;now=next.at;next.cancelled=true;next.fn();}now=end;}
 return {root,document,window,timers,messages,advance,send:m=>listener({target:"overlay",...m},{id:"own"},m.reply??(()=>{})),card:()=>root.children[0]?.shadow?.children.find(n=>n.tag==="div")};
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
 f.send({state:"answer",id:"two",text:'```c\n'+code+'\n```'});card=f.card();assert.equal(card.children[0].textContent,"Code");
 const body=card.children[1],box=body.children[0],button=box.children[0].children[1];assert.equal(button.children[0].tag,"svg");button.dispatch("click",{isTrusted:true});await Promise.resolve();assert.equal(f.messages[0].action,"copy-code");assert.equal(f.messages[0].text,code);assert.equal(button.children[1].textContent,"Copied");assert.equal(button.attributes["aria-label"],"Code copied");assert.equal(button.children[0].children[0].attributes.d,"M2 8l4 4L14 3");
 f.timers.at(-1).fn();assert.equal(button.children[1].textContent,"Copy");assert.equal(f.document.activeElement.id,"question");
});

test("Failed code copy shows retry instead of a success icon; untrusted clicks do nothing",async()=>{
 const f=await fixture(false);f.send({state:"processing",id:"copy"});f.send({state:"answer",id:"copy",text:'```js\nconsole.log("test");\n```'});
 const button=f.card().children[1].children[0].children[0].children[1];button.dispatch("click");assert.equal(f.messages.length,0);
 button.dispatch("click",{isTrusted:true});await new Promise(resolve=>setImmediate(resolve));
 assert.equal(button.children[1].textContent,"Retry");assert.equal(button.attributes["aria-label"],"Copy failed. Try again.");assert.notEqual(button.children[0].children[0].attributes.d,"M2 8l4 4L14 3");
});
test("Compact previews are one short line while hover retains the complete answer",async()=>{
 const f=await fixture();const text="Binary search repeatedly halves a sorted search range until it finds the requested value.";
 f.send({state:"processing",id:"one"});f.send({state:"answer",id:"one",text});const card=f.card();
 assert.equal(card.children[0].textContent.length,40);assert.ok(card.children[0].textContent.endsWith("…"));assert.equal(card.children[1].children[0].textContent,text);
 card.dispatch("mouseenter");assert.ok(card.className.includes("expanded"));card.dispatch("mouseleave");assert.ok(!card.className.includes("expanded"));
 f.send({state:"processing",id:"two"});f.send({state:"answer",id:"two",text:"A, C, D"});assert.equal(f.card().children[0].textContent,"A, C, D");
});
test("Every answer pauses on hover and resumes only its remaining duration",async()=>{
 const f=await fixture();f.send({state:"processing",id:"one"});f.send({state:"answer",id:"one",text:"B"});
 const card=f.card();f.advance(5000);card.dispatch("mouseenter");f.advance(60000);assert.ok(f.card());
 card.dispatch("mouseleave");assert.equal(f.timers.at(-1).ms,17000);f.advance(7000);card.dispatch("mouseenter");f.advance(100000);assert.ok(f.card());
 card.dispatch("mouseleave");assert.equal(f.timers.at(-1).ms,10000);f.advance(9999);assert.ok(f.card());f.advance(1);assert.equal(f.root.children.length,0);
});
test("Hover timer handles details, replacement, hide and the processing watchdog independently",async()=>{
 const f=await fixture();f.send({state:"processing",id:"one"});const watchdog=f.timers.at(-1);f.card().dispatch("mouseenter");assert.ok(!watchdog.cancelled);
 f.send({state:"answer",id:"one",text:"C",details:"Explanation",seconds:12});const old=f.card();f.advance(2000);old.dispatch("mouseenter");f.advance(20000);assert.ok(f.card().className.includes("expanded"));
 f.send({state:"processing",id:"two"});f.send({state:"answer",id:"two",text:"A",seconds:12});old.dispatch("mouseenter");f.advance(12000);assert.equal(f.root.children.length,0);
 f.send({state:"processing",id:"three"});f.send({state:"answer",id:"three",text:"Code",seconds:12});const hidden=f.card();hidden.dispatch("mouseenter");f.send({state:"hide"});hidden.dispatch("mouseleave");f.advance(300000);assert.equal(f.root.children.length,0);
});
test("Answer text is subtle and borderless, including expanded text, code and Copy",async()=>{
 const f=await fixture();f.send({state:"processing",id:"one"});f.send({state:"answer",id:"one",text:"B"});
 assert.equal(f.card().style["--answer-opacity"],"0.55");const css=f.root.children[0].shadow.children[0].textContent;
 assert.match(css,/\.card\{[^}]*background:none;[^{]*border:0;/);assert.match(css,/\.card\{[^}]*pointer-events:auto/);
 for(const selector of [".code","button"])assert.ok(css.includes(selector+"{"));assert.ok(!css.includes("border:1px"));assert.ok(!css.includes("border-bottom"));
 assert.equal(f.document.activeElement.id,"question");
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
