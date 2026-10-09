import { test, expect } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import AxeBuilder from '@axe-core/playwright';

async function expectAccessible(page) {
  const result = await new AxeBuilder({page}).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
  expect(result.violations.map(v => ({rule: v.id, impact: v.impact, nodes: v.nodes.map(n => ({target: n.target, reason: n.failureSummary}))}))).toEqual([]);
}

const srt = '1\n00:00:00,000 --> 00:00:03,000\nÜrün lansmanı Ekim ayında yapılacak.\n\n2\n00:00:03,000 --> 00:00:06,000\nBütçe yüz bin lira olarak belirlendi.\n\n3\n00:00:06,000 --> 00:00:09,000\nGüvenlik testleri lansmandan önce tamamlanacak.\n';
test('dark visual system: authentication, responsive layout and keyboard access', async ({page}) => {
  await page.goto('/account/login');
  await expect(page.locator('html')).toHaveAttribute('data-bs-theme', 'dark');
  await expect(page.locator('.auth-orb')).toBeVisible();
  await expectAccessible(page);
  expect(await page.locator('.auth-orb').evaluate(img => img.complete && img.naturalWidth > 0)).toBeTruthy();
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', {name: 'İçeriğe geç'})).toBeFocused();
  await page.getByLabel('E-posta adresi').focus();
  await page.screenshot({path: 'artifacts/login-desktop.png', fullPage: true});
  for (const width of [320, 390, 768, 1024]) {
    await page.setViewportSize({width, height: 844});
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
    await expect(page.getByRole('button', {name: 'Giriş yap'})).toBeVisible();
    if (width === 390) {
      await expectAccessible(page);
      await page.screenshot({path: 'artifacts/login-mobile.png', fullPage: true});
    }
  }
  await page.emulateMedia({reducedMotion: 'reduce'});
  expect(await page.locator('.btn-primary').evaluate(el => getComputedStyle(el).transitionDuration)).toBe('0s');
});

test('video → transcript → seek → grounded chat → embed → revoke → delete', async ({page, browser}) => {
  const failures = []; page.on('pageerror', error => failures.push(error.message));
  await page.goto('/account/register');
  await page.getByLabel('E-posta adresi').fill(`browser-${Date.now()}@example.invalid`);
  await page.getByLabel('Parola', {exact: true}).fill('Synthetic-Test!234');
  await page.getByRole('button', {name: 'Hesap oluştur'}).click();
  await expect(page.getByRole('heading', {name: 'Video kütüphanesi.'})).toBeVisible();
  await expect(page.locator('#library-empty')).toBeVisible();
  await expectAccessible(page);
  await page.screenshot({path: 'artifacts/library-desktop.png', fullPage: true});
  await page.setViewportSize({width: 390, height: 844});
  await expectAccessible(page);
  await page.screenshot({path: 'artifacts/library-mobile.png', fullPage: true});
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.setViewportSize({width: 1440, height: 1050});
  const dir = mkdtempSync(join(tmpdir(), 'videoatlas-browser-'));
  try {
    const fixture = join(dir, 'meeting.mp4');
    execFileSync('ffmpeg', ['-nostdin','-y','-v','error','-f','lavfi','-i','testsrc2=size=640x360:rate=10','-f','lavfi','-i','sine=frequency=440:sample_rate=16000','-t','10','-c:v','libx264','-threads','1','-pix_fmt','yuv420p','-c:a','aac',fixture]);
    await page.getByRole('button', {name: '＋ Video yükle', exact: true}).click();
    await expectAccessible(page);
    await page.screenshot({path: 'artifacts/upload-desktop.png', fullPage: true});
    await page.locator('#video-file').setInputFiles(fixture);
    await page.getByLabel('Video başlığı').fill('Ürün stratejisi · sentetik test');
    await page.locator('input[name="consent"]').check();
    await page.getByRole('button', {name: 'Yükle ve işlemeye başla'}).click();
    await expect(page).toHaveURL(/\/videos\//);
    await expect(page.locator('#video-status')).toHaveText('SRT bekliyor', {timeout: 60000});
    await page.locator('#srt-file').setInputFiles({name: 'meeting.srt', mimeType: 'application/x-subrip', buffer: Buffer.from(srt)});
    await expect(page.locator('#video-status')).toHaveText('Hazır');
    await expect(page.locator('.transcript-row')).toHaveCount(3);
    await expect(page.locator('#toast')).toBeHidden({timeout: 10000});
    await page.locator('#video').evaluate(async video => { await video.play(); video.pause(); video.currentTime = 0; });
    await expectAccessible(page);
    await page.screenshot({path: 'artifacts/player-welcome-desktop.png', fullPage: true});
    const library = await page.context().newPage();
    await library.goto('/');
    await expect(library.locator('.video-card')).toHaveCount(1);
    await library.screenshot({path: 'artifacts/library-populated-desktop.png', fullPage: true});
    await library.close();
    for (const width of [320, 390, 768, 1024]) {
      await page.setViewportSize({width, height: 844});
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
      if (width === 390) {
        await expectAccessible(page);
        await page.screenshot({path: 'artifacts/player-welcome-mobile.png', fullPage: true});
        await page.locator('.chat-panel').screenshot({path: 'artifacts/chat-mobile.png'});
      }
    }
    await page.setViewportSize({width: 1440, height: 1050});
    await page.getByRole('button', {name: /00:03 Bütçe/}).click();
    await expect.poll(() => page.locator('#video').evaluate(v => v.currentTime)).toBeGreaterThanOrEqual(3);
    await page.locator('#video').evaluate(v => v.pause());
    await page.getByRole('tab', {name: 'Konu başlıkları'}).click();
    await expect(page.locator('.chapter-card').first()).toBeVisible();
    await page.getByRole('tab', {name: 'Metin dökümü'}).click();
    await page.getByLabel('Videoyla ilgili sorunuz').fill('Bütçe ne kadar?');
    await page.getByRole('button', {name: 'Soruyu gönder'}).click();
    await expect(page.locator('.chat-message.assistant')).toContainText('yüz bin lira');
    await expect(page.locator('.citation')).toHaveCount(1);
    await page.locator('.citation').click(); await page.locator('#video').evaluate(v => v.pause());
    await page.screenshot({path: 'artifacts/player-desktop.png', fullPage: true});
    const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('link', {name: 'SRT indir'}).click()]);
    expect(download.suggestedFilename()).toBe('transcript.srt');
    await page.getByRole('button', {name: 'Paylaş ve göm'}).click();
    await expectAccessible(page);
    await page.screenshot({path: 'artifacts/share-desktop.png', fullPage: true});
    await page.getByRole('button', {name: 'Bağlantı ve iframe oluştur'}).click();
    await expect(page.locator('#share-result')).toBeVisible();
    const embedUrl = await page.locator('#share-url').inputValue();
    expect(await page.locator('#iframe-code').inputValue()).toContain('<iframe');
    const anonymous = await browser.newContext(); const shared = await anonymous.newPage();
    await shared.goto(embedUrl); await expect(shared.locator('.transcript-row')).toHaveCount(3);
    await shared.screenshot({path: 'artifacts/embed-desktop.png', fullPage: true});
    await shared.setViewportSize({width: 390, height: 844});
    await expectAccessible(shared);
    expect(await shared.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
    // An actual cross-origin iframe does not require any authentication cookie.
    const host = await anonymous.newPage();
    await anonymous.route('https://video.example.invalid/**', async route => {
      const original = new URL(route.request().url());
      const upstream = await route.fetch({url: new URL(original.pathname + original.search, embedUrl).href});
      await route.fulfill({response: upstream});
    });
    const crossSiteEmbed = 'https://video.example.invalid' + new URL(embedUrl).pathname;
    await host.route('https://embed-host.example.invalid/', route => route.fulfill({contentType: 'text/html', body: `<html><body><iframe src="${crossSiteEmbed}" width="1200" height="850" title="test"></iframe></body></html>`}));
    await host.goto('https://embed-host.example.invalid/');
    await expect(host.frameLocator('iframe').locator('.transcript-row')).toHaveCount(3);
    await page.getByRole('button', {name: 'Mevcut paylaşımı kapat'}).click();
    await expect(page.locator('#share-result')).toBeHidden();
    const expired = await shared.goto(embedUrl); expect(expired.status()).toBe(404);
    await anonymous.close(); await page.getByRole('button', {name: 'Kapat', exact: true}).click();
    await page.setViewportSize({width: 390, height: 844});
    await page.screenshot({path: 'artifacts/player-mobile.png', fullPage: true});
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
    await page.getByRole('button', {name: 'Videoyu sil', exact: true}).click();
    await page.getByRole('button', {name: 'Kalıcı olarak sil'}).click();
    await expect(page).toHaveURL('/');
    expect(failures).toEqual([]);
  } finally { rmSync(dir, {recursive: true, force: true}); }
});
