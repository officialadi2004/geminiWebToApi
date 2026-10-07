import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import vm from "node:vm";
import { validEnvelope, wire, HOST } from "../dist/src/protocol.js";
test("Versioned native protocol rejects invalid envelopes", () => {
 assert.equal(HOST,"com.invisibleai.assistant"); assert.ok(validEnvelope(wire("PING"))); assert.ok(validEnvelope({version:1,type:"PROCESSING_START",id:"request",payload:null}));
 for(const value of [{version:2,type:"PING",id:"x"},{version:1,type:"PING",id:""},{version:1,type:"PING",id:"x",payload:[]}]) assert.equal(validEnvelope(value),false);
});
test("MV3 has explicit clipboard permission and stable helper origin, no background screen or clipboard monitoring", async () => {
 const m=JSON.parse(await readFile(new URL("../dist/manifest.json",import.meta.url)));
 assert.equal(m.manifest_version,3); assert.deepEqual(m.content_scripts[0].matches,["http://*/*","https://*/*"]); assert.equal(m.host_permissions,undefined); assert.equal(m.externally_connectable,undefined);
 assert.deepEqual(m.permissions.sort(),["activeTab","scripting","nativeMessaging","clipboardRead","clipboardWrite","offscreen"].sort());
 assert.equal(m.commands["ask-clipboard"].suggested_key.default,"Ctrl+Shift+V");
 assert.equal(m.commands["ask-region"].suggested_key.default,"Ctrl+Shift+S");
 const id=createHash("sha256").update(Buffer.from(m.key,"base64")).digest("hex").slice(0,32).replace(/[0-9a-f]/g,x=>String.fromCharCode(97+parseInt(x,16)));
 assert.ok((await readFile(new URL("../../Shared/Protocol/ExtensionIdentity.cs",import.meta.url),"utf8")).includes(id));
 for(const file of [m.background.service_worker,m.options_page,m.action.default_popup,"src/offscreen/index.html"]) await readFile(new URL(`../dist/${file}`,import.meta.url));
});
test("Code copy preserves quotes, tabs, indentation and newlines, without echoing code",async()=>{
 let listener, copied, field={value:"",focus(){},select(){},blur(){}};
 vm.runInNewContext(await readFile(new URL("../dist/src/offscreen/clipboard.js",import.meta.url),"utf8"),{chrome:{runtime:{id:"own",onMessage:{addListener(fn){listener=fn;}}}},document:{getElementById(){return field;},execCommand(command){assert.equal(command,"copy");copied=field.value;return true;}}});
 const code='if (x) {\n\tprintf("quote", x);\n}\n';let response;
 listener({target:"clipboard",action:"write",text:code},{id:"own"},r=>response=r);
 assert.equal(copied,code);assert.equal(field.value,"");assert.equal(response.ok,true);assert.deepEqual(Object.keys(response),["ok"]);
});
test("Offscreen clipboard reader runs only on explicit request and clears its field", async () => {
 let listener, reads=0, field={value:"",focus(){},select(){},blur(){}};
 vm.runInNewContext(await readFile(new URL("../dist/src/offscreen/clipboard.js",import.meta.url),"utf8"),{chrome:{runtime:{id:"own",onMessage:{addListener(fn){listener=fn;}}}},document:{getElementById(){return field;},execCommand(command){assert.equal(command,"paste");reads++;field.value="copied question";return true;}}});
 assert.equal(reads,0); let response;
 listener({target:"clipboard",action:"read"},{id:"other"},()=>assert.fail("untrusted sender")); assert.equal(reads,0);
 listener({target:"clipboard",action:"read"},{id:"own"},r=>response=r); assert.equal(response.text,"copied question"); assert.equal(field.value,""); assert.equal(reads,1);
});
