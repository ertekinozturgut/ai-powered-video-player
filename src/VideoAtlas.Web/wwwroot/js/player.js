import { $, element, api, time, toast, stateNames, errors } from './common.js';
import { setupSharing } from './sharing.js';
import { setupReview } from './review.js';
import { setupOperations } from './operations.js';
const app = $('#player-app'), base = app.dataset.api, embedded = app.dataset.embedded === 'true';
const video = $('#video');
let detail, ready = false, history = [], asking = false, timer, activeId = -1, sourceSet = false;
const review = embedded ? null : setupReview(base, () => detail, async () => { clearTimeout(timer); await load(); });
const operations = embedded ? null : setupOperations(base, async () => { clearTimeout(timer); await load(); });
async function load(rethrow = false) {
  try {
    const next = await api(base);
    if (detail && next.transcriptVersion !== detail.transcriptVersion) {
      history = []; $('#chat-messages').replaceChildren(element('p', 'panel-empty', 'Metin sürümü değişti. Eski kaynakları kullanmamak için sohbet temizlendi.'));
    }
    detail = next; render(); review?.update(detail); await operations?.update(detail);
    if (['Queued', 'Processing'].includes(detail.video.status)) timer = setTimeout(load, 2500);
  } catch (error) { $('#player-error').textContent = error.message; $('#player-error').hidden = false; if (rethrow) throw error; }
}
function render() {
  const v = detail.video;
  ready = v.status === 'Ready';
  const busy = ['Queued', 'Processing'].includes(v.status);
  $('#video-heading').textContent = v.title; document.title = `${v.title} · VideoAtlas`;
  $('#video-duration').textContent = time(v.duration);
  $('#total-time').textContent = time(v.duration);
  $('#video-status').textContent = stateNames[v.status];
  $('#video-status').className = `status-badge status-${v.status.toLowerCase()}`;
  $('#analysis-label').textContent = detail.analysisMode === 'ai' ? '✦ AI konu analizi' : detail.analysisMode === 'keywords' ? '◇ Anahtar kelime gruplama' : '';
  if (detail.indexStatus === 'stale' || detail.indexStatus === 'missing') $('#analysis-label').textContent += ' · Anlamsal indeks için yeniden analiz gerekli';
  $('#chat-mode').textContent = detail.aiConfigured ? 'AI yanıtlarını kaynak konuşmalarla doğrulayın.' : 'Alıntı arama modu · AI bağlantısı yapılandırılmamış.';
  $('#processing-notice').hidden = !busy;
  $('#processing-stage').textContent = v.stage;
  $('#processing-progress').value = v.progress;
  $('#player-error').hidden = v.status !== 'Failed' && v.status !== 'AwaitingTranscript';
  $('#player-error').textContent = v.status === 'AwaitingTranscript' ? 'Otomatik transkripsiyon hazır değil veya ses bulunamadı. SRT dosyası içe aktarabilir ya da AI yapılandırmasını tamamlayıp yeniden analiz edebilirsiniz.' : errors[v.errorCode] ?? v.stage;
  $('#chat-question').disabled = !ready; $('#chat-submit').disabled = !ready || asking;
  const presentation = detail.presentation;
  if (embedded && presentation) {
    document.body.classList.toggle('theme-minimal', presentation.theme === 'minimal');
    $('.chat-panel').hidden = !presentation.chatEnabled;
    $('.player-grid').classList.toggle('without-chat', !presentation.chatEnabled);
    $('#tab-transcript').hidden = !presentation.showTranscript;
    $('#tab-chapters').hidden = !presentation.showChapters;
    $('#srt-download').hidden = !presentation.showTranscript;
    $('.transcript-panel').hidden = !presentation.showTranscript && !presentation.showChapters;
    if (!presentation.showTranscript) { $('#transcript-panel').hidden = true; $('#chapters-panel').hidden = !presentation.showChapters; $('#tab-chapters').setAttribute('aria-selected', 'true'); }
    if (!presentation.showChapters) $('#chapters-panel').hidden = true;
  }
  $('#srt-download').classList.toggle('disabled', !ready); $('#srt-download').setAttribute('aria-disabled', String(!ready));
  if (ready) $('#srt-download').href = `${base}/subtitles.srt`;
  if (v.mediaReady && !sourceSet) {
    video.src = `${base}/media`; video.poster = `${base}/poster`; sourceSet = true;
    if (embedded && presentation?.startAt) video.addEventListener('loadedmetadata', () => { video.currentTime = Math.min(presentation.startAt, video.duration); }, {once:true});
    $('#video-placeholder').hidden = true;
  }
  const oldTrack = $('track', video);
  if (oldTrack) oldTrack.remove();
  if (ready && (!embedded || presentation?.showTranscript !== false)) { const track = element('track'); track.kind = 'captions'; track.label = 'Konuşma metni'; track.src = `${base}/subtitles.vtt`; video.append(track); }
  if (!embedded) {
    $('#share-open').disabled = !ready;
    $('#retry-video').disabled = busy;
    $('#delete-video').disabled = busy;
    $('#srt-file').disabled = busy || !v.mediaReady;
  }
  $('#segment-count').textContent = detail.segments.length ? `${detail.segments.length} konuşma` : '';
  renderTranscript(); renderChapters();
}
function seek(seconds) {
  if (!sourceSet) return;
  const jump = () => { video.currentTime = Math.min(seconds, Number.isFinite(video.duration) ? video.duration : seconds); video.play().catch(() => toast('Oynatmak için video üzerindeki başlat düğmesine basın.')); };
  if (video.readyState) jump(); else video.addEventListener('loadedmetadata', jump, {once: true});
}
function renderTranscript() {
  const list = $('#transcript-list'); list.replaceChildren();
  const query = $('#transcript-search').value.toLocaleLowerCase('tr');
  for (const segment of detail.segments.filter(s => s.text.toLocaleLowerCase('tr').includes(query))) {
    const row = element('button', 'transcript-row'); row.type = 'button'; row.dataset.id = segment.id;
    row.append(element('span', 'timestamp', time(segment.start)), element('span', 'segment-text', segment.text));
    if (detail.uncertainSegmentIds?.includes(segment.id)) row.append(element('span', 'review-badge', 'İnceleme gerekli'));
    row.addEventListener('click', () => seek(segment.start)); list.append(row);
  }
  if (!list.children.length) list.append(element('p', 'panel-empty', ready ? 'Bu sözcüklerle eşleşen konuşma yok.' : 'Metin dökümü hazır olduğunda burada görünecek.'));
  activeId = -1; highlight();
}
function renderChapters() {
  const filter = $('#category-filter'), selected = filter.value;
  filter.replaceChildren(new Option('Tüm kategoriler', ''));
  for (const category of [...new Set(detail.chapters.map(c => c.category))]) filter.append(new Option(category, category));
  filter.value = selected;
  const list = $('#chapters-list'); list.replaceChildren();
  detail.chapters.filter(c => !filter.value || c.category === filter.value).forEach((chapter, index) => {
    const row = element('button', 'chapter-card');
    const number = element('span', 'chapter-number', String(index + 1).padStart(2, '0'));
    const body = element('span', 'chapter-body');
    body.append(element('span', 'chapter-category', chapter.category), element('strong', '', chapter.title), element('span', 'muted', chapter.summary), element('span', 'timestamp', `${time(chapter.start)} — ${time(chapter.end)}`));
    row.append(number, body); row.addEventListener('click', () => seek(chapter.start)); list.append(row);
  });
  if (!list.children.length) list.append(element('p', 'panel-empty', 'Konu başlıkları analiz tamamlanınca burada görünür.'));
}
function highlight() {
  $('#current-time').textContent = time(video.currentTime);
  const current = detail?.segments.find(s => s.start <= video.currentTime && video.currentTime < s.end)?.id ?? -1;
  if (current === activeId) return; activeId = current;
  for (const row of document.querySelectorAll('.transcript-row')) { const active = Number(row.dataset.id) === current; row.classList.toggle('current', active); row.setAttribute('aria-current', String(active)); }
}
video.addEventListener('timeupdate', highlight);
video.addEventListener('error', () => { toast('Video oynatılamadı. Oturumu veya paylaşım bağlantısının geçerliliğini kontrol edin.'); });
$('#transcript-search').addEventListener('input', renderTranscript);
$('#category-filter').addEventListener('change', renderChapters);
for (const name of ['transcript', 'chapters']) {
  const button = $(`#tab-${name}`);
  button.addEventListener('click', () => {
    for (const other of ['transcript', 'chapters']) { const active = name === other; $(`#tab-${other}`).setAttribute('aria-selected', String(active)); $(`#tab-${other}`).tabIndex = active ? 0 : -1; $(`#tab-${other}`).classList.toggle('selected', active); $(`#${other}-panel`).hidden = !active; }
  });
  button.addEventListener('keydown', event => { if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) { event.preventDefault(); const next = $(`#tab-${name === 'transcript' ? 'chapters' : 'transcript'}`); next.click(); next.focus(); } });
}
function message(role, text) {
  $('#chat-welcome')?.remove();
  const item = element('div', `chat-message ${role}`);
  item.append(element('span', 'message-label', role === 'user' ? 'Siz' : '✦ VideoAtlas'), element('p', '', text));
  $('#chat-messages').append(item); item.scrollIntoView({block: 'nearest'}); return item;
}
$('#chat-form').addEventListener('submit', async event => {
  event.preventDefault(); if (!ready || asking) return;
  const question = $('#chat-question').value.trim(); if (!question) return;
  asking = true; $('#chat-submit').disabled = true; $('#chat-error').textContent = '';
  message('user', question); $('#chat-question').value = '';
  const loading = message('assistant', 'Konuşmalar inceleniyor…'); loading.classList.add('loading-message');
  try {
    const action = $('#chat-action').value;
    const answer = await api(`${base}/chat`, {method: 'POST', body: JSON.stringify({question, history: history.slice(-6), action, ...(action === 'section' ? {start:Number($('#section-start').value),end:Number($('#section-end').value)} : {})})});
    // A concurrent owner edit must not attach an old answer to new segment text.
    clearTimeout(timer); await load(true);
    if (!ready || answer.transcriptVersion !== detail.transcriptVersion) throw new Error('Yanıt hazırlanırken metin değişti veya yeniden analiz başladı. İşlem tamamlanınca soruyu yeniden gönderin.');
    loading.remove(); const item = message('assistant', answer.answer);
    if (answer.claims?.length) {
      $('p', item).remove();
      for (const claim of answer.claims) {
        const block = element('div', 'grounded-claim'); block.append(element('p', '', claim.text));
        for (const source of claim.citations) { const button = element('button', 'citation', `▷ ${time(source.start)} – ${time(source.end)}`); button.title = source.text; button.addEventListener('click', () => seek(source.start)); block.append(button); }
        item.append(block);
      }
    } else if (answer.citations.length) {
      const citations = element('div', 'citations'); citations.append(element('small', '', 'VİDEODAKİ KAYNAKLAR'));
      for (const source of answer.citations) { const button = element('button', 'citation', `▷ ${time(source.start)} – ${time(source.end)}`); button.title = source.text; button.addEventListener('click', () => seek(source.start)); citations.append(button); }
      item.append(citations);
    }
    item.append(element('small', 'muted', answer.mode === 'ai' ? 'AI yanıtı · Kaynaklardan doğrulayın' : 'Alıntı araması · AI üretimi değildir'));
    if (answer.warning) item.append(element('p', 'quality-warning', answer.warning));
    history.push({role: 'user', content: question}, {role: 'assistant', content: answer.answer.slice(0, 4000)});
    item.scrollIntoView({block: 'nearest'});
  } catch (error) { loading.remove(); $('#chat-error').textContent = error.message; $('#chat-question').value = question; }
  finally { asking = false; $('#chat-submit').disabled = !ready; $('#chat-question').focus(); }
});
$('#chat-question').addEventListener('keydown', event => { if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); $('#chat-form').requestSubmit(); } });
$('#chat-action').addEventListener('change', () => {
  const action = $('#chat-action').value; $('#chat-section').hidden = action !== 'section';
  if (action === 'section') { $('#section-start').value = Math.floor(video.currentTime); $('#section-end').value = Math.min(Math.floor(video.currentTime) + 30, detail.video.duration); }
  $('#chat-question').value = {summary:'Videonun ana noktalarını kaynaklarıyla özetle.',decisions:'Videoda açıkça alınan kararları bul.',actions:'Videodaki yapılacak işleri, belirtilmişse sorumluları ve tarihleri bul.',section:'Seçtiğim zaman aralığını açıkla.'}[action] ?? '';
});
document.querySelectorAll('[data-question]').forEach(button => button.addEventListener('click', () => { $('#chat-question').value = button.dataset.question; if (ready) $('#chat-form').requestSubmit(); }));
$('#clear-chat').addEventListener('click', () => { if (asking) return; history = []; $('#chat-messages').replaceChildren(element('p', 'panel-empty', 'Sohbet temizlendi. Yeni bir soru sorabilirsiniz.')); });
if (!embedded) {
  setupSharing(base);
  $('#retry-video').addEventListener('click', async () => {
    $('#retry-video').disabled = true;
    try { await api(`${base}/retry`, {method: 'POST'}); clearTimeout(timer); await load(); }
    catch (error) { toast(error.message); $('#retry-video').disabled = false; }
  });
  $('#srt-file').addEventListener('change', async event => {
    const file = event.target.files[0]; if (!file) return;
    if (file.size > 2_000_000) { toast('SRT dosyası 2 MB sınırını aşıyor.'); return; }
    event.target.disabled = true;
    try { await api(`${base}/subtitles`, {method: 'POST', body: JSON.stringify({content: await file.text()})}); toast('Altyazı ve konu başlıkları hazır.'); await load(); }
    catch (error) { toast(error.message); }
    finally { event.target.value = ''; event.target.disabled = false; }
  });
  $('#delete-video').addEventListener('click', () => $('#delete-dialog').showModal());
  $('#confirm-delete').addEventListener('click', async event => {
    event.target.disabled = true;
    try { await api(base, {method: 'DELETE'}); window.location.assign('/'); }
    catch (error) { toast(error.message); event.target.disabled = false; }
  });
}
await load();
