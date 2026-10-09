import { $, api, toast } from './common.js';
export function setupSharing(base) {
  $('#share-open').addEventListener('click', () => $('#share-dialog').showModal());
  $('#share-form').addEventListener('submit', async event => {
    event.preventDefault(); $('#create-share').disabled = true; $('#share-error').textContent = '';
    try {
      const result = await api(`${base}/share`, {method: 'POST', body: JSON.stringify({days: Number($('#share-days').value), origins: $('#share-origins').value.split(/[\n,]+/).map(s => s.trim()).filter(Boolean), requireLogin:$('#share-private').checked, viewers:$('#share-viewers').value.split(/[\n,]+/).map(s => s.trim()).filter(Boolean), presentation:{theme:$('#share-theme').value,startAt:Number($('#share-start').value),showTranscript:$('#share-transcript').checked,showChapters:$('#share-chapters').checked,chatEnabled:$('#share-chat').checked}})});
      const url = new URL(result.path, window.location.origin).href;
      $('#share-url').value = url;
      $('#iframe-code').value = `<iframe src="${url}" width="100%" height="800" title="VideoAtlas video ve sohbet" allow="fullscreen" allowfullscreen loading="lazy" referrerpolicy="no-referrer"></iframe>`;
      $('#copy-embed').hidden = result.requireLogin;
      if (result.requireLogin) $('#iframe-code').value = 'Bu özel bağlantı yalnızca izinli hesaplarla giriş yapılarak açılır; iframe gömme kapalıdır.';
      $('#share-result').hidden = false; toast('Paylaşım bağlantısı oluşturuldu.');
    } catch (error) { $('#share-error').textContent = error.message; }
    finally { $('#create-share').disabled = false; }
  });
  $('#copy-embed').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText($('#iframe-code').value); toast('Iframe kodu kopyalandı.'); }
    catch { $('#iframe-code').select(); toast('Kodu seçtik. Kopyalamak için Ctrl+C / ⌘C kullanın.'); }
  });
  $('#revoke-share').addEventListener('click', async event => {
    event.target.disabled = true;
    try { await api(`${base}/share`, {method: 'DELETE'}); $('#share-result').hidden = true; $('#share-url').value = ''; $('#iframe-code').value = ''; toast('Paylaşım bağlantısı kapatıldı.'); }
    catch (error) { $('#share-error').textContent = error.message; }
    finally { event.target.disabled = false; }
  });
}
