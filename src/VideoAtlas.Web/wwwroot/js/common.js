export const $ = (selector, root = document) => root.querySelector(selector);
export function element(tag, className = '', text = '') {
  const node = document.createElement(tag);
  node.className = className;
  node.textContent = text;
  return node;
}
export const csrf = () => $('meta[name="csrf-token"]')?.content ?? '';
export function time(seconds) {
  const total = Math.max(0, Math.floor(seconds || 0));
  const h = Math.floor(total / 3600), m = Math.floor(total % 3600 / 60), s = total % 60;
  return (h ? `${h}:` : '') + `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}
export function toast(message) {
  const box = $('#toast');
  box.textContent = message;
  box.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => { box.hidden = true; }, 5000);
}
export async function api(url, options = {}) {
  const headers = new Headers(options.headers ?? {});
  if (options.method && options.method !== 'GET') headers.set('X-CSRF-TOKEN', csrf());
  if (options.body && !(options.body instanceof FormData)) headers.set('Content-Type', 'application/json');
  const response = await fetch(url, {...options, headers, credentials: 'same-origin'});
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    const fallback = {401: 'Oturumunuz sona erdi. Yeniden giriş yapın.', 404: 'Video bulunamadı veya paylaşımın süresi doldu.', 429: 'İstek sınırına ulaşıldı. Biraz bekleyip tekrar deneyin.', 413: 'Dosya yükleme sınırını aşıyor.'};
    throw new Error(problem.title ?? fallback[response.status] ?? 'İşlem tamamlanamadı. Tekrar deneyin.');
  }
  return response.status === 204 || response.status === 202 ? null : response.json();
}
export const stateNames = {Queued: 'Sırada', Processing: 'İşleniyor', Ready: 'Hazır', AwaitingTranscript: 'SRT bekliyor', Failed: 'İşlem başarısız', Canceled: 'İptal edildi'};
export const errors = {invalid_video: 'Bu dosya desteklenen/geçerli bir video değil. MP4, MOV veya WebM yükleyin.', invalid_media: 'Bu dosya desteklenen bir video veya ses kaydı değil. MP4, MOV, WebM, MP3, WAV, M4A, AAC, FLAC ya da OGG yükleyin.', duration_limit: 'Medya süresi 2 saati aşmamalıdır.', processing_timeout: 'Medya dönüştürme zaman sınırına ulaştı.', ai_credentials: 'AI hizmetinin anahtarı geçersiz veya yetkisiz. Sunucu yöneticiniz kontrol etmelidir.', ai_quota: 'AI sağlayıcısının kotasına ulaşıldı.', ai_timeout: 'AI hizmetinin yanıt süresi doldu.', ai_invalid_response: 'AI hizmeti geçerli zaman kodu/analiz döndürmedi.', ai_unavailable: 'AI hizmetine şu anda ulaşılamıyor.', processing_failed: 'Medya analizi tamamlanamadı. Yeniden deneyebilir veya SRT yükleyebilirsiniz.'};
Object.assign(errors, {resolution_limit: 'Video çözünürlüğü desteklenen sınırı aşıyor. En fazla 4096×4096 piksel eşdeğeri kullanın.', output_size_limit: 'Dönüştürülen medya 1 GiB sınırını aşıyor. Daha kısa bir kayıt yükleyin.'});
Object.assign(errors, {ai_configuration: 'Sağlayıcı modeli, API sözleşmesi veya uç noktası geçersiz. Yönetici panelinden kontrol edin.', provider_not_configured: 'Bu işlem için aktif sağlayıcı seçilmedi.', provider_secret_unavailable: 'Sağlayıcı anahtarı çözülemedi. Yönetici anahtarı yeniden kaydetmelidir.', provider_adapter_missing: 'Seçili sağlayıcı adaptörü bulunamadı.'});
Object.assign(errors, {ai_budget:'Günlük AI çağrı veya tahmini bütçe sınırına ulaşıldı. Tamamlanan parçalar korundu. Sınırları yöneticiyle kontrol edin.', stt_checkpoint_mismatch:'Kaydedilmiş ses parçaları farklı STT/model/sözlüğe ait. Eski ayara dönün veya işlem panelinden ses kontrol noktalarını açıkça sıfırlayın.'});
Object.assign(errors, {diarization_unsupported:'Aktif STT sağlayıcısı konuşmacı ayrımını desteklemiyor. ElevenLabs veya uyumlu bir n8n STT akışı seçin.', invalid_speaker_count:'Konuşmacı sayısı 2–32 arasında olmalıdır.'});
Object.assign(errors, {local_diarization_unavailable:'Yerel konuşmacı ayrımı çalıştırılamadı. Python bağımlılıklarını ve ses kaydını kontrol edin.'});

const sidebarToggle = $('#sidebar-toggle');
const sidebarStorageKey = 'VideoAtlas.SidebarCollapsed';
const setSidebarCollapsed = collapsed => {
  document.body.classList.toggle('sidebar-collapsed', collapsed);
  if (!sidebarToggle) return;
  const label = collapsed ? sidebarToggle.dataset.showLabel : sidebarToggle.dataset.hideLabel;
  sidebarToggle.setAttribute('aria-expanded', String(!collapsed));
  sidebarToggle.setAttribute('aria-label', label);
  sidebarToggle.title = label;
};
if (sidebarToggle && !sidebarToggle.dataset.sidebarToggleReady) {
  sidebarToggle.dataset.sidebarToggleReady = 'true';
  let collapsed = false;
  try { collapsed = localStorage.getItem(sidebarStorageKey) === 'true'; }
  catch { /* The menu remains visible when browser storage is unavailable. */ }
  setSidebarCollapsed(collapsed);
  sidebarToggle.addEventListener('click', () => {
    const next = !document.body.classList.contains('sidebar-collapsed');
    setSidebarCollapsed(next);
    try { localStorage.setItem(sidebarStorageKey, String(next)); }
    catch { /* The control still works for the current page. */ }
  });
  window.addEventListener('storage', event => {
    if (event.key === sidebarStorageKey) setSidebarCollapsed(event.newValue === 'true');
  });
}
document.addEventListener('click', event => {
  const opener = event.target.closest('[data-open]');
  if (opener) document.getElementById(opener.dataset.open)?.showModal();
  const closer = event.target.closest('[data-close]');
  if (closer) closer.closest('dialog')?.close();
});
for (const form of document.querySelectorAll('.auth-card form')) {
  form.addEventListener('submit', () => { const button = $('button[type="submit"]', form); button.disabled = true; button.textContent = 'Lütfen bekleyin…'; });
}
