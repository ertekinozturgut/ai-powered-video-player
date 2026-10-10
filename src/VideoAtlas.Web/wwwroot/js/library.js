import { $, element, api, csrf, time, stateNames } from './common.js';
let page = 0, videos = [], uploading = false;
const grid = $('#library-grid');
const english = document.documentElement.lang.toLowerCase().startsWith('en');
async function load() {
  try {
    videos = await api(`/api/videos?page=${page}`);
    $('#library-error').hidden = true;
    $('#video-count').textContent = videos.length;
    $('#previous-page').disabled = page === 0;
    $('#next-page').disabled = videos.length < 24;
    $('#page-label').textContent = `${english ? 'Page' : 'Sayfa'} ${page + 1}`;
    render();
  } catch (error) { $('#library-error').textContent = error.message; $('#library-error').hidden = false; }
  finally { $('#library-loading').hidden = true; }
}
function render() {
  grid.replaceChildren();
  const query = $('#library-search').value.toLocaleLowerCase('tr');
  const filtered = videos.filter(v => v.title.toLocaleLowerCase('tr').includes(query));
  $('#library-empty').hidden = videos.length > 0;
  for (const v of filtered) {
    const link = element('a', 'video-card'); link.href = `/videos/${v.id}`;
    const cover = element('div', 'card-cover');
    if (v.mediaReady) { const img = element('img'); img.src = `/api/videos/${v.id}/poster`; img.alt = ''; img.loading = 'lazy'; cover.append(img); if (v.mediaKind === 'audio') { cover.classList.add('audio-cover'); cover.append(element('span', 'audio-cover-mark', '♫')); } }
    else cover.append(element('span', 'cover-play', '▷'));
    cover.append(element('span', 'duration-label', v.duration ? time(v.duration) : (english ? 'Preparing' : 'Hazırlanıyor')));
    const body = element('div', 'card-body');
    body.append(element('span', `status-badge status-${v.status.toLowerCase()}`, stateNames[v.status]));
    body.append(element('h3', '', v.title));
    body.append(element('p', 'muted', new Intl.DateTimeFormat(english ? 'en' : 'tr', {dateStyle: 'medium'}).format(new Date(v.createdAt))));
    const footer = element('div', 'card-footer'), arrow = element('span', 'arrow-out');
    arrow.setAttribute('aria-hidden', 'true');
    footer.append(element('span', '', v.status === 'Ready' ? (english ? 'Transcript · Chapters · Chat' : 'Metin · Konular · Sohbet') : v.stage), arrow);
    body.append(footer); link.append(cover, body); grid.append(link);
  }
  if (!filtered.length && videos.length) grid.append(element('p', 'panel-empty', english ? 'No videos match this search.' : 'Bu aramayla eşleşen video yok.'));
}
$('#library-search').addEventListener('input', render);
$('#previous-page').addEventListener('click', () => { page--; load(); });
$('#next-page').addEventListener('click', () => { page++; load(); });
$('#video-file').addEventListener('change', () => {
  const file = $('#video-file').files[0];
  if (file && !$('#video-title').value) $('#video-title').value = file.name.replace(/\.[^.]+$/, '').slice(0, 180);
});
const uploadDiarization = $('#upload-diarization'), uploadSpeakerCountField = $('#upload-speaker-count-field'), uploadSpeakerCount = $('#upload-speaker-count');
uploadDiarization.addEventListener('change', () => {
  uploadSpeakerCountField.hidden = !uploadDiarization.checked;
  uploadSpeakerCount.disabled = !uploadDiarization.checked;
  if (uploadDiarization.checked) uploadSpeakerCount.focus();
});
const drop = $('#drop-zone');
for (const type of ['dragover', 'dragenter']) drop.addEventListener(type, event => { event.preventDefault(); drop.classList.add('dragging'); });
drop.addEventListener('dragleave', () => drop.classList.remove('dragging'));
drop.addEventListener('drop', event => {
  event.preventDefault(); drop.classList.remove('dragging');
  if (event.dataTransfer.files.length) { $('#video-file').files = event.dataTransfer.files; $('#video-file').dispatchEvent(new Event('change')); }
});
$('#upload-dialog').addEventListener('cancel', event => { if (uploading) event.preventDefault(); });
$('#upload-form').addEventListener('submit', async event => {
  event.preventDefault(); if (uploading) return;
  const file = $('#video-file').files[0];
  if (!file || file.size > 536870912 || !file.size) { $('#upload-error').textContent = 'Dosya 1 bayt ile 512 MiB arasında olmalıdır.'; return; }
  uploading = true;
  $('#upload-error').textContent = '';
  $('#upload-submit').disabled = true;
  $('#upload-submit').textContent = 'Yükleniyor…';
  $('#upload-progress').hidden = false;
  $('[data-close]', $('#upload-dialog')).disabled = true;
  const xhr = new XMLHttpRequest(); xhr.open('POST', '/api/videos'); xhr.setRequestHeader('X-CSRF-TOKEN', csrf());
  xhr.upload.onprogress = e => { if (e.lengthComputable) $('#upload-progress').value = e.loaded / e.total * 100; };
  xhr.onload = () => {
    let body; try { body = JSON.parse(xhr.responseText); } catch { body = {}; }
    if (xhr.status === 201) window.location.assign(`/videos/${body.id}`);
    else fail(body.title ?? (xhr.status === 401 ? 'Oturumunuz sona erdi. Yeniden giriş yapın.' : 'Yükleme tamamlanamadı. Dosya boyutunu ve bağlantıyı kontrol edin.'));
  };
  xhr.onerror = () => fail('Bağlantı kesildi. Tekrar deneyin.');
  xhr.send(new FormData(event.currentTarget));
});
function fail(message) {
  uploading = false; $('#upload-error').textContent = message;
  $('#upload-submit').disabled = false; $('#upload-submit').textContent = 'Yükle ve işlemeye başla →';
  $('[data-close]', $('#upload-dialog')).disabled = false;
}
await load();
setInterval(() => { if (!document.hidden && videos.some(v => ['Queued', 'Processing'].includes(v.status))) load(); }, 5000);
