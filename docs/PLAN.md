# VideoAtlas — teslim kapsamı ve kararlar

Hedef: bireysel kullanıcıların videolarını yükleyip yazıya döktüğü, SRT indirdiği, zaman kodlarına atladığı, konu başlıklarını gezdiği ve kaynaklı sohbet ettiği .NET 10 web uygulaması. Açık paylaşım kullanıcı tarafından ayrıca etkinleştirilir; iptal edilebilir iframe bağlantısı üretir.

## Sağlayıcı bağımsız genişletme

STT, LLM ve embedding için ayrı application adapter interface’leri ve altyapıda `AiProviderFactory` eklendi. Kalıcı yönetici seçimi, istek/iş başına dondurulmuş konfigürasyon, model-kimlikli vektör indeksi, Data Protection ile şifreli anahtarlar ve güvenli ortam referansları kullanılır. ElevenLabs/OpenRouter önerilen ilk sağlayıcılardır; OpenAI, Azure Speech, Foundry OpenAI v1 ve sürümlü n8n webhook adaptörleri vardır. Yetkili panel `/admin/providers`; public kayıttan rol yükseltme yoktur. Detaylar [PROVIDERS.md](PROVIDERS.md).

## Transkript kalite ve video bağlamı genişletmesi

Video sahibi yüklemede/sonrasında terim-açıklama sözlüğü ve analiz yönergeleri tanımlar. Domain ham metni, son 10 sürümü, inceleme uyarılarını ve düzenleme sürümünü saklar. Application kalite kurallarını, onay/ret/geri yükleme ve çakışma denetimini yönetir; Infrastructure `ITranscriptReviewer` üzerinden seçili LLM ve hedefli STT kullanır. LLM önerileri asla otomatik yayınlanmaz. Onaylanan değişiklikler aynı zaman kodlarıyla kalıcı yeniden analiz kuyruğuna alınır. Kapsam, güvenlik ve bilinen sınırlar [TRANSCRIPT-QUALITY.md](TRANSCRIPT-QUALITY.md) içinde.

## Mimari

İşleme kontrol noktaları, iptal, tüm metin kalite taraması, ikinci STT/hizalama, iddia bazlı kaynaklar, özel paylaşım ve kullanım genişlemesi: [IMPROVEMENTS.md](IMPROVEMENTS.md). Gerçek kabul planı: [ACCEPTANCE.md](ACCEPTANCE.md). Ayrı worker/nesne depolama bu teslimde yapılmadı.

- Clean Architecture: Domain → bağımsız C#; Application → Domain; Infrastructure → Application; Web → composition root ve HTTP/Razor adaptörleri.
- SQLite / EF Core, tek uygulama örneği, kalıcı yerel disk. Süreç yeniden başlatıldığında yarım kalan işler tekrar kuyruğa girer. Ölçekleme için PostgreSQL + nesne depolama + ayrı worker sonraki adımdır.
- ASP.NET Core Identity, HttpOnly cookie, antiforgery, sahiplik kontrolleri. Kayıt production'da kapalı; yönetici CLI ile kullanıcı açar. Demo hesap/parola yok.
- FFmpeg güvenilir sabit argümanlarla, shell olmadan ve süre sınırıyla çalışır; ağ protokolleri kapalı. Videolar tarayıcı için MP4'e dönüştürülür. 512 MiB yükleme, 2 saat süre sınırı, kullanıcı başına depolama kotası.
- OpenAI Whisper zaman kodlu transkripsiyon (8 dakikalık ses parçaları); OpenAI JSON yanıtları ile konu gruplama ve kaynak segment ID'leriyle soru-cevap. Anahtar yalnızca sunucudadır. İstemci girdisi veya model cevabı HTML olarak çalıştırılmaz.
- API anahtarı yoksa SRT içe aktarma, yerel konu gruplama ve açıkça işaretlenmiş alıntı arama kullanılabilir. Bunlar AI doğrulaması sayılmaz.
- Paylaşım: 256 bit capability token, veritabanında yalnızca SHA-256 özeti; sona erme, iptal ve CSP frame-ancestors allowlist. Üçüncü taraf cookie gerekmez. Paylaşılan videonun metin ve sohbeti de erişime açılır.
- Gizlilik: içerik/log ayrımı, açık yükleme onayı, video silmede medya ve döküm temizliği. Harici AI veri aktarımı açıklanır.

## Kabul kriterleri

1. Happy: kullanıcı video yükler; iş durumu kalıcıdır; işlenmiş video range isteği ile oynar; SRT zamanları videoyla eşleşir.
2. Happy: metin satırı, konu veya sohbet kaynağına tıklamak oynatıcıyı ilgili zamana götürür.
3. Happy: video metnine dayalı yanıt geçerli kaynak ID'leri döndürür; bağlam dışı soru için yeterli kanıt olmadığı söylenir.
4. Validation: bozuk/zaman aralığı geçersiz SRT, yanlış dosya türü, aşırı boyut ve sürede video reddedilir.
5. Conflict: işlemdeki video değiştirilmez/silinmez; yinelenen yeniden-işleme çağrıları ikinci iş üretmez.
6. Security: başka kullanıcının video, SRT, medya ve paylaşım ayarlarına erişilemez. CSRF engellenir. İptal edilen/süresi dolan token tüm paylaşım uçlarında geçersizdir.
7. UI: mobil yerleşim, klavye kullanımı, etiketli formlar, yükleniyor/hata/boş durumları; dış CDN bağımlılığı yok.

## Agent Squad kullanımı

Kaynak `ertekinozturgut/agent-squad`, commit `a34336d43fa3f1770b4905bb8d5ec3f80ff60d1f`, MIT lisansı ile `vendor/agent-squad` altında korunur. Bu bir AI runtime değildir. Saf .NET katmanları, TimeProvider, yasak API denetleyicisi, mimari testler, güvenlik kontrolleri ve izlenebilirlik yaklaşımı uygulanır.

Uyarlama: ilk teslim ASP.NET Core Razor + Bootstrap ve hafif JavaScript kullanır; upstream Company/Orders örnekleri uygulama olarak kopyalanmaz. Kaynak ağacı boş olduğu için otomatik branch/merge runner çalıştırılmaz. Upstream 5-motor sertifikasyonu iddia edilmez; Stryker/CodeQL/Opengrep sonuçları çalıştırılmadan geçti sayılmaz. Güvenlik sertifikası veya KVKK uyumluluk onayı yerine doğrulanmış kontroller ve kalan yayın adımları raporlanır.
