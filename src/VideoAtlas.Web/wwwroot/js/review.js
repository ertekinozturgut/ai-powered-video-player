import { $, api, element, time, toast } from './common.js';

export function setupReview(base, getDetail, refresh) {
  let state, working = false, activeSegmentId = null, operationLabel = '';
  const dialog = $('#review-dialog'), contextDialog = $('#context-dialog'), audio = $('#review-audio');
  const segment = () => state?.segments.find(s => String(s.id) === $('#review-segment').value);
  const pending = id => state.issues.filter(i => i.segmentId === id && i.status === 'pending');
  const busy = () => working || ['Queued', 'Processing'].includes(getDetail()?.video.status);
  function update() {
    $('#context-open').disabled = busy();
    // The review dialog can safely be reopened while a targeted request is in flight.
    // Its controls stay locked, but the user can see progress and return to the same segment.
    $('#review-open').disabled = ['Queued', 'Processing'].includes(getDetail()?.video.status) || !getDetail()?.segments.length;
  }
  function setWorking(value) {
    working = value;
    $('#review-progress').hidden = !value;
    $('#review-progress-text').textContent = operationLabel || 'İncelenen bölüm işleniyor…';
    for (const root of [dialog, contextDialog])
      for (const control of root.querySelectorAll('button:not([data-close]), input, textarea, select')) control.disabled = value;
    if (!value) {
      $('#review-ai').disabled = !getDetail()?.aiConfigured || !segment();
      for (const id of ['review-listen', 'review-stt', 'review-save']) $(`#${id}`).disabled = !segment();
      $('#review-restore').disabled = !$('#review-revision').options.length;
    }
    update();
  }
  async function readState() { state = await api(`${base}/review`); }
  function makeTermRow(term = '', description = '') {
    const row = element('div', 'term-row');
    const termInput = element('input', 'form-control'); termInput.type = 'text'; termInput.maxLength = 100; termInput.placeholder = 'Terim'; termInput.value = term; termInput.dataset.term = '';
    const descriptionInput = element('input', 'form-control'); descriptionInput.type = 'text'; descriptionInput.maxLength = 500; descriptionInput.placeholder = 'Açıklama'; descriptionInput.value = description; descriptionInput.dataset.description = '';
    const remove = element('button', 'icon-button term-remove', '×'); remove.type = 'button'; remove.title = 'Terimi kaldır'; remove.setAttribute('aria-label', 'Terimi kaldır'); remove.addEventListener('click', () => row.remove());
    row.append(termInput, descriptionInput, remove); return row;
  }
  function fillTerms(terms) {
    const rows = $('#context-terms-rows'); rows.replaceChildren();
    (terms.length ? terms : [{term: '', description: ''}]).forEach(t => rows.append(makeTermRow(t.term, t.description)));
  }
  function contextTerms() {
    return [...$('#context-terms-rows').querySelectorAll('.term-row')].map(row => ({term: $('[data-term]', row).value.trim(), description: $('[data-description]', row).value.trim()})).filter(t => t.term || t.description);
  }
  function fillSegments() {
    const select = $('#review-segment'), previous = select.value;
    select.replaceChildren();
    for (const s of state.segments.filter(s => !$('#review-only-issues').checked || pending(s.id).length))
      select.append(new Option(`${time(s.start)} – ${time(s.end)} · ${s.text.slice(0, 90)}`, s.id));
    const wanted = activeSegmentId == null ? previous : String(activeSegmentId);
    if ([...select.options].some(o => o.value === wanted)) select.value = wanted;
    showSegment();
    $('#review-status').textContent = `${state.issues.filter(i => i.status === 'pending').length} uyarı · Metin sürümü ${state.transcriptVersion}. Uyarı olmaması metnin hatasız olduğunu garanti etmez.`;
    const revisions = $('#review-revision'); revisions.replaceChildren();
    if (state.hasOriginal) revisions.append(new Option('İlk ham metin (korunan özgün kayıt)', '-1'));
    for (const r of [...state.revisions].reverse())
      revisions.append(new Option(`Sürüm ${r.number} · ${r.reason} · ${new Date(r.createdAt).toLocaleString('tr')}`, r.number));
  }
  function showSegment() {
    const s = segment(), list = $('#review-issues'); list.replaceChildren(); audio.pause();
    activeSegmentId = s?.id ?? activeSegmentId;
    const mediaUrl = `${base}/media`;
    if (audio.getAttribute('src') !== mediaUrl) { audio.src = mediaUrl; audio.load(); }
    $('#review-text').value = s?.text ?? '';
    $('#review-start').value = s?.start ?? 0; $('#review-end').value = s?.end ?? 0; $('#review-edit-timing').checked = false;
    $('#review-verified').checked = false;
    const original = state.originalSegments.find(o => s && o.id === s.id && o.start === s.start && o.end === s.end);
    $('#review-original').textContent = original?.text ?? 'Bu zaman aralığıyla eşleşen ilk ham bölüm yok; ilk ham metnin tamamına sürüm menüsünden dönebilirsiniz.';
    if (!s) list.append(element('p', 'muted', 'Bu filtreyle eşleşen konuşma yok. Tüm konuşmaları görmek için filtreyi kapatın.'));
    for (const issue of s ? pending(s.id) : []) {
      const card = element('section', 'review-issue');
      const labels = {heuristic: 'Kural tabanlı uyarı · güven puanı değildir', llm: 'AI metin önerisi · sesle doğrulanmadı', 'llm-batch':'Tam metin AI taraması', silence:'Düşük ses / metin uyuşmazlığı', alignment:'Ses/metin zaman hizalama önerisi', 'stt-secondary':'İkinci STT önerisi', 'stt-main':'Ana STT önerisi', stt: 'STT yeniden çözümleme · kullanıcı onayı gerekli'};
      card.append(element('h3', '', labels[issue.source] ?? 'İnceleme'), element('p', '', issue.reason));
      if (issue.suggestion) {
        card.append(element('p', 'review-suggestion', issue.suggestion));
        const use = element('button', 'btn btn-outline-secondary', 'Öneriyi düzenleme alanına al'); use.type = 'button';
        use.addEventListener('click', () => { $('#review-text').value = issue.suggestion; $('#review-verified').checked = false; if (issue.suggestedStart != null && issue.suggestedEnd != null) { $('#review-start').value = issue.suggestedStart; $('#review-end').value = issue.suggestedEnd; $('#review-edit-timing').checked = true; } $('#review-text').focus(); });
        card.append(use);
      }
      const dismiss = element('button', 'btn btn-outline-secondary', 'Uyarıyı reddet'); dismiss.type = 'button';
      dismiss.addEventListener('click', () => run(`issues/${issue.id}/dismiss`)); card.append(dismiss); list.append(card);
    }
    if (s && !list.children.length) list.append(element('p', 'muted', 'Bu bölümde kayıtlı uyarı yok. Sesi dinleyebilir, elle düzenleyebilir veya AI incelemesi isteyebilirsiniz.'));
    setWorking(working);
  }
  async function run(path, extra = {}, closes = false, context = false) {
    if (busy() || !state) return;
    const error = context ? $('#context-error') : $('#review-error'); error.textContent = '';
    operationLabel = path.includes('/retranscribe') ? 'Seçili ses parçası çıkarılıyor ve yalnızca bu aralık yeniden çözümleniyor…'
      : path.includes('/ai') ? 'Seçili konuşma için AI önerisi hazırlanıyor…' : 'İşlem sürüyor; öneriler onaysız uygulanmaz…';
    setWorking(true);
    $('#review-status').textContent = operationLabel;
    try {
      await api(`${base}/review/${path}`, {method: 'POST', body: JSON.stringify({version: state.version, ...extra})});
      if (closes) { dialog.close(); contextDialog.close(); toast('Kaydedildi. Konu başlıkları ve indeks yeniden hazırlanıyor.'); }
      await refresh(); await readState();
      if (!closes) fillSegments();
    } catch (e) { error.textContent = e.message; $('#review-status').textContent = 'İşlem tamamlanmadı. Metin otomatik değiştirilmedi.'; }
    finally { operationLabel = ''; setWorking(false); }
  }
  $('#context-open').addEventListener('click', async () => {
    if (busy()) return;
    try {
      await readState(); fillTerms(state.context.terms);
      $('#context-instructions').value = state.context.instructions; $('#context-language').value = state.context.language === 'en' ? 'en' : 'tr'; $('#context-error').textContent = '';
      contextDialog.showModal();
    } catch (e) { toast(e.message); }
  });
  $('#context-form').addEventListener('submit', event => {
    event.preventDefault();
    run('context', {terms: contextTerms(), instructions: $('#context-instructions').value, language: $('#context-language').value}, true, true);
  });
  $('#context-add-term').addEventListener('click', () => {
    const rows = $('#context-terms-rows'); if (rows.children.length >= 50) return toast('En fazla 50 terim ekleyebilirsiniz.');
    const row = makeTermRow(); rows.append(row); $('[data-term]', row).focus();
  });
  $('#review-open').addEventListener('click', async () => {
    if (['Queued', 'Processing'].includes(getDetail()?.video.status)) return;
    try { if (!state || !working) await readState(); $('#review-error').textContent = ''; fillSegments(); dialog.showModal(); }
    catch (e) { toast(e.message); }
  });
  $('#review-segment').addEventListener('change', showSegment);
  $('#review-only-issues').addEventListener('change', fillSegments);
  $('#review-check').addEventListener('click', () => {
    if (confirm('Kural tabanlı uyarılar yeniden hesaplanacak. Önceki AI/STT önerileri ve reddedilen uyarılar temizlenecek; metin değişmeyecek. Devam edilsin mi?')) run('check');
  });
  function playSegment(s) {
    if (!s) return;
    const mediaUrl = `${base}/media`;
    if (audio.getAttribute('src') !== mediaUrl) { audio.src = mediaUrl; audio.load(); }
    const start = Math.max(0, s.start - 1), play = () => {
      $('#video').pause(); $('#video').currentTime = s.start; audio.currentTime = start;
      audio.play().catch(() => { $('#review-error').textContent = 'Ses başlatılamadı. Ses oynatıcısındaki başlat düğmesini kullanın.'; });
    };
    if (audio.readyState === HTMLMediaElement.HAVE_NOTHING) {
      // Start the request during the user's click so browsers allow playback once metadata arrives.
      audio.play().catch(() => { });
      audio.addEventListener('loadedmetadata', play, {once:true});
    } else play();
  }
  $('#review-listen').addEventListener('click', () => playSegment(segment()));
  audio.addEventListener('timeupdate', () => { const s = segment(); if (s && audio.currentTime >= s.end + 1) audio.pause(); });
  dialog.addEventListener('close', () => audio.pause());
  $('#review-ai').addEventListener('click', () => { const s = segment(); if (s) run(`segments/${s.id}/ai`); });
  $('#review-stt').addEventListener('click', () => { const s = segment(); if (s) run(`segments/${s.id}/retranscribe`); });
  $('#review-compare').addEventListener('click', () => { const s = segment(); if (s) run(`segments/${s.id}/compare`); });
  $('#review-align').addEventListener('click', () => { const s = segment(); if (s) run(`segments/${s.id}/align`); });
  $('#review-edit-form').addEventListener('submit', event => {
    event.preventDefault(); const s = segment();
    if (s) run('edit', {segmentId: s.id, text: $('#review-text').value, audioVerified: $('#review-verified').checked, ...($('#review-edit-timing').checked ? {start:Number($('#review-start').value),end:Number($('#review-end').value)} : {})}, true);
  });
  $('#review-restore').addEventListener('click', () => {
    if (confirm('Seçilen metin sürümü geri yüklenecek. Konular ve indeks yeniden hazırlanacak; paylaşılan video da güncellenecek. Devam edilsin mi?'))
      run(`restore/${$('#review-revision').value}`, {}, true);
  });
  async function inspectSegment(segmentId) {
    if (busy()) return;
    try {
      // This is called directly from the inline AI suggestion button. Begin the selected
      // audio while the review state is loading, so the correction always has its context.
      playSegment(getDetail()?.segments.find(item => item.id === segmentId));
      activeSegmentId = segmentId; await readState(); $('#review-error').textContent = ''; fillSegments();
      const select = $('#review-segment'); if ([...select.options].some(option => option.value === String(segmentId))) select.value = String(segmentId);
      showSegment(); dialog.showModal(); playSegment(segment());
      const selected = segment(); if (selected) await run(`segments/${selected.id}/ai`);
    } catch (e) { toast(e.message); }
  }
  return {update, inspectSegment};
}
