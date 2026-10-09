// Records the unmodified app through real browser interactions and real HTTP.
// Only tutorial captions are composited later, outside the application's frame.
import {chromium, expect} from '@playwright/test';
import {spawn, execFileSync} from 'node:child_process';
import {mkdtemp, mkdir, readFile, writeFile, copyFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {resolve, join, dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomBytes} from 'node:crypto';
import {createServer} from 'node:http';
import {performance} from 'node:perf_hooks';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const out = join(root, 'artifacts', 'demo');
const temp = await mkdtemp(join(tmpdir(), 'videoatlas-demo-'));
const origin = 'http://127.0.0.1:5084';
const dotnet = process.env.DOTNET_PATH || '/workspace/.dotnet/dotnet';
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
await mkdir(join(out, 'raw'), {recursive: true});
let logs = '', browser, context, mobileContext, app, host, embedUrl;
const chapters = [], mobileChapters = [], failures = [];
let started = 0, mobileStarted = 0;
const stamp = seconds => {
  const ms = Math.max(0, Math.round(seconds * 1000));
  return String(Math.floor(ms / 3600000)).padStart(2,'0') + ':' + String(Math.floor(ms / 60000) % 60).padStart(2,'0') + ':' + String(Math.floor(ms / 1000) % 60).padStart(2,'0') + ',' + String(ms % 1000).padStart(3,'0');
};
async function run(file, args, options = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(file, args, {cwd: root, ...options});
    let errors = '';
    child.stderr.on('data', b => { errors += b; });
    child.on('error', reject);
    child.on('exit', code => code === 0 ? resolve() : reject(new Error(file + ': ' + errors.slice(-5000))));
  });
}
async function click(page, locator) {
  await locator.scrollIntoViewIfNeeded();
  const rect = await locator.boundingBox();
  if (rect) await page.mouse.move(rect.x + rect.width / 2, rect.y + rect.height / 2, {steps: 12});
  await pause(350);
  await locator.click();
}
async function chapter(title, caption) {
  chapters.push({start: Math.max(0, (performance.now() - started) / 1000), title, caption});
  console.log('DEMO: ' + title);
}
function srt(entries, duration) {
  return entries.map((item, i) => (i + 1) + '\n' + stamp(i === 0 ? 0 : item.start) + ' --> ' + stamp(entries[i + 1]?.start ?? duration) + '\n' + item.title + '\n' + item.caption + '\n').join('\n');
}
async function encode(raw, name, entries, width, height, title) {
  const duration = Number(execFileSync('ffprobe', ['-v','error','-show_entries','format=duration','-of','csv=p=0',raw], {encoding:'utf8'}).trim());
  const captions = join(out, name + '.srt');
  await writeFile(captions, srt(entries, duration));
  // All file paths are task-owned. ffmpeg receives an argument array, never shell input.
  const ass = join(out, name + '.ass');
  const assTime = seconds => stamp(seconds).replace(',', '.').slice(0,-1);
  const assHeader = '[Script Info]\nScriptType: v4.00+\nPlayResX: '+width+'\nPlayResY: '+(height+144)+'\nWrapStyle: 0\n\n[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\nStyle: Default,DejaVu Sans,'+(width<500?15:21)+',&H00F5F3FF,&H00F5F3FF,&H000C0809,&H000C0809,0,0,0,0,100,100,0,0,1,0,0,2,16,16,14,1\n\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n';
  await writeFile(ass,assHeader+entries.map((item,i)=>'Dialogue: 0,'+assTime(i===0?0:item.start)+','+assTime(entries[i+1]?.start??duration)+',Default,,0,0,0,,'+item.title+'\\N'+item.caption).join('\n'));
  const filter = "pad=" + width + ':' + (height + 144) + ":0:64:color=0x08090e,drawtext=fontfile=/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf:text='" + title + "':x=24:y=20:fontsize=22:fontcolor=0xb9bcff,ass='" + ass + "'";
  await run('ffmpeg', ['-y','-v','error','-i',raw,'-vf',filter,'-r','25','-c:v','libx264','-threads','2','-preset','fast','-crf','20','-pix_fmt','yuv420p','-movflags','+faststart','-an',join(out,name+'.mp4')]);
  return duration;
}

try {
  console.log('Isolated demo data: ' + temp);
  app = spawn(dotnet, [join(root,'src/VideoAtlas.Web/bin/Debug/net10.0/VideoAtlas.Web.dll')], {
    cwd: join(root,'src/VideoAtlas.Web'),
    env: {...process.env, ASPNETCORE_ENVIRONMENT:'Development', ASPNETCORE_URLS:origin, Storage__Root:temp, Ai__ApiKey:'', ProviderCredentials__OpenRouter:'', ProviderCredentials__ElevenLabs:'', Accounts__AllowRegistration:'true'},
    stdio:['ignore','pipe','pipe']
  });
  app.stdout.on('data', b => { logs += b; }); app.stderr.on('data', b => { logs += b; });
  app.on('error', error => { logs += error.message; });
  for (let i = 0; i < 120; i++) {
    if (await fetch(origin + '/health/ready').then(r => r.ok).catch(() => false)) break;
    if (i === 119 || app.exitCode !== null) throw new Error('Demo server did not start: ' + logs.slice(-2500));
    await pause(250);
  }
  browser = await chromium.launch({executablePath:process.env.CHROMIUM_PATH || '/usr/bin/chromium', headless:true});
  const setup = await browser.newContext({viewport:{width:1280,height:720}});
  const seed = await setup.newPage();
  await seed.setContent(await readFile(join(root,'scripts/demo/meeting.html'),'utf8'));
  await seed.screenshot({path:join(out,'ornek-toplanti.png')});
  await run('ffmpeg',['-y','-v','error','-loop','1','-framerate','10','-i',join(out,'ornek-toplanti.png'),'-t','150','-c:v','libx264','-threads','2','-preset','fast','-crf','25','-pix_fmt','yuv420p','-movflags','+faststart',join(out,'ornek-toplanti.mp4')]);
  await copyFile(join(root,'scripts/demo/meeting.srt'),join(out,'ornek-toplanti.srt'));
  const email = 'demo@videoatlas.example', password = 'Demo!' + randomBytes(18).toString('hex');
  await seed.goto(origin + '/account/register');
  await seed.getByLabel('E-posta adresi').fill(email);
  await seed.getByLabel('Parola',{exact:true}).fill(password);
  await seed.getByRole('button',{name:'Hesap oluştur'}).click();
  await expect(seed.locator('#library-empty')).toBeVisible();
  await setup.close();

  context = await browser.newContext({viewport:{width:1440,height:1000},locale:'tr-TR',recordVideo:{dir:join(out,'raw'),size:{width:1440,height:1000}}});
  started = performance.now();
  const page = await context.newPage();
  page.on('pageerror', e => failures.push(e.message));
  const recording = page.video();
  await chapter('01 / Giriş', 'Gerçek VideoAtlas arayüzü. Bu kayıt, örnek video ve hazırlanmış SRT ile gösterilir.');
  await page.goto(origin + '/account/login'); await pause(2500);
  await page.getByLabel('E-posta adresi').pressSequentially(email,{delay:45});
  await page.getByLabel('Parola',{exact:true}).fill(password);
  await click(page,page.getByRole('button',{name:'Giriş yap'}));
  await expect(page.locator('#library-empty')).toBeVisible();
  await chapter('02 / Video kütüphanesi', 'Videoları tek yerde yönetin. AI anahtarı olmadığı için altyazı ile çalışma modu açık.');
  await pause(5500);
  await chapter('03 / Video yükleme', 'Video yükle düğmesini açın; dosyayı ve başlığı seçip işleme onayını verin.');
  await click(page,page.getByRole('button',{name:'＋ Video yükle',exact:true}));
  await pause(1800);
  await page.locator('#video-file').setInputFiles(join(out,'ornek-toplanti.mp4'));
  await page.getByLabel('Video başlığı').fill('Ürün stratejisi · Ekim lansmanı');
  await page.locator('input[name="consent"]').check(); await pause(2500);
  await click(page,page.getByRole('button',{name:'Yükle ve işlemeye başla'}));
  await expect(page).toHaveURL(/\/videos\//);
  const playerUrl = page.url();
  await chapter('04 / İşleme durumu', 'Video sunucuda hazırlanır. Bu demoda otomatik konuşma tanıma kullanılmıyor.');
  await expect(page.locator('#video-status')).toHaveText('SRT bekliyor',{timeout:60000});
  await pause(3000);
  await chapter('05 / SRT içe aktarma', 'Hazırlanmış örnek altyazıyı içe aktarın. Zaman kodları ve konu grupları oluşur.');
  await page.locator('#srt-file').setInputFiles(join(out,'ornek-toplanti.srt'));
  await expect(page.locator('#video-status')).toHaveText('Hazır');
  await expect(page.locator('.transcript-row')).toHaveCount(10);
  await expect(page.locator('#toast')).toBeHidden({timeout:10000});
  await page.locator('#video').evaluate(async v => { await v.play(); v.pause(); });
  await pause(1500);
  await page.screenshot({path:join(out,'oynatici.png')});

  await chapter('06 / Metinden videoya', '00:45 satırına tıklayın. Oynatıcı bütçe konuşmasının başladığı ana atlar.');
  await click(page,page.getByRole('button',{name:/00:45 Bütçe/}));
  await expect.poll(()=>page.locator('#video').evaluate(v=>v.currentTime)).toBeGreaterThanOrEqual(45);
  await pause(2500); await page.locator('#video').evaluate(v=>v.pause());
  await page.evaluate(()=>scrollTo(0,0)); await pause(3000);
  await chapter('07 / Konuşmalarda arama', 'Metin dökümünde bütçe araması yaparak ilgili satırları filtreleyin.');
  await page.getByLabel('Konuşmalarda ara').pressSequentially('bütçe',{delay:180});
  await expect(page.locator('.transcript-row')).toHaveCount(2);
  await pause(4000); await page.getByLabel('Konuşmalarda ara').fill('');

  await chapter('08 / Konu grupları', 'Konu başlıklarını ve zaman aralıklarını inceleyin. Bu mod anahtar kelime gruplaması kullanır.');
  await click(page,page.getByRole('tab',{name:'Konu başlıkları'}));
  await expect.poll(()=>page.locator('.chapter-card').count()).toBeGreaterThanOrEqual(2);
  await page.locator('#category-filter').selectOption({label:'Anahtar kelimeler'});
  await pause(2500); await click(page,page.locator('.chapter-card').nth(1));
  await pause(2500); await page.locator('#video').evaluate(v=>v.pause());
  await click(page,page.getByRole('tab',{name:'Metin dökümü'}));

  await chapter('09 / Videoya soru sorun', '“Bütçe ne kadar?” sorusuna metinden kaynaklı alıntılar gelir. Bu cevap AI üretimi değildir.');
  await page.getByLabel('Videoyla ilgili sorunuz').pressSequentially('Bütçe ne kadar?',{delay:100});
  await click(page,page.getByRole('button',{name:'Soruyu gönder'}));
  await expect(page.locator('.chat-message.assistant')).toContainText('yüz bin lira');
  await pause(5500);
  await chapter('10 / Cevabın kaynağı', 'Cevabın altındaki zaman koduna tıklayarak bilginin geçtiği bölümü doğrulayın.');
  await click(page,page.locator('.citation').first());
  await expect.poll(()=>page.locator('#video').evaluate(v=>v.currentTime)).toBeGreaterThanOrEqual(45);
  await pause(2500); await page.locator('#video').evaluate(v=>v.pause());
  await page.evaluate(()=>scrollTo(0,0)); await pause(3000);
  await chapter('11 / SRT dışa aktarma', 'SRT indir ile zaman kodlu altyazıyı indirin; başka araçlarda da kullanabilirsiniz.');
  const downloaded = page.waitForEvent('download');
  await click(page,page.getByRole('link',{name:'SRT indir'}));
  const file = await downloaded; await file.saveAs(join(out,'uygulamadan-indirilen.srt'));
  await pause(4500);

  await chapter('12 / Paylaşım ve iframe', 'Paylaş ve göm penceresinde süreyi ve izinli siteleri seçip iframe kodu oluşturun.');
  await click(page,page.getByRole('button',{name:'Paylaş ve göm'}));
  await page.locator('#share-days').selectOption('7'); await pause(1800);
  await click(page,page.getByRole('button',{name:'Bağlantı ve iframe oluştur'}));
  await expect(page.locator('#share-result')).toBeVisible();
  embedUrl = await page.locator('#share-url').inputValue();
  expect(await page.locator('#iframe-code').inputValue()).toContain('<iframe');
  await page.locator('#iframe-code').scrollIntoViewIfNeeded();
  await pause(5500);
  await page.screenshot({path:join(out,'paylasim.png')});

  // A second actual origin serves only a demo host page, never mocked application APIs.
  host = createServer((request,response)=>{
    response.writeHead(200,{'Content-Type':'text/html; charset=utf-8','Cache-Control':'no-store'});
    response.end('<!doctype html><html lang="tr"><meta charset="utf-8"><title>Örnek eğitim sitesi</title><style>body{margin:0;background:#eaeef6;font:14px Arial;color:#20233b}header{padding:18px 28px;background:white;display:flex;justify-content:space-between}main{margin:20px}h1{font-size:22px;margin:0 0 8px}p{margin:0 0 15px;color:#535a70}iframe{width:100%;height:820px;border:1px solid #bcc4d6;border-radius:22px}</style><header><strong>ÖRNEK EĞİTİM SİTESİ</strong><span>Gerçek iframe · farklı yerel site</span></header><main><h1>Ürün stratejisi / Video eğitim alanı</h1><p>VideoAtlas oynatıcı, metin ve sohbet bileşeni aşağıda gömülüdür.</p><iframe src="'+embedUrl+'" title="VideoAtlas video ve sohbet" allow="fullscreen"></iframe></main></html>');
  });
  await new Promise((resolve,reject)=>{host.once('error',reject);host.listen(5085,'127.0.0.1',resolve);});
  await chapter('13 / Başka sitede kullanım', 'Ayrı bir yerel sitedeki gerçek iframe: ziyaretçi videoyu, metni ve sohbeti birlikte kullanabilir.');
  await page.goto('http://localhost:5085/');
  const frame = page.frameLocator('iframe');
  await expect(frame.locator('.transcript-row')).toHaveCount(10);
  await frame.getByLabel('Videoyla ilgili sorunuz').pressSequentially('Lansman ne zaman?',{delay:65});
  await frame.getByRole('button',{name:'Soruyu gönder'}).click();
  await expect(frame.locator('.chat-message.assistant')).toContainText('20 Ekim');
  await pause(6000); await page.screenshot({path:join(out,'iframe.png')});

  // Anonymous mobile view: the share does not depend on an authenticated session.
  mobileContext = await browser.newContext({viewport:{width:390,height:844},locale:'tr-TR',isMobile:true,hasTouch:true,recordVideo:{dir:join(out,'raw'),size:{width:390,height:844}}});
  mobileStarted = performance.now();
  const mobile = await mobileContext.newPage(), mobileRecording = mobile.video();
  mobile.on('pageerror', e=>failures.push(e.message));
  mobileChapters.push({start:0,title:'Mobil / Video ve metin',caption:'Gerçek paylaşılan UI · oturum gerekmez.'});
  await mobile.goto(embedUrl); await expect(mobile.locator('.transcript-row')).toHaveCount(10);
  await pause(3500);
  await mobile.getByLabel('Konuşmalarda ara').fill('bütçe');
  await mobile.locator('.transcript-row').first().click(); await pause(2000);
  await mobile.locator('#video').evaluate(v=>v.pause());
  mobileChapters.push({start:(performance.now()-mobileStarted)/1000,title:'Mobil / Kaynaklı soru-cevap',caption:'Alıntı arama modu · AI üretimi değil.'});
  await mobile.locator('.chat-panel').scrollIntoViewIfNeeded(); await pause(2500);
  await mobile.getByLabel('Videoyla ilgili sorunuz').fill('Bütçe ne kadar?');
  await mobile.getByRole('button',{name:'Soruyu gönder'}).click();
  await expect(mobile.locator('.chat-message.assistant')).toContainText('yüz bin lira');
  await pause(4500);
  expect(await mobile.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
  await mobile.screenshot({path:join(out,'mobil.png'),fullPage:true});
  await mobileContext.close(); mobileContext=null;
  const mobileRaw = await mobileRecording.path();

  await chapter('14 / Paylaşımı kapatma', 'Paylaşımı kapattığınızda bağlantı geçersiz olur. Demo bağlantısı da kayıt sonunda iptal edilir.');
  await page.goto(playerUrl); await expect(page.locator('#share-open')).toBeEnabled();
  await click(page,page.getByRole('button',{name:'Paylaş ve göm'}));
  await click(page,page.getByRole('button',{name:'Mevcut paylaşımı kapat'}));
  await expect(page.locator('#toast')).toContainText('Paylaşım bağlantısı kapatıldı');
  expect((await fetch(embedUrl)).status).toBe(404); await pause(4000);
  await page.getByRole('button',{name:'Kapat',exact:true}).click();
  await chapter('15 / Kütüphaneye dönüş', 'Video, döküm ve konu grupları hesabınızda kalır. Canlı AI için sunucuda API anahtarı gerekir.');
  await page.goto(origin); await expect(page.locator('.video-card')).toHaveCount(1);
  await pause(5000); await page.screenshot({path:join(out,'kutuphane.png'),fullPage:true});
  expect(failures).toEqual([]);
  await context.close(); context=null;
  const raw = await recording.path();
  console.log('Recording complete. Encoding MP4 files.');
  // Remove idle desktop time while the separate mobile video was being recorded.
  const mobileBegin = (mobileStarted - started) / 1000;
  const mobileEnd = chapters.find(c=>c.title.startsWith('14')).start;
  const cutRaw = join(out,'raw','desktop-trimmed.mkv');
  await run('ffmpeg',['-y','-v','error','-i',raw,'-filter_complex',
    '[0:v]split[a][b];[a]trim=end='+mobileBegin+',setpts=PTS-STARTPTS[a1];[b]trim=start='+mobileEnd+',setpts=PTS-STARTPTS[b1];[a1][b1]concat=n=2:v=1:a=0[v]',
    '-map','[v]','-c:v','libx264','-threads','2','-preset','fast','-crf','18',cutRaw]);
  for(const item of chapters) if(item.start>=mobileEnd) item.start-=mobileEnd-mobileBegin;
  const duration = await encode(cutRaw,'videoatlas-kullanim-demo',chapters,1440,1000,'VideoAtlas / Gerçek UI kullanım demosu');
  const mobileDuration = await encode(mobileRaw,'videoatlas-mobil-demo',mobileChapters,390,844,'VideoAtlas / Mobil demo');
  await writeFile(join(out,'bolumler.json'),JSON.stringify({duration,mobileDuration,chapters,checks:{realBackend:true,mockedResponses:false,liveAi:false,syntheticContent:true,shareRevoked:true,javascriptErrors:failures}},null,2));
  console.log(JSON.stringify({desktopSeconds:duration,mobileSeconds:mobileDuration,chapters:chapters.length,output:out}));
} finally {
  if(context) await context.close();
  if(mobileContext) await mobileContext.close();
  if(browser) await browser.close();
  if(host) await new Promise(resolve=>host.close(resolve));
  if(app && app.exitCode===null) {
    app.kill('SIGTERM');
    await Promise.race([new Promise(resolve=>app.once('exit',resolve)),pause(5000)]);
    if(app.exitCode===null) app.kill('SIGKILL');
  }
  // Do not remove or overwrite any existing application data. This isolated
  // synthetic dataset remains in the printed temporary directory.
}
