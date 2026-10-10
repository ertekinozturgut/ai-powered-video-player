# VideoAtlas

.NET 10 ile video kütüphanesi, zaman kodlu transkripsiyon, SRT dışa aktarma, konu gruplama, kaynaklı video sohbeti ve iptal edilebilir iframe paylaşımı.

Geliştirme altyapısı: [ertekinozturgut/agent-squad](https://github.com/ertekinozturgut/agent-squad), MIT; kaynak sürümü ve uyarlamalar [plan belgesinde](docs/PLAN.md). Agent Squad bir AI runtime olarak sunulmaz; mimari ve kalite yaklaşımı kullanılır.

## Hızlı başlangıç

Gerekenler: `global.json` ile sabitlenen .NET 10 SDK, Node.js 22+, FFmpeg/FFprobe (libx264 ve AAC), Linux/macOS veya WSL.

```bash
bash scripts/setup.sh
bash scripts/dev.sh
```

Geliştirme sunucusu 5080 portunu kullanır. Development ortamında kayıt açık; Production ortamında kapalıdır. Hazır veya sabit parolalı hesap yoktur. Formdan kendi geliştirme hesabınızı oluşturun. Bulut onboarding arayüzü localhost önizlemesi sağlamaz.

API anahtarı yokken uygulama yine açılır: SRT içe aktarma, anahtar kelime konuları ve açıkça etiketlenmiş alıntı araması çalışır. STT, LLM ve embedding sağlayıcıları **ayrı ayrı**, yalnızca yöneticilerin erişebildiği `/admin/providers` panelinden seçilir. Önerilen yapı: ElevenLabs `scribe_v1`, OpenRouter `openai/gpt-4.1-mini` ve `openai/text-embedding-3-small` (512 boyut). OpenAI, Azure Speech, Azure AI Foundry OpenAI v1 ve n8n adaptörleri de vardır. Anahtarlar panelden şifreli kaydedilir veya `ProviderCredentials__OpenRouter` / `ProviderCredentials__ElevenLabs` güvenli ortam bağlamalarıyla okunur; tarayıcıya geri verilmez. Yönetici rolü ve kurulum: [sağlayıcı kılavuzu](docs/PROVIDERS.md). Sohbette paylaşılmış gerçek anahtarları yenileyin. Bu ortamda canlı OpenRouter/ElevenLabs çağrıları ağ politikası nedeniyle henüz doğrulanmadı.

## Kullanım

Yeni: [işleme/kalite/paylaşım/kullanım geliştirmeleri](docs/IMPROVEMENTS.md) ve [gerçek video kabul ölçümleri](docs/ACCEPTANCE.md). İşlem ekranında iptal/devam, yönetimde kullanım/hata takibi; paylaşımda görünüm ve izinli hesap seçenekleri bulunur.

1. Video yükleyin; işleme ve gerekiyorsa dış AI aktarımını onaylayın. MP4/MOV/WebM; en fazla 512 MiB ve 2 saat. Destekleyen STT sağlayıcısında konuşmacı ayrımını açıp 2–32 arası beklenen konuşmacı sayısını seçebilirsiniz.
2. İş durumu otomatik yenilenir. AI yoksa zaman kodlu SRT içe aktarın. SRT içe aktarımı mevcut metni değiştirir. Yeniden analiz, varsa metni yeniden kullanır ve AI konu başlıklarını üretir.
3. Metin satırına veya konu başlığına tıklayın; video o ana atlar. Metinde arama ve kategori filtresi vardır.
4. Sorunuzu yazın; cevaptaki kaynak düğmeleri ilgili zamana götürür. AI yanıtlarını kaynaklarla doğrulayın. Uzun videolarda bağlam seçimi sınırlıdır; tüm video hakkında eksiksiz çıkarım garantisi yoktur.
5. SRT indirin. “Paylaş ve göm” ile 1–90 günlük bağlantı ve iframe HTML alın. Site allowlist'i yalnızca gömmeyi sınırlar; linki bilen kişiler doğrudan açabilir.
6. Paylaşımı kapatmak tüm paylaşım uçlarını geçersiz kılar. Video silme medya, döküm ve paylaşım kaydını kaldırır.
7. Yüklerken veya video ekranındaki **Terimler ve yönergeler** bölümünden videoya özel sözlük/açıklama ve analiz tercihleri girin.
8. **Transkript kalite kontrolü** ile şüpheli bölümleri filtreleyin; sesi dinleyin, AI önerisi veya hedefli STT isteyin. Metin ve zaman düzeltmeleri açık onayla uygulanır; ham metin korunur, konular ve indeks yeniden hazırlanır. [Kalite kontrol kılavuzu ve sınırları](docs/TRANSCRIPT-QUALITY.md).
9. Konuşmacı ayrımlı analizden sonra oluşan konuşmacı etiketlerine ad/rol verin. Bitmiş bir analizi sonradan konuşmacı ayrımlı hale getirmek, sesin STT sağlayıcısına yeniden gönderilmesini ve tüm türetilmiş analizin yeniden kurulmasını gerektirir.

## Test ve dağıtım

```bash
dotnet test VideoAtlas.sln
npx playwright install chromium
npm run test:e2e
# Sistemde kurulu Chromium varsa:
CHROMIUM_PATH=/usr/bin/chromium npm run test:e2e
docker compose up -d --build
```

Docker Compose için önce `.env.example` içeriğini `.env` dosyasına alın; domain, ACME e-postası ve AI anahtarını güvenli şekilde girin. [Üretim işletim kılavuzu](docs/OPERATIONS.md), [test kanıtları](docs/VALIDATION.md), [mimari plan](docs/PLAN.md), [Orbital arayüz tasarımı](docs/DESIGN.md).

## Yapı

Gerçek arayüzden kullanım demosunu yeniden üretmek için [demo kayıt kılavuzu](docs/DEMO.md). Teslim paketi masaüstü/mobil MP4, açıklama altyazıları, örnek video/SRT ve çevrimdışı bölüm seçmeli izleyici içerir.

- `src/VideoAtlas.Domain`: video durumları, segment/konu modelleri.
- `src/VideoAtlas.Application`: erişim, kütüphane, SRT, arama ve kaynak doğrulamalı sohbet.
- `src/VideoAtlas.Infrastructure`: SQLite/Identity, FFmpeg, bağımsız AI adaptörleri/factory, şifreli sağlayıcı ayarları ve kalıcı işçi.
- `src/VideoAtlas.Web`: Razor, JSON API, oturum/CSRF, responsive JavaScript arayüzü.
- `tests`: xUnit, gerçek assembly mimari kontrolleri, API/FFmpeg ve Playwright testleri. Varsayılan tarayıcı testleri 5090/5091 portlarında ayrı geçici verilerle, canlı anahtarlardan yalıtılmış çalışır.
- `vendor/agent-squad`: kullanılan kaynak sürümü ve lisansı.

İlk dağıtım tek uygulama örneği ve kalıcı yerel disk içindir; çoklu replica desteklenmez. Harici AI ücretleri size aittir. Kayıtlı kullanıcı izolasyonu vardır; kurumsal SSO, ekip rolleri, ödeme ve canlı yayın kapsam dışındadır. Konuşmacı ayrımı ElevenLabs, local Whisper ve uyumlu n8n STT akışlarında desteklenir; OpenAI/Azure adaptörleri bu özelliği desteklemez. Local Whisper konuşmacı ayrımı sesi makineden çıkarmadan Resemblyzer ses gömmeleri ve kümeleme ile çalışır; `python -m pip install -r requirements-local-whisper.txt` bağımlılıkları kurulmalıdır.
