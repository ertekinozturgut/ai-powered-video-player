# Sağlayıcı bağımsız AI ve yönetim

## Mimari ve varsayılan modeller

Üç ayrı kabiliyet vardır; biri kapalıyken diğerleri çalışabilir.

| İş | Adapter interface | Hazır adaptörler | Yeni kurulum önerisi |
|---|---|---|---|
| Ses → zaman kodlu metin | ISpeechToTextAdapter | ElevenLabs, OpenAI Whisper, Azure Speech Fast Transcription, n8n | ElevenLabs / scribe_v1 |
| Konu analizi + kaynaklı cevap | ILlmAdapter | OpenRouter, OpenAI, Azure AI Foundry OpenAI v1, n8n | OpenRouter / openai/gpt-4.1-mini |
| Anlamsal indeks ve soru vektörü | IEmbeddingAdapter | OpenRouter, OpenAI, Azure AI Foundry OpenAI v1, n8n | OpenRouter / openai/text-embedding-3-small / 512 boyut |

Kullanıcı belirli bir OpenRouter model kimliği vermediğinden yukarıdaki değiştirilebilir varsayılanlar seçildi. Hesabın bu modellere erişimi canlı kabul testinde doğrulanmalıdır.

`IAiProviderFactory` / `AiProviderFactory`, kalıcı seçimden uygun adaptörü üretir. Her istek ve video işi kendi değişmez yapılandırma görüntüsünü alır. Yönetici iş devam ederken seçim değiştirse bile o işin STT/LLM/embedding çağrıları başlangıçtaki profillerle tamamlanır. Yeni istekler yeni ayarları kullanır. HTTP sözleşmeleri altyapı katmanında; uygulama katmanı somut sağlayıcıya bağımlı değildir.

STT kapalıysa yeni video SRT bekler. Chat kapalıysa konu grupları anahtar kelime yöntemiyle, cevaplar açıkça etiketli alıntı aramasıyla verilir. Embedding kapalıysa metin bağlamı/kelime araması kullanılır. **Etkin sağlayıcı hatası yerel moda veya başka sağlayıcıya sessizce düşmez.** Ücretli POST çağrıları otomatik tekrarlanmaz.

## Admin paneline erişim

Panel: `/admin/providers`. Normal kayıt hiçbir zaman yönetici rolü vermez. Sunucu işletmecisi mevcut hesaba rol atar:

```bash
read -r -p 'Yönetici e-postası: ' BOOTSTRAP_EMAIL
export BOOTSTRAP_EMAIL
# Aynı Storage__Root / data volume üzerinde çalıştırın.
dotnet VideoAtlas.Web.dll --grant-admin
unset BOOTSTRAP_EMAIL
```

Yeni hesap gerekiyorsa önce OPERATIONS.md'deki `--create-user` komutunu kullanın. Rol atandıktan sonra yeniden giriş yapın. Rol geri alma: `--revoke-admin`. Yönetici yetkisi her admin isteğinde veritabanından kontrol edilir; eski cookie yetkiyi korumaz. Üretimde normal self-service kayıt kapalıdır.

Panelde:

1. İlgili sağlayıcının **Ayarları düzenle** bağlantısını açın.
2. API taban adresini, modeli/deployment adını ve gerekiyorsa vektör boyutunu girin.
3. API anahtarını kaydedin; boş bırakmak mevcut kayıtlı anahtarı korur.
4. STT, sohbet ve embedding seçimlerini ayrı ayrı yapıp **Aktif sağlayıcıları kaydet** düğmesine basın.
5. Kısa, hassas olmayan gerçek bir konuşma örneğiyle kabul testi yapın.

“Anahtar kayıtlı” yalnızca yapılandırma durumudur; başarılı canlı bağlantı iddiası değildir. Kaydetme ücretli test çağrısı göndermez. Eksik anahtarlı veya yanlış kabiliyette bir profil etkinleştirilemez. Aktif profilin anahtarını kaldırmadan önce yönlendirmeden çıkarmak gerekir. Eşzamanlı düzenlemeler sürüm kontrolüyle 409 döndürür.

## Anahtarlar ve bulut ortamı

İki güvenli çalışma yolu:

- **Admin paneli:** Anahtar, profil kimliğine bağlı ASP.NET Data Protection ile şifrelenerek SQLite içinde saklanır. HTML/JSON çıktısında ve doğrulama hatasında geri verilmez. Data Protection anahtar dizini ve SQLite yedekleri birlikte korunmalıdır; disk şifrelemesi ayrıca gerekir.
- **Güvenli ortam bağlaması:** `ProviderCredentials__OpenRouter` ve `ProviderCredentials__ElevenLabs`. Bunlar çalışma anında okunur, proxy placeholder'ları dahil veritabanına veya dosyaya kopyalanmaz. Panelden açıkça kaydedilen anahtar ortam anahtarının önüne geçer. Paneldeki anahtarı silmek ortam bağlamasını kaldırmaz.

Yeni veri dizininde bu iki bağlama mevcutsa ilk başlangıç önerilen rotaları seçer. **Mevcut veritabanında yönetici seçimi korunur**; aktif rotaları panelden değiştirin. İsteğe bağlı sunucu CLI'si, mevcut ortam bağlamalarını kullanarak üç önerilen rotayı seçer:

```bash
dotnet VideoAtlas.Web.dll --configure-providers < /dev/null
```

Bu komut, anahtarların zaten güvenli ortamda bulunduğunu varsayar. Alternatif olarak stdin'den `openRouter` ve `elevenLabs` alanlı JSON kabul eder; gerçek değerleri komut argümanına, Git dosyasına veya loga yazmayın. Proxy secret placeholder'ları için yalnızca ortam bağlaması yolunu kullanın.

Eski `Ai__ApiKey` yalnızca OpenAI uyumluluğu içindir. Yeni OpenRouter/ElevenLabs kurulumu için zorunlu değildir. Var olan sağlayıcı veritabanına sonradan ortam değişkeni eklemek kayıtlı rotaları değiştirmez.

**Bu teslimde canlı durum:** Bulut proxy'si `openrouter.ai` için HTTP 403 ve `api.elevenlabs.io` için CONNECT 403 döndürdü. Anahtarlar bu hata nedeniyle doğrulanmış değildir. Bu iki host ve güvenli anahtar bağlaması gereksinimi bulut taslağına eklendi; taslak kaydı çalışan ağı değiştirmez. Ortam ayarlarından değişikliği uygulayıp sohbet içinde paylaşılmış anahtarları **yenileriyle** güvenli alanlara girin. Anahtar değerleri kaynak koda, kaydedilmiş talimatlara veya bu belgeye alınmadı.

## Uç nokta güvenliği

Uygulama izinli host listesi `ProviderSecurity:AllowedHosts`. Varsayılan: api.openai.com, openrouter.ai, api.elevenlabs.io. Bulut ağ izinlerinden bağımsız ikinci bir kontroldür. Azure/n8n kullanırken işletmeci gerçek host'u listeye eklemelidir; admin paneli bu listeyi genişletemez.

- HTTPS ve varsayılan 443 portu zorunlu.
- Kullanıcı bilgisi, query/fragment içeren taban URL ve izin verilmeyen host reddedilir.
- HTTP yönlendirmeleri izlenmez; anahtar başka host'a taşınmaz.
- OpenRouter/ElevenLabs/OpenAI adaptörlerinin host'u kendi servisleriyle sınırlıdır.
- Özel host değişikliğinde kayıtlı eski anahtar otomatik taşınmaz; yeni anahtar girilmelidir.
- HTTP gövdeleri/hata yanıtları loglanmaz; hata kodu normalleştirilir. 402/429 kota, 401/403 kimlik, 400/404/422 yapılandırma hatasıdır.

Örnek operator ayarı (mevcut üç host'u koruyun):

```json
{
  "ProviderSecurity": {
    "AllowedHosts": [
      "api.openai.com", "openrouter.ai", "api.elevenlabs.io",
      "westeurope.api.cognitive.microsoft.com",
      "YOUR-RESOURCE.services.ai.azure.com",
      "YOUR-N8N.example.com"
    ]
  }
}
```

## Azure adaptörleri

**Azure Speech:** Fast Transcription API; taban adresi `https://REGION.api.cognitive.microsoft.com/`, model alanı konuşma locale'i (ör. tr-TR). Multipart audio + definition; `speechtotext/transcriptions:transcribe?api-version=2024-11-15`; Ocp-Apim-Subscription-Key. Cevap phrases içindeki offsetMilliseconds/durationMilliseconds değerleri saniyeye dönüştürülür. Speech kaynak bölgesi/SKU/API erişimi gerçek Azure kaynağında doğrulanmalıdır.

**Azure AI Foundry:** Bu adaptör **OpenAI v1 uyumlu** endpoint içindir; tüm Foundry API çeşitlerini otomatik keşfetmez. Örnek taban `https://RESOURCE.services.ai.azure.com/openai/v1/` veya desteklenen Azure OpenAI v1 adresi. api-key başlığı, model alanında deployment adı. Chat deployment'ı JSON object yanıtını, embedding deployment'ı seçilen dimensions değerini desteklemelidir. Entra managed identity bu sürümde yoktur.

Azure adaptörleri kontrollü sözleşme testlerinden geçti; gerçek Azure kaynağıyla test edilmedi.

## n8n senkron webhook sözleşmesi v1

Her kabiliyetin ayrı HTTPS webhook profili olabilir. Kimlik: `Authorization: Bearer <secret>`. Endpoint'e ek path eklenmez. Webhook işlemi **senkron JSON** yanıtıyla bitirmelidir; 202 + job ID/polling sözleşmesi bu sürümde desteklenmez. Zaman aşımı üç dakika. Girişleri ve sırları n8n execution loglarında saklamamayı ayrıca yapılandırın.

STT multipart alanları: `operation=speech`, `contractVersion=1`, `model`, `file` (audio/mpeg). Cevap:

```json
{"segments":[{"start":0.2,"end":3.4,"text":"Konuşma metni."}]}
```

Zamanlar **gönderilen ses parçasına göre** olmalıdır; video offset'ini uygulama ekler. Tam video offset'ini n8n'de tekrar eklemeyin.

Chat JSON isteği: `operation=chat`, `contractVersion=1`, `model`, `system`, `data`, `responseFormat=json`, `maxOutputTokens=2500`. System/data ayrımını koruyun. Cevap:

```json
{"content":"{\"answer\":\"Bütçe yüz bin lira.\",\"segmentIds\":[1]}"}
```

Konu analizi aynı chat sözleşmesini, farklı system/data ile kullanır; content içinde chapters dizisi beklenir. İç JSON biçimini gelen system belirler. Transcript içindeki komutları yürütmeyin; model yalnızca verilen segment ID'lerini kaynak göstermelidir.

Embedding JSON isteği: `operation=embedding`, `contractVersion=1`, `model`, `input: string[]`, `dimensions`. Cevap: `{"vectors":[[...],[...]]}`; giriş sırası korunmalı, her vektör tam belirtilen boyutta, sonlu ve sıfır olmayan değerli olmalıdır. Gerçek n8n akışı yayımlanmadı; bu sürüm adaptör ve test edilmiş sözleşmeyi sağlar.

## İndeks, hata ve yeniden işleme

İndeks kimliği adaptör türü + API endpoint + model/deployment + vektör boyutundan üretilir; anahtar kimliğe dahil edilmez. Anahtar rotasyonu gereksiz yeniden indeksleme yaratmaz. Farklı modelin vektörleri karşılaştırılmaz. Eşleşmeyen/eksik indekste UI yeniden analiz uyarır; metin tabanlı bağlam kullanılır. İşçide “Yeniden analiz et” metni koruyup konu ve indeksi yeniden üretir; STT'yi gereksiz yere tekrar ücretlendirmez. Yeni STT'yle tekrar transkripsiyon için videoyu yeni kayıt olarak yükleyin.

İşleme sonrası metin saklanır; konu/indeks aşaması hata verirse yeniden deneme metni tekrar tanıtmaz. İlk transkripsiyon parçası sırasında süreç kesilmesi tamamlanmamış ses çağrısını yeniden ücretlendirebilir. Büyük ölçek ve asenkron callback gereksinimleri ayrı geliştirmedir.

## Kabul testi

1. Yeni anahtarları güvenli alana girin; ağ izinlerini uygulayın.
2. Küçük, size ait/hassas olmayan **sesli** bir video yükleyin. SRT içe aktarmadan Hazır durumuna geçmeli.
3. SRT baş/orta/son zamanlarını dinleyerek doğrulayın.
4. AI konu kategorileri ve ready indeks durumunu doğrulayın.
5. Bilinen ve videoda bulunmayan iki soru sorun; gerçek kaynak zaman kodlarını kontrol edin.
6. Admin'den yalnızca LLM veya embedding seçimini değiştirin; yeniden analiz uyarısını ve yeni işlerde rotayı kontrol edin.

Yerel birim/sözleşme/iş hattı testleri canlı sağlayıcı kabulünün yerine geçmez. Standart xUnit, Playwright ve demo iş akışlarında gerçek credential bağlamaları özellikle temizlenir; Playwright 5090/5091 portlarında ayrı geçici veri alanlarını kullanır. `E2E_URL` ile dış sunucu verilirse onun yapılandırmasını değiştirmez; sadece özel test ortamını hedefleyin.
