import {chromium, expect} from '@playwright/test';
import {createServer} from 'node:http';
import {readFile, writeFile} from 'node:fs/promises';
import {resolve, join, dirname, basename, extname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
const out=resolve(dirname(fileURLToPath(import.meta.url)),'../artifacts/demo');
const meta=JSON.parse(await readFile(join(out,'bolumler.json'),'utf8'));
const errors=[];
const server=createServer(async(req,res)=>{
  const filename=basename(new URL(req.url,'http://localhost').pathname);
  try {
    const data=await readFile(join(out,filename));
    const types={'.html':'text/html; charset=utf-8','.mp4':'video/mp4','.png':'image/png'};
    const headers={'Content-Type':types[extname(filename)]||'text/plain; charset=utf-8','Accept-Ranges':'bytes'};
    const range=/^bytes=(\d+)-(\d*)$/.exec(req.headers.range||'');
    if(range) {
      const start=Number(range[1]),end=Math.min(range[2]?Number(range[2]):data.length-1,data.length-1);
      res.writeHead(206,{...headers,'Content-Range':'bytes '+start+'-'+end+'/'+data.length,'Content-Length':end-start+1});
      res.end(data.subarray(start,end+1));
    } else {res.writeHead(200,{...headers,'Content-Length':data.length});res.end(data);}
  } catch {res.writeHead(404);res.end();}
});
await new Promise(resolve=>server.listen(5086,'127.0.0.1',resolve));
let browser;
try {
  browser=await chromium.launch({executablePath:process.env.CHROMIUM_PATH||'/usr/bin/chromium'});
  const page=await browser.newPage({viewport:{width:1440,height:1000}});
  page.on('pageerror',e=>errors.push(e.message));
  await page.goto('http://127.0.0.1:5086/demoyu-izle.html');
  await expect(page.locator('.chapter')).toHaveCount(15);
  await expect.poll(()=>page.locator('#demo').evaluate(v=>v.readyState)).toBeGreaterThanOrEqual(1);
  await page.locator('.chapter').nth(8).click();
  await expect.poll(()=>page.locator('#demo').evaluate(v=>v.currentTime)).toBeGreaterThanOrEqual(meta.chapters[8].start);
  await expect.poll(()=>page.locator('#demo').evaluate(v=>v.paused)).toBeFalsy();
  await page.locator('#demo').evaluate(v=>v.pause());
  await expect(page.locator('.chapter.current')).toContainText('09 / Videoya soru sorun');
  await page.screenshot({path:join(out,'demo-izleyici.png'),fullPage:true});
  await page.locator('video.mobile').evaluate(async v=>{await v.play();});
  await expect.poll(()=>page.locator('video.mobile').evaluate(v=>v.currentTime)).toBeGreaterThan(0);
  await page.locator('video.mobile').evaluate(v=>v.pause());
  await page.setViewportSize({width:390,height:844});
  expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  const input=(await readFile(join(out,'ornek-toplanti.srt'),'utf8')).replaceAll('\r','').trim();
  const downloaded=(await readFile(join(out,'uygulamadan-indirilen.srt'),'utf8')).replaceAll('\r','').trim();
  expect(downloaded).toBe(input);
  for(const name of ['videoatlas-kullanim-demo.mp4','videoatlas-mobil-demo.mp4'])
    execFileSync('ffmpeg',['-v','error','-i',join(out,name),'-f','null','-'],{stdio:'pipe'});
  expect(errors).toEqual([]);
  const result={viewerChapters:15,desktopPlayback:true,mobilePlayback:true,chapterSeeking:true,mobileOverflow:false,downloadedSrtMatchesInput:true,fullVideoDecode:true,javascriptErrors:errors};
  await writeFile(join(out,'dogrulama.json'),JSON.stringify(result,null,2));
  console.log(JSON.stringify(result));
} finally {
  if(browser)await browser.close();
  await new Promise(resolve=>server.close(resolve));
}
