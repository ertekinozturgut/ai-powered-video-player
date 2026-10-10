import { $, element, api, time, toast, stateNames, errors } from './common.js';
import { setupSharing } from './sharing.js';
import { setupReview } from './review.js';
import { setupOperations } from './operations.js';
const app = $('#player-app'), base = app.dataset.api, embedded = app.dataset.embedded === 'true';
const video = $('#video');
let detail, ready = false, history = [], asking = false, timer, activeId = -1, sourceSet = false, selectingSection = false;
let observedProcessingStage = '', observedProcessingAt = 0;
const review = embedded ? null : setupReview(base, () => detail, async () => { clearTimeout(timer); await load(); });
const operations = embedded ? null : setupOperations(base, async () => { clearTimeout(timer); await load(); });
const quickEdit = embedded ? null : $('#quick-edit-dialog');
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
  const audioOnly = v.mediaKind === 'audio';
  $('#workspace-kind').textContent = audioOnly ? 'SES ÇALIŞMA ALANI' : 'VİDEO ÇALIŞMA ALANI';
  $('#media-frame').classList.toggle('audio-mode', audioOnly);
  video.setAttribute('aria-label', audioOnly ? 'Ses oynatıcı' : 'Video oynatıcı');
  $('#video-duration').textContent = time(v.duration);
  $('#total-time').textContent = time(v.duration);
  $('#video-status').textContent = stateNames[v.status];
  $('#video-status').className = `status-badge status-${v.status.toLowerCase()}`;
  $('#analysis-label').textContent = detail.analysisMode === 'ai' ? '✦ AI konu analizi' : detail.analysisMode === 'keywords' ? '◇ Anahtar kelime gruplama' : '';
  if (detail.indexStatus === 'stale' || detail.indexStatus === 'missing') $('#analysis-label').textContent += ' · Anlamsal indeks için yeniden analiz gerekli';
  $('#chat-mode').textContent = detail.aiConfigured ? 'AI yanıtlarını kaynak konuşmalarla doğrulayın.' : 'Alıntı arama modu · AI bağlantısı yapılandırılmamış.';
  $('#processing-notice').hidden = !busy;
  const isSpeechTranscription = busy && v.stage?.startsWith('Konuşma parçası');
  if (busy && v.stage !== observedProcessingStage) {
    observedProcessingStage = v.stage;
    observedProcessingAt = Date.now();
  } else if (!busy) {
    observedProcessingStage = '';
    observedProcessingAt = 0;
  }
  const elapsedSeconds = observedProcessingAt ? Math.max(0, Math.floor((Date.now() - observedProcessingAt) / 1000)) : 0;
  const elapsedText = elapsedSeconds >= 60
    ? `${Math.floor(elapsedSeconds / 60)} dk ${elapsedSeconds % 60} sn`
    : `${elapsedSeconds} sn`;
  $('#processing-stage').textContent = isSpeechTranscription
    ? `${v.stage} · Ses modeli çalışıyor (${elapsedText})`
    : v.stage;
  if (isSpeechTranscription) $('#processing-progress').removeAttribute('value');
  else $('#processing-progress').value = v.progress;
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
    $('#speaker-open').disabled = busy || !v.mediaReady;
    $('[data-speaker-label]').textContent = detail.speakerDiarization || detail.speakers?.length ? 'Konuşmacılar' : 'Konuşmacılıya çevir';
    $('#delete-video').disabled = busy;
    $('#srt-file').disabled = busy || !v.mediaReady;
  }
  $('#segment-count').textContent = detail.segments.length
    ? `${detail.segments.length} ${document.documentElement.lang.startsWith('en') ? 'segments' : 'konuşma'}`
    : '';
  renderTranscript(); renderChapters();
}
function seek(seconds) {
  if (!sourceSet) return;
  const jump = () => { video.currentTime = Math.min(seconds, Number.isFinite(video.duration) ? video.duration : seconds); video.play().catch(() => toast('Oynatmak için medya oynatıcısındaki başlat düğmesine basın.')); };
  if (video.readyState) jump(); else video.addEventListener('loadedmetadata', jump, {once: true});
}
function renderTranscript() {
  const list = $('#transcript-list'); list.replaceChildren();
  const query = $('#transcript-search').value.toLocaleLowerCase('tr');
  const speakerName = segment => detail.speakers?.find(speaker => speaker.id === segment.speaker)?.name ?? segment.speaker?.replaceAll('_', ' ') ?? '';
  for (const segment of detail.segments.filter(s => `${speakerName(s)} ${s.text}`.toLocaleLowerCase('tr').includes(query))) {
    const row = element('div', 'transcript-row'); row.dataset.id = segment.id;
    const seekButton = element('button', 'transcript-seek'); seekButton.type = 'button'; seekButton.title = 'Bu anda videoya git';
    seekButton.append(element('span', 'timestamp', time(segment.start)));
    if (segment.speaker) seekButton.append(element('span', 'speaker-label', speakerName(segment)));
    seekButton.append(element('span', 'segment-text', segment.text));
    seekButton.addEventListener('click', () => seek(segment.start)); row.append(seekButton);
    const actions = element('div', 'segment-actions');
    if (detail.uncertainSegmentIds?.includes(segment.id)) {
      actions.append(element('span', 'review-badge', 'İnceleme gerekli'));
      if (!embedded) { const inspect = element('button', 'inline-edit inline-inspect', 'AI öner'); inspect.type = 'button'; inspect.title = 'AI ile incele ve doğru alternatif öner'; inspect.setAttribute('aria-label', inspect.title); inspect.addEventListener('click', () => review?.inspectSegment(segment.id)); actions.append(inspect); }
    }
    if (!embedded) actions.append(editButton('Metni ve zamanı düzenle', () => openTranscriptEdit(segment)));
    if (actions.children.length) row.append(actions);
    list.append(row);
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
    const row = element('div', 'chapter-card');
    const number = element('span', 'chapter-number', String(index + 1).padStart(2, '0'));
    const body = element('span', 'chapter-body');
    body.append(element('span', 'chapter-category', chapter.category), element('strong', '', chapter.title), element('span', 'muted', chapter.summary), element('span', 'timestamp', `${time(chapter.start)} — ${time(chapter.end)}`));
    const seekButton = element('button', 'chapter-seek'); seekButton.type = 'button'; seekButton.title = 'Bu anda videoya git'; seekButton.append(number, body); seekButton.addEventListener('click', () => seek(chapter.start)); row.append(seekButton);
    if (!embedded) row.append(editButton('Konu başlığını ve zamanı düzenle', () => openChapterEdit(chapter, detail.chapters.indexOf(chapter)))); list.append(row);
  });
  if (!list.children.length) list.append(element('p', 'panel-empty', 'Konu başlıkları analiz tamamlanınca burada görünür.'));
}
function editButton(label, action) {
  const button = element('button', 'inline-edit', 'Düzenle'); button.type = 'button'; button.title = label; button.setAttribute('aria-label', label); button.addEventListener('click', action); return button;
}
function openTranscriptEdit(segment) {
  $('#quick-edit-kind').textContent = 'METİN DÖKÜMÜ'; $('#quick-edit-title').textContent = 'Konuşmayı düzelt'; $('#quick-edit-help').textContent = 'Metni veya zaman kodlarını kaydetmeden önce ilgili sesi dinleyerek doğrulayın. Kaydetme, konu başlıklarını ve AI indeksini yeniden hazırlar.';
  $('#quick-edit-index').value = segment.id; $('#quick-edit-text').value = segment.text; $('#quick-edit-start').value = segment.start; $('#quick-edit-end').value = segment.end; $('#quick-edit-verified').checked = false;
  $('#quick-edit-chapter-fields').hidden = true; $('label[for="quick-edit-text"]').hidden = false; $('#quick-edit-text').hidden = false; $('#quick-edit-text').required = true; $('#quick-edit-verified-label').hidden = false; $('#quick-edit-verified').required = true; $('#quick-edit-error').textContent = ''; quickEdit.dataset.kind = 'transcript'; quickEdit.dataset.originalText = segment.text; quickEdit.dataset.reviewVersion = String(detail.reviewVersion); quickEdit.showModal();
}
function openChapterEdit(chapter, index) {
  $('#quick-edit-kind').textContent = 'KONU BAŞLIĞI'; $('#quick-edit-title').textContent = 'Konu başlığını düzenle'; $('#quick-edit-help').textContent = 'Başlık, özet, kategori ve zaman aralığını düzenleyin. Bu değişiklik video metnini veya anlamsal indeksi değiştirmez.';
  $('#quick-edit-index').value = index; $('#quick-edit-title-input').value = chapter.title; $('#quick-edit-category').value = chapter.category; $('#quick-edit-summary').value = chapter.summary; $('#quick-edit-start').value = chapter.start; $('#quick-edit-end').value = chapter.end;
  $('#quick-edit-chapter-fields').hidden = false; $('label[for="quick-edit-text"]').hidden = true; $('#quick-edit-text').hidden = true; $('#quick-edit-text').required = false; $('#quick-edit-verified-label').hidden = true; $('#quick-edit-verified').required = false; $('#quick-edit-error').textContent = ''; quickEdit.dataset.kind = 'chapter'; quickEdit.dataset.reviewVersion = String(detail.reviewVersion); quickEdit.showModal();
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
function closeSectionSelection() {
  selectingSection = false;
  $('#chat-section').hidden = true;
  if ($('#chat-action').value === 'section') $('#chat-action').value = 'question';
  document.querySelectorAll('[data-chat-action]').forEach(button => button.classList.remove('selected'));
}
$('#chat-form').addEventListener('submit', async event => {
  event.preventDefault(); if (!ready || asking) return;
  const question = $('#chat-question').value.trim(); if (!question) return;
  if (selectingSection) { $('#chat-error').textContent = 'Önce başlangıç ve bitiş zamanını seçip aralığı onaylayın.'; return; }
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
    if (action === 'section') closeSectionSelection();
    history.push({role: 'user', content: question}, {role: 'assistant', content: answer.answer.slice(0, 4000)});
    item.scrollIntoView({block: 'nearest'});
  } catch (error) { loading.remove(); $('#chat-error').textContent = error.message; $('#chat-question').value = question; }
  finally { asking = false; $('#chat-submit').disabled = !ready; $('#chat-question').focus(); }
});
$('#chat-question').addEventListener('keydown', event => { if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) { event.preventDefault(); $('#chat-form').requestSubmit(); } });
$('#chat-action').addEventListener('change', () => {
  const action = $('#chat-action').value; $('#chat-section').hidden = action !== 'section';
  document.querySelectorAll('[data-chat-action]').forEach(button => button.classList.toggle('selected', button.dataset.chatAction === action));
  $('#chat-question').value = {summary:'Videonun ana noktalarını kaynaklarıyla özetle.',highlights:'Videodaki en önemli noktaları, kaynak zaman kodlarıyla madde madde çıkar.',chapters:'Videonun konu başlıklarını zaman kodlarıyla listele.',decisions:'Videoda açıkça alınan kararları bul.',actions:'Videodaki yapılacak işleri, belirtilmişse sorumluları ve tarihleri bul.',section:'Seçili zaman aralığını kaynaklarıyla özetle.'}[action] ?? '';
});
document.querySelectorAll('[data-chat-action]').forEach(button => button.addEventListener('click', () => {
  if (button.dataset.chatAction === 'section') {
    selectingSection = true; $('#chat-error').textContent = ''; $('#chat-section').hidden = false;
    $('#section-start').value = Math.floor(video.currentTime); $('#section-end').value = Math.min(Math.floor(video.currentTime) + 30, detail.video.duration);
    document.querySelectorAll('[data-chat-action]').forEach(item => item.classList.toggle('selected', item === button));
    $('#section-start').focus(); return;
  }
  selectingSection = false;
  $('#chat-action').value = button.dataset.chatAction;
  $('#chat-action').dispatchEvent(new Event('change'));
  if (ready) $('#chat-form').requestSubmit();
}));
$('#section-confirm').addEventListener('click', () => {
  const start = Number($('#section-start').value), end = Number($('#section-end').value), duration = detail?.video?.duration ?? 0;
  if (!Number.isFinite(start) || !Number.isFinite(end) || start < 0 || end <= start || end > duration) { $('#chat-error').textContent = `Başlangıç ve bitiş zamanlarını 0:00–${time(duration)} aralığında doğru seçin.`; return; }
  selectingSection = false; $('#chat-action').value = 'section'; $('#chat-action').dispatchEvent(new Event('change'));
  if (ready) $('#chat-form').requestSubmit();
});
document.querySelectorAll('[data-question]').forEach(button => button.addEventListener('click', () => { $('#chat-question').value = button.dataset.question; if (ready) $('#chat-form').requestSubmit(); }));
$('#clear-chat').addEventListener('click', () => { if (asking) return; history = []; $('#chat-messages').replaceChildren(element('p', 'panel-empty', 'Sohbet temizlendi. Yeni bir soru sorabilirsiniz.')); });
if (!embedded) {
  setupSharing(base);
  const decimal = value => Number(String(value).trim().replace(',', '.'));
  const speakerDialog = $('#speaker-dialog'), reanalysisDiarization = $('#reanalysis-diarization');
  const updateSpeakerCount = () => {
    const enabled = reanalysisDiarization.checked;
    $('#reanalysis-speaker-count-field').hidden = !enabled;
    $('#reanalysis-speaker-count').disabled = !enabled;
    const unsupported = enabled && !detail.speakerDiarizationAvailable;
    $('#reanalysis-error').textContent = unsupported ? errors.diarization_unsupported : '';
    $('#reanalysis-submit').disabled = unsupported;
  };
  const openSpeakerDialog = () => {
    const speakers = detail.speakers ?? [];
    $('#speaker-identities').hidden = speakers.length === 0;
    const fields = $('#speaker-name-fields'); fields.replaceChildren();
    for (const speaker of speakers) {
      const label = element('label'); label.textContent = speaker.id.replaceAll('_', ' ');
      const input = element('input', 'form-control'); input.type = 'text'; input.maxLength = 80; input.value = speaker.name; input.dataset.speakerId = speaker.id;
      label.append(input); fields.append(label);
    }
    $('#speaker-name-error').textContent = '';
    reanalysisDiarization.checked = detail.speakerDiarization;
    $('#reanalysis-speaker-count').value = detail.expectedSpeakerCount ?? Math.max(2, speakers.length || 2);
    $('#reanalysis-warning').hidden = !ready;
    $('#reanalysis-submit').textContent = ready ? 'Tam yeniden analizi başlat' : 'Analizi bu ayarlarla başlat';
    updateSpeakerCount(); speakerDialog.showModal();
  };
  $('#speaker-open').addEventListener('click', openSpeakerDialog);
  $('#retry-video').addEventListener('click', async () => {
    if (detail.video.status !== 'Failed' || detail.segments.length === 0) { openSpeakerDialog(); return; }
    const button = $('#retry-video'); button.disabled = true;
    try {
      await api(`${base}/retry`, {method:'POST'}); clearTimeout(timer); await load();
      toast('Analiz tamamlanan metin korunarak kaldığı aşamadan sürdürüldü.');
    } catch (error) { toast(error.message); button.disabled = false; }
  });
  reanalysisDiarization.addEventListener('change', updateSpeakerCount);
  $('#speaker-names-form').addEventListener('submit', async event => {
    event.preventDefault(); const button = $('button[type="submit"]', event.currentTarget); button.disabled = true; $('#speaker-name-error').textContent = '';
    try {
      const names = Object.fromEntries([...document.querySelectorAll('[data-speaker-id]')].map(input => [input.dataset.speakerId, input.value.trim()]));
      await api(`${base}/speakers`, {method:'PUT', body:JSON.stringify({names})});
      await load(true); toast('Konuşmacı adları kaydedildi.');
    } catch (error) { $('#speaker-name-error').textContent = error.message; }
    finally { button.disabled = false; }
  });
  $('#reanalysis-form').addEventListener('submit', async event => {
    event.preventDefault(); const button = $('#reanalysis-submit'); button.disabled = true; $('#reanalysis-error').textContent = '';
    try {
      const enabled = reanalysisDiarization.checked;
      const speakerCount = enabled ? Number($('#reanalysis-speaker-count').value) : null;
      if (enabled && (!Number.isInteger(speakerCount) || speakerCount < 2 || speakerCount > 32)) throw new Error(errors.invalid_speaker_count);
      await api(`${base}/reanalyze`, {method:'POST', body:JSON.stringify({speakerDiarization:enabled, speakerCount})});
      speakerDialog.close(); clearTimeout(timer); await load();
      toast(enabled ? 'Konuşmacı ayrımlı tam analiz başlatıldı.' : 'Konuşmacı ayrımı olmadan tam analiz başlatıldı.');
    } catch (error) { $('#reanalysis-error').textContent = error.message; }
    finally { button.disabled = false; updateSpeakerCount(); }
  });
  $('#quick-edit-form').addEventListener('invalid', event => {
    event.preventDefault();
    $('#quick-edit-error').textContent = event.target === $('#quick-edit-verified') ? 'Kaydetmeden önce sesi dinlediğinizi onaylayın.' : 'Lütfen zorunlu alanları kontrol edin.';
  }, true);
  $('#quick-edit-form').addEventListener('submit', async event => {
    event.preventDefault(); if (!detail || !ready) return;
    const save = $('#quick-edit-save'); save.disabled = true; $('#quick-edit-error').textContent = '';
    try {
      const isTranscript = quickEdit.dataset.kind === 'transcript';
      const start = decimal($('#quick-edit-start').value), end = decimal($('#quick-edit-end').value);
      if (!Number.isFinite(start) || !Number.isFinite(end) || start < 0 || end <= start) throw new Error('Başlangıç ve bitiş zamanlarını doğru girin.');
      const latest = await api(base);
      if (latest.reviewVersion !== Number(quickEdit.dataset.reviewVersion)) {
        const current = isTranscript && latest.segments.find(segment => segment.id === Number($('#quick-edit-index').value));
        if (!current || current.text !== quickEdit.dataset.originalText) {
          detail = latest; render(); review?.update(detail);
          throw new Error('Bu konuşma siz düzenlerken değişti. Güncel metin yüklendi; lütfen düzenlemeyi tekrar açın.');
        }
      }
      const body = isTranscript
        ? {version: latest.reviewVersion, segmentId: Number($('#quick-edit-index').value), text: $('#quick-edit-text').value, audioVerified: $('#quick-edit-verified').checked, start, end}
        : {version: latest.reviewVersion, chapterIndex: Number($('#quick-edit-index').value), title: $('#quick-edit-title-input').value, summary: $('#quick-edit-summary').value, category: $('#quick-edit-category').value, start, end};
      await api(`${base}/review/${isTranscript ? 'edit' : 'chapters/edit'}`, {method: 'POST', body: JSON.stringify(body)});
      quickEdit.close(); clearTimeout(timer); await load(true);
      toast(isTranscript ? 'Metin kaydedildi; konu başlıkları ve AI indeksi yeniden hazırlanıyor.' : 'Konu başlığı kaydedildi.');
    } catch (error) { $('#quick-edit-error').textContent = error.message; }
    finally { save.disabled = false; }
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
