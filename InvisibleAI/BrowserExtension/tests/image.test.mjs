import test from "node:test";
import assert from "node:assert/strict";
import { cropBounds } from "../dist/src/background/image.js";
test("Capture scales CSS selections to actual pixels at mixed DPI and viewport sizes",()=>{
 const region={x:10,y:20,width:100,height:60,viewportWidth:1000,viewportHeight:800};
 assert.deepEqual(cropBounds(region,2000,1600),{x:20,y:40,width:200,height:120});
 assert.deepEqual(cropBounds(region,1250,1000),{x:12,y:25,width:126,height:75});
 assert.deepEqual(cropBounds({...region,x:900,y:740},1000,800),{x:900,y:740,width:100,height:60});
});
test("Capture rejects tiny, out-of-bounds, nonfinite and oversized images",()=>{
 const valid={x:0,y:0,width:50,height:50,viewportWidth:100,viewportHeight:100};
 for(const change of [{width:0},{height:3},{x:-1},{x:99},{viewportWidth:0},{y:NaN}])assert.throws(()=>cropBounds({...valid,...change},100,100));
 assert.throws(()=>cropBounds(valid,10000,10000));
});
