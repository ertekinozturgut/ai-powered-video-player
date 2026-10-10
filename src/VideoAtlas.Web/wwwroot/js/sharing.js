import { $, api, toast } from './common.js';
export function setupSharing(base) {
  $('#share-open').addEventListener('click', () => $('#share-dialog').showModal());
  const values = select => Array.from(select.selectedOptions).map(option => option.value);
  const sites = $('#share-origins');
  function renderPicker(select, trigger, container, emptyLabel, removable = false) {
    const selected = values(select);
    trigger.firstChild.textContent = selected.length ? `${selected.length} seçim yapıldı ` : `${emptyLabel} `;
    container.replaceChildren();
    if (!select.options.length) container.append(Object.assign(document.createElement('p'), {className:'multi-picker-empty', textContent:'Henüz seçenek yok.'}));
    for (const option of select.options) {
      const label = document.createElement('label'), checkbox = document.createElement('input');
      checkbox.type = 'checkbox'; checkbox.checked = option.selected; checkbox.value = option.value;
      checkbox.addEventListener('change', () => { option.selected = checkbox.checked; renderPicker(select, trigger, container, emptyLabel, removable); });
      label.append(checkbox, document.createTextNode(option.text)); container.append(label);
      if (removable) {
        const remove = document.createElement('button'); remove.type = 'button'; remove.className = 'multi-picker-remove'; remove.textContent = '×'; remove.title = `${option.text} sitesini kaldır`;
        remove.addEventListener('click', () => { option.remove(); renderPicker(select, trigger, container, emptyLabel, removable); }); label.append(remove);
      }
    }
  }
  function activatePicker(trigger, menu) {
    trigger.addEventListener('click', () => { const show = menu.hidden; document.querySelectorAll('.multi-picker-menu').forEach(item => item.hidden = true); menu.hidden = !show; trigger.setAttribute('aria-expanded', String(show)); });
  }
  const siteTrigger = $('#share-origins-trigger'), siteMenu = $('#share-origins-menu'), siteOptions = $('#share-origin-options');
  activatePicker(siteTrigger, siteMenu);
  renderPicker(sites, siteTrigger, siteOptions, 'İzinli siteleri seçin', true);
  function normalizedOrigin(value) {
    if (value === '*') return value;
    let url;
    try { url = new URL(value); } catch { return null; }
    return url.protocol === 'https:' && !url.username && !url.password && url.pathname === '/' && !url.search && !url.hash ? url.origin : null;
  }
  $('#add-share-origin').addEventListener('click', () => {
    const entered = $('#share-origin-input').value.trim(), origin = normalizedOrigin(entered);
    if (!origin) { $('#share-error').textContent = 'Yalnızca * veya https://site.example biçiminde geçerli bir site adresi ekleyebilirsiniz.'; return; }
    if (Array.from(sites.options).some(option => option.value === origin)) { $('#share-error').textContent = 'Bu site zaten listede.'; return; }
    $('#share-error').textContent = ''; sites.add(new Option(origin, origin, true, true)); $('#share-origin-input').value = ''; renderPicker(sites, siteTrigger, siteOptions, 'İzinli siteleri seçin', true);
  });
  $('#share-origin-input').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); $('#add-share-origin').click(); } });
  $('#share-form').addEventListener('submit', async event => {
    event.preventDefault(); $('#create-share').disabled = true; $('#share-error').textContent = '';
    try {
      const result = await api(`${base}/share`, {method: 'POST', body: JSON.stringify({days: Number($('#share-days').value), origins: values(sites), requireLogin:false, viewers:[], presentation:{theme:$('#share-theme').value,startAt:Number($('#share-start').value),showTranscript:$('#share-transcript').checked,showChapters:$('#share-chapters').checked,chatEnabled:$('#share-chat').checked}})});
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
