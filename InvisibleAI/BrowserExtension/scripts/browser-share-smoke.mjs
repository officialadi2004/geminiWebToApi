// Explicit test command only. Capture remains local and is stopped in finally.
// Frames are inspected solely within the synthetic fixture's small rectangle;
// no screenshots/video, browser history, provider inputs or credentials are saved.
import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createServer } from "node:http";
import { createInterface } from "node:readline";
import { mkdtemp, mkdir, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { chromium } from "playwright";
const [browser,hostPath]=process.argv.slice(2);
assert.ok(["Chrome","Edge"].includes(browser)&&hostPath,"Use Scripts/Test-ScreenShare.ps1");
const qa=resolve("../artifacts/qa");await mkdir(qa,{recursive:true});
const server=createServer((_,res)=>{res.setHeader("Content-Type","text/html");res.end('<!doctype html><title>Synthetic capture test</title><style>html,body{background:#fff;margin:0}</style><button id="start">Start local capture test</button>');});
await new Promise(r=>server.listen(0,"127.0.0.1",r));
let context,fixture,lines,page;
const replies=[];let errors="";
try {
 context=await chromium.launchPersistentContext(await mkdtemp(qa+"/capture-"),{headless:false,viewport:null,executablePath:browser==="Edge"?"C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe":"C:/Program Files/Google/Chrome/Application/chrome.exe",args:["--start-fullscreen","--auto-select-screen-capture-source","--auto-select-desktop-capture-source=Entire screen","--disable-features=MediaRouter"]});
 page=context.pages()[0];await page.goto(`http://127.0.0.1:${server.address().port}`);await page.bringToFront();
 await page.evaluate(()=>{
  document.getElementById("start").onclick=()=>{
   globalThis.captureReady=(async()=>{
    const stream=await navigator.mediaDevices.getDisplayMedia({video:{displaySurface:"monitor",frameRate:10,width:{ideal:4096},height:{ideal:4096}},audio:false});globalThis.captureStream=stream;
    const video=document.createElement("video");video.muted=true;video.srcObject=stream;await video.play();globalThis.captureVideo=video;
    return stream.getVideoTracks()[0].getSettings();
   })();
  };
 });
 await page.locator("#start").click();
 const settings=await Promise.race([page.evaluate(()=>globalThis.captureReady),new Promise((_,reject)=>setTimeout(()=>reject(new Error("Screen picker did not auto-select in this test profile.")),15000))]);
 assert.equal(settings.displaySurface,"monitor","Test did not capture an entire screen");
 await page.mouse.move(10,10);
 fixture=spawn(hostPath,["--screen-share-fixture"],{windowsHide:true,stdio:["pipe","pipe","pipe"]});
 fixture.stderr.on("data",data=>{errors+=data.toString();});
 lines=createInterface({input:fixture.stdout});lines.on("line",line=>replies.push(JSON.parse(line)));
 async function command(value){
  const index=replies.length;fixture.stdin.write(value+"\n");const deadline=Date.now()+10000;
  while(replies.length===index){assert.ok(Date.now()<deadline,"Synthetic host did not respond");await page.waitForTimeout(30);}
  const result=replies[index];assert.equal(result.error,undefined);return result;
 }
 const protectedWindow=await command("protected");assert.equal(protectedWindow.visible,true);assert.equal(protectedWindow.affinity,17);
 assert.equal(protectedWindow.screens,1,"Automatic crop mapping requires one monitor; use recipient-view acceptance on multi-monitor setups");
 async function pixels(){
  // Only copy the opaque white fixture area around the answer from the live video.
  return page.evaluate(({crop,monitor})=>{
   const video=globalThis.captureVideo,sx=video.videoWidth/monitor.width,sy=video.videoHeight/monitor.height;
   const canvas=document.createElement("canvas");canvas.width=Math.ceil(crop.width*sx);canvas.height=Math.ceil(crop.height*sy);
   const ctx=canvas.getContext("2d");ctx.drawImage(video,(crop.x-monitor.x)*sx,(crop.y-monitor.y)*sy,canvas.width,canvas.height,0,0,canvas.width,canvas.height);
   const data=ctx.getImageData(0,0,canvas.width,canvas.height).data;let dark=0;
   for(let i=0;i<data.length;i+=4)if(data[i]<230||data[i+1]<230||data[i+2]<230)dark++;
   const info={dark,videoWidth:video.videoWidth,videoHeight:video.videoHeight,first:[...data.slice(0,3)],middle:[...data.slice(Math.floor(data.length/8)*4,Math.floor(data.length/8)*4+3)]};
   data.fill(0);canvas.width=canvas.height=0;return info;
  },protectedWindow);
 }
 await page.waitForTimeout(1200);const excludedInfo=await pixels(),excluded=excludedInfo.dark;
 const control=await command("unprotected");assert.equal(control.affinity,0);
 await page.waitForTimeout(1200);const includedInfo=await pixels(),included=includedInfo.dark;
 const restored=await command("protected");assert.equal(restored.affinity,17);
 await page.waitForTimeout(1200);const excludedAgain=(await pixels()).dark;
 await command("hide");await page.waitForTimeout(1200);const baselineInfo=await pixels(),baseline=baselineInfo.dark;
 console.log(JSON.stringify({browser,crop:protectedWindow.crop,background:protectedWindow.background,monitor:protectedWindow.monitor,captureWidth:settings.width,captureHeight:settings.height,includedInfo,excludedInfo,baselineInfo,excludedAgain}));
 assert.ok(baseline<=5,"Owned white capture fixture did not map to the selected screen");
 assert.ok(included>baseline+15,`Positive control missing: included=${included}, baseline=${baseline}`);
 assert.ok(excluded<=baseline+5&&excludedAgain<=baseline+5,`Excluded answer appeared: excluded=${excluded}, repeat=${excludedAgain}, baseline=${baseline}`);
 assert.equal(errors,"");
 const result={browser,capture:"getDisplayMedia monitor",included,excluded,excludedAgain,baseline,recipientMeet:"NOT TESTED",physicalF11:"NOT TESTED"};
 await writeFile(resolve(qa,`capture-${browser.toLowerCase()}.json`),JSON.stringify(result,null,2));
 console.log(`PASS ${browser} entire-screen getDisplayMedia: local native window omitted; unexcluded positive control present; no video/image files saved`);
 console.log("LIMITATION Google Meet recipient view and physical F11 were not tested.");
}finally{
 if(page)try{await page.evaluate(()=>{globalThis.captureStream?.getTracks().forEach(track=>track.stop());if(globalThis.captureVideo)globalThis.captureVideo.srcObject=null;});}catch{/* Browser closed. */}
 if(fixture){fixture.stdin.end("quit\n");await Promise.race([new Promise(r=>fixture.once("exit",r)),new Promise(r=>setTimeout(r,2000))]);if(fixture.exitCode===null)fixture.kill();}
 lines?.close();if(context)await context.close();await new Promise(r=>server.close(r));
}
