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
export const errors = {invalid_video: 'Bu dosya desteklenen/geçerli bir video değil. MP4, MOV veya WebM yükleyin.', duration_limit: 'Video süresi 2 saati aşmamalıdır.', processing_timeout: 'Video dönüştürme zaman sınırına ulaştı.', ai_credentials: 'AI hizmetinin anahtarı geçersiz veya yetkisiz. Sunucu yöneticiniz kontrol etmelidir.', ai_quota: 'AI sağlayıcısının kotasına ulaşıldı.', ai_timeout: 'AI hizmetinin yanıt süresi doldu.', ai_invalid_response: 'AI hizmeti geçerli zaman kodu/analiz döndürmedi.', ai_unavailable: 'AI hizmetine şu anda ulaşılamıyor.', processing_failed: 'Video analizi tamamlanamadı. Yeniden deneyebilir veya SRT yükleyebilirsiniz.'};
Object.assign(errors, {resolution_limit: 'Video çözünürlüğü desteklenen sınırı aşıyor. En fazla 4096×4096 piksel eşdeğeri kullanın.', output_size_limit: 'Dönüştürülen video 1 GiB sınırını aşıyor. Daha kısa bir video yükleyin.'});
Object.assign(errors, {ai_configuration: 'Sağlayıcı modeli, API sözleşmesi veya uç noktası geçersiz. Yönetici panelinden kontrol edin.', provider_not_configured: 'Bu işlem için aktif sağlayıcı seçilmedi.', provider_secret_unavailable: 'Sağlayıcı anahtarı çözülemedi. Yönetici anahtarı yeniden kaydetmelidir.', provider_adapter_missing: 'Seçili sağlayıcı adaptörü bulunamadı.'});
Object.assign(errors, {ai_budget:'Günlük AI çağrı veya tahmini bütçe sınırına ulaşıldı. Tamamlanan parçalar korundu. Sınırları yöneticiyle kontrol edin.', stt_checkpoint_mismatch:'Kaydedilmiş ses parçaları farklı STT/model/sözlüğe ait. Eski ayara dönün veya işlem panelinden ses kontrol noktalarını açıkça sıfırlayın.'});
document.addEventListener('click', event => {
  const opener = event.target.closest('[data-open]');
  if (opener) document.getElementById(opener.dataset.open)?.showModal();
  const closer = event.target.closest('[data-close]');
  if (closer) closer.closest('dialog')?.close();
});
for (const form of document.querySelectorAll('.auth-card form')) {
  form.addEventListener('submit', () => { const button = $('button[type="submit"]', form); button.disabled = true; button.textContent = 'Lütfen bekleyin…'; });
}
