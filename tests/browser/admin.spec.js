import {test, expect} from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import {spawn, execFileSync} from 'node:child_process';
import {mkdtempSync, existsSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join, resolve} from 'node:path';
import {randomBytes} from 'node:crypto';

test('administrator can configure, select and persist providers without exposing credentials', async ({browser}) => {
  test.setTimeout(90000);
  const root = mkdtempSync(join(tmpdir(), 'videoatlas-admin-ui-'));
  const dotnet = process.env.DOTNET_PATH || (existsSync('/workspace/.dotnet/dotnet') ? '/workspace/.dotnet/dotnet' : 'dotnet');
  const project = resolve('src/VideoAtlas.Web'), dll = join(project,'bin/Debug/net10.0/VideoAtlas.Web.dll');
  const email = 'admin@example.invalid', password = 'Admin!' + randomBytes(16).toString('hex');
  const env = {...process.env,ASPNETCORE_ENVIRONMENT:'Development',ASPNETCORE_URLS:'http://127.0.0.1:5091',Storage__Root:root,Ai__ApiKey:'',ProviderCredentials__OpenRouter:'',ProviderCredentials__ElevenLabs:'',BOOTSTRAP_EMAIL:email,BOOTSTRAP_PASSWORD:password};
  execFileSync(dotnet,[dll,'--create-user'],{cwd:project,env,stdio:'pipe'});
  execFileSync(dotnet,[dll,'--grant-admin'],{cwd:project,env,stdio:'pipe'});
  const server=spawn(dotnet,[dll],{cwd:project,env,stdio:'ignore'});
  let context;
  try {
    await expect.poll(async()=>fetch('http://127.0.0.1:5091/health/ready').then(r=>r.status).catch(()=>0)).toBe(200);
    context=await browser.newContext({viewport:{width:1440,height:1050}});
    const page=await context.newPage(); const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.goto('http://127.0.0.1:5091/account/login');
    await page.getByLabel('E-posta adresi').fill(email);await page.getByLabel('Parola',{exact:true}).fill(password);
    await page.getByRole('button',{name:'Giriş yap'}).click();
    await page.getByRole('link',{name:'AI sağlayıcı yönetimi'}).click();
    await expect(page.getByRole('heading',{name:'AI sağlayıcıları.'})).toBeVisible();
    await page.getByRole('link',{name:'OpenRouter LLM gateway ayarlarını düzenle'}).click();
    await page.getByLabel('Yeni API anahtarı').fill('synthetic-browser-provider-secret');
    await page.getByRole('button',{name:'Sağlayıcı ayarlarını kaydet'}).click();
    await page.locator('#Chat').selectOption('chat-openrouter');
    await page.getByRole('button',{name:'Aktif sağlayıcıları kaydet'}).click();
    await page.reload();await expect(page.locator('#Chat')).toHaveValue('chat-openrouter');
    expect(await page.content()).not.toContain('synthetic-browser-provider-secret');
    const accessibility=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
    expect(accessibility.violations).toEqual([]);
    await page.screenshot({path:'artifacts/admin-providers-desktop.png',fullPage:true});
    await page.setViewportSize({width:390,height:844});
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
    await page.screenshot({path:'artifacts/admin-providers-mobile.png',fullPage:true});
    await page.getByRole('link',{name:'OpenRouter LLM gateway ayarlarını düzenle'}).click();
    await expect(page.getByLabel('Yeni API anahtarı')).toHaveValue('');
    const editAccessibility=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa','wcag22aa']).analyze();
    expect(editAccessibility.violations).toEqual([]);
    await page.screenshot({path:'artifacts/admin-provider-edit.png',fullPage:true});
    await page.goto('http://127.0.0.1:5091/admin/operations');
    await expect(page.getByRole('heading',{name:'İşlemler ve kullanım'})).toBeVisible();
    await expect(page.locator('main')).toContainText('0 API çağrısı');
    await page.setViewportSize({width:1440,height:1050});
    await page.screenshot({path:'artifacts/admin-operations.png',fullPage:true});
    expect(errors).toEqual([]);
  } finally {
    if(context)await context.close();
    server.kill('SIGTERM');
    await new Promise(resolve=>server.once('exit',resolve));
  }
});
