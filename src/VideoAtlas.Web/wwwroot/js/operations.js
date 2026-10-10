import { $, api, element, toast } from './common.js';
export function setupOperations(base, refresh) {
  let state, busy = false;
  async function update(detail) {
    try {
      state = await api(`${base}/operations`); busy = ['Queued', 'Processing'].includes(detail.video.status);
      const u = state.usage;
      $('#operations-summary').textContent = `${state.attempts} deneme · ${state.completedChunks}/${state.totalChunks} ses parçası · ${state.qualityReviewed}/${state.totalSegments} AI incelemesi · ${u.calls} API çağrısı (${u.failedOrUncertain} başarısız/belirsiz) · Tahmini $${u.estimatedUsd.toFixed(4)} · Sağlayıcı bildirimi $${u.reportedUsd.toFixed(4)} (${u.reportedCostCalls} çağrı) · ${u.unpricedCalls} tarifesiz çağrı. Günlük video çağrı sınırı: ${state.dailyCallLimit}; hesap tahmini bütçesi: $${state.dailyEstimatedBudgetUsd}. Tutarlar fatura değildir.`;
      $('#operations-steps').replaceChildren(...state.steps.map(s => element('li', '', `${s.label} · ${{running:'Sürüyor',done:'Tamamlandı',waiting:'Bekliyor',failed:'Başarısız',canceled:'İptal'}[s.state] ?? s.state}${s.errorCode ? ' · ' + s.errorCode : ''}`)));
      $('#cancel-job').disabled = !busy || state.cancelRequested;
      $('#full-review-job').disabled = busy || !detail.aiConfigured || !detail.segments.length;
      $('#stop-full-review').hidden = !state.fullReview; $('#stop-full-review').disabled = busy;
      $('#reset-chunks').hidden = !state.completedChunks || !!detail.segments.length;
      $('#reset-chunks').disabled = busy; $('#retention-save').disabled = busy;
      $('#retention-current').textContent = state.deleteAfter ? `Otomatik silme: ${new Date(state.deleteAfter).toLocaleString('tr')}` : 'Otomatik silme kapalı; süresiz saklanıyor.';
    } catch (e) { $('#operations-error').textContent = e.message; }
  }
  async function run(action, extra = {}) {
    if (!state) return;
    $('#operations-error').textContent = '';
    try { await api(`${base}/operations/${action}`, {method:'POST',body:JSON.stringify({version:state.version,...extra})}); toast('Tercihiniz kaydedildi.'); await refresh(); }
    catch (e) { $('#operations-error').textContent = e.message; }
  }
  $('#cancel-job').addEventListener('click', () => run('cancel'));
  $('#stop-full-review').addEventListener('click', () => { if (confirm('Tam metin AI kalite taraması kapatılıp kalan analiz aşamaları çalıştırılacak; konu/indeks API ücreti doğabilir. Devam edilsin mi?')) run('quality', {enabled:false}); });
  $('#full-review-job').addEventListener('click', () => { if (confirm('Tüm metin etkin AI sağlayıcısına gönderilecek ve ücret doğurabilir. Yalnızca bağlamla doğrudan desteklenen AI düzeltmeleri analiz sonunda otomatik uygulanır. Başlatılsın mı?')) run('quality', {enabled:true}); });
  $('#reset-chunks').addEventListener('click', () => { if (confirm('Kaydedilmiş STT parçaları silinecek ve ses tekrar ücretli çözümlenebilecek. Devam edilsin mi?')) run('reset-checkpoints'); });
  $('#retention-form').addEventListener('submit', e => { e.preventDefault(); if (!busy) run('retention', {days:Number($('#retention-days').value),confirmDeletion:$('#retention-confirm').checked}); });
  return {update};
}
