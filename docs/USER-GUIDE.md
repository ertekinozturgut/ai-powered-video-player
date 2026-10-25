# VideoAtlas uygulama kullanım kılavuzu

Bu belge web arayüzündeki kullanıcı ve yönetici sayfalarını, credential tanımlama yollarını ve temel iş akışlarını anlatır. Kurulum için [Docker kılavuzunu](DOCKER.md), sağlayıcıların teknik ayrıntıları için [PROVIDERS.md](PROVIDERS.md), makineden makineye entegrasyon için [API.md](API.md) belgesini kullanın.

## 1. Roller ve erişim

- **Anonim ziyaretçi:** giriş, izin verilmişse kayıt, parola talebi, gizlilik ve erişimine izin verilen paylaşım bağlantılarını kullanabilir.
- **Kullanıcı:** kendi video/ses kütüphanesini, analizlerini, paylaşım bağlantılarını, hesap ayarlarını ve kendi API credential'larını yönetir. Başka kullanıcıların kayıtlarına erişemez.
- **Yönetici:** kullanıcı yetkilerine ek olarak sistem, işlem, API, AI sağlayıcı ve dil yönetimi sayfalarını kullanır.

Production ortamında self-service kayıt kapalıdır. İlk kullanıcıyı oluşturma ve yönetici yapma örneği:

```bash
export BOOTSTRAP_EMAIL='admin@example.com'
export BOOTSTRAP_PASSWORD='CHANGE-WITH-A-STRONG-PASSWORD'
docker compose exec -e BOOTSTRAP_EMAIL -e BOOTSTRAP_PASSWORD app dotnet VideoAtlas.Web.dll --create-user
docker compose exec -e BOOTSTRAP_EMAIL app dotnet VideoAtlas.Web.dll --grant-admin
unset BOOTSTRAP_EMAIL BOOTSTRAP_PASSWORD
```

Yerel Compose kullanıyorsanız komutlara `-f compose.local.yaml` ekleyin. Parolayı komut satırı argümanı olarak vermeyin; shell geçmişine yazılabilir.

## 2. Credential türleri

### 2.1 AI sağlayıcı credential'ları

Bu anahtarlar VideoAtlas'ın dış STT, LLM ve embedding servislerine bağlanması içindir. İki yöntem vardır.

#### Yönetici paneli — önerilen

1. Yönetici hesabıyla `/admin/providers` sayfasını açın.
2. STT, sohbet veya embedding grubundaki sağlayıcının **Ayarları düzenle** bağlantısını açın.
3. Endpoint, model/deployment, gerekiyorsa embedding boyutu ve yeni API anahtarını girin.
4. Profili kaydedin. Kayıtlı gizli değer daha sonra ekrana geri verilmez.
5. `/admin/providers` sayfasına dönün; birincil STT, isteğe bağlı ikinci STT, sohbet ve embedding seçimlerini ayrı ayrı yapın.
6. **Aktif sağlayıcıları kaydet** düğmesine basın.
7. Hassas olmayan kısa bir medya dosyasıyla gerçek bağlantı kabul testi yapın.

Panelden girilen anahtar profil kimliğine bağlı ASP.NET Data Protection ile şifrelenir. Şifreli değer SQLite içinde, şifreleme anahtarları `/data/keys` altında tutulur. Veritabanını başka sunucuya taşırken aynı `/data/keys` dizinini de güvenli biçimde taşıyın.

#### Ortam değişkeni / deployment secret

OpenRouter ve ElevenLabs için desteklenen bağlamalar:

| Deployment değeri | ASP.NET yapılandırma anahtarı | Kullanım |
|---|---|---|
| `OPENROUTER_API_KEY` | `ProviderCredentials__OpenRouter` | OpenRouter sohbet ve embedding profilleri |
| `ELEVENLABS_API_KEY` | `ProviderCredentials__ElevenLabs` | ElevenLabs STT profili |
| `AI_API_KEY` | `Ai__ApiKey` | Eski OpenAI uyumlu profil |
| `AI_CHAT_MODEL` | `Ai__ChatModel` | Eski OpenAI sohbet modeli |

Docker kurulumu için `.env.example` dosyasını `.env` olarak kopyalayıp değerleri yalnızca deployment makinesinde doldurun:

```dotenv
OPENROUTER_API_KEY=
ELEVENLABS_API_KEY=
AI_API_KEY=
AI_CHAT_MODEL=gpt-4o-mini
```

Ardından konteyneri yeniden oluşturun:

```bash
docker compose up -d --force-recreate app
```

Yerel kullanımda:

```bash
docker compose -f compose.local.yaml up -d --force-recreate app
```

Ortam anahtarı veritabanına kopyalanmaz. Aynı profil için panelden ayrıca anahtar kaydedilmişse paneldeki değer önceliklidir. Azure Speech, Azure AI Foundry ve n8n credential'ları profil/endpoint özelinde olduğu için `/admin/providers` üzerinden girilir.

Local Whisper ve yerel Ollama uzak API anahtarı istemez. Local Whisper'ı seçmek için:

```bash
docker compose exec app dotnet VideoAtlas.Web.dll --configure-local-whisper
docker compose restart app
```

### 2.2 VideoAtlas Analysis API credential'ları

Bu credential, OpenRouter veya ElevenLabs anahtarı değildir. Harici bir uygulamanın VideoAtlas'a medya gönderip analiz sonucunu okumasını sağlar.

Kullanıcı oluşturma yolu:

1. `/account/profile` sayfasını açın.
2. **API kimlik bilgileri** bölümünde bir ad ve geçerlilik süresi seçin.
3. Gerekli kapsamları seçin: okuma, yazma ve isteğe bağlı silme.
4. **API anahtarı oluştur** düğmesine basın.
5. Bir kez gösterilen `vat_...` değerini hemen güvenli secret manager'a kopyalayın.

Yönetici `/admin/api` sayfasından kayıtlı bir kullanıcıyı seçip onun adına credential oluşturabilir. Credential kullanıcıyla eşleşir; bu anahtarla oluşturulan analizler kullanıcının kütüphanesinde ve profilinde görünür.

Örnek kullanım:

```bash
export VIDEOATLAS_API_KEY='vat_PREFIX.SECRET'
curl -X POST https://videoatlas.example/api/v1/analyses \
  -H "Authorization: Bearer ${VIDEOATLAS_API_KEY}" \
  -F "file=@meeting.mp3" \
  -F "title=Weekly meeting" \
  -F "consent=true" \
  -F "language=en" \
  -F "speakerDiarization=true"
unset VIDEOATLAS_API_KEY
```

Anahtar kaybolursa geri okunamaz; eskisini iptal edip yenisini oluşturun. Tam endpoint ve kapsam listesi [Analysis API v1](API.md) belgesindedir.

## 3. Sayfalar ve kullanım

### `/account/login` — Giriş

- E-posta ve parola ile oturum açılır.
- Çok sayıda hatalı giriş hesabı geçici kilitleyebilir.
- **Şifremi unuttum** bağlantısı yerel kurulumda yönetici aracılı talep oluşturur; e-posta taşıyıcısı yoktur.
- Production ortamında kayıt kapalıysa kayıt bağlantısı gösterilmez.

### `/` — Video ve ses kütüphanesi

Bu sayfa kullanıcıya ait analizlerin başlangıç noktasıdır.

1. **Video/ses yükle** düğmesini açın.
2. MP4/MOV/WebM video veya desteklenen MP3, WAV, M4A/AAC, FLAC, OGG/Opus, AIFF ve WMA/ASF ses kaydını seçin.
3. Analiz dilini seçin.
4. Konuşmacı ayrımı gerekiyorsa etkinleştirin. Sayıyı boş bırakırsanız uyumlu sağlayıcı otomatik tahmin eder; biliniyorsa 2–32 arasında sabitleyin.
5. Terim/açıklama ve analiz yönergelerini satır bazında girin.
6. Dış AI aktarımı ve saklama koşullarını okuyup gerekli onayı verin.
7. Yüklemeyi başlatın. Karttan durum ve ilerlemeyi takip edin.

STT yapılandırılmamışsa kayıt `AwaitingTranscript` durumuna geçebilir. Bu durumda video sayfasından zaman kodlu SRT içe aktarın.

### `/videos/{id}` — Medya çalışma alanı

Ana analiz ekranıdır:

- Oynatıcı ve transkript satırları zaman koduyla birbirine bağlıdır.
- **Metin dökümü** sekmesinde arama, satır düzenleme, şüpheli bölüm işareti ve AI ile düzeltme bulunur.
- **Konu başlıkları** sekmesinde kategori filtreleme ve başlık/özet düzenleme yapılır.
- **Transkript kalite kontrolü** şüpheli yerleri toplar; ilgili ses aralığını dinleme, hedefli tekrar STT ve AI önerisi sunar.
- **Konuşmacılar** bölümünde etiketlere ad/rol verilebilir. Konuşmacı ayrımı sonradan açılırsa sesin yeniden analiz edileceği açıkça belirtilir.
- **Terimler ve yönergeler** özel isim, marka, lokasyon ve alan sözlüğünü günceller.
- **Videoya sorun** alanında video özeti, önemli noktalar, bölümler ve seçilen zaman aralığı gibi hızlı aksiyonlar vardır. Kaynak zaman kodlarına tıklayarak cevabı doğrulayın.
- **SRT indir** güncel metni dışa aktarır; **SRT içe aktar** mevcut transkripti değiştirir.
- **Yeniden analiz et** türetilmiş konu ve indeksleri yeniden hazırlar. Tam STT/konuşmacı ayarı değişikliği ayrı yeniden analiz onayı ister.
- **Videoyu sil** medya, transkript, konular ve paylaşım erişimini kalıcı olarak kaldırır.

Sohbet içeriği yalnızca açık tarayıcı sekmesinde tutulur. AI cevaplarını mutlaka kaynak zaman kodlarıyla doğrulayın.

### Paylaş ve göm / `/embed/{token}`

Video çalışma alanındaki **Paylaş ve göm** düğmesiyle:

1. Bağlantı süresini veya süresiz seçeneğini belirleyin.
2. Giriş zorunluluğu ve izin verilen özellikleri seçin.
3. İzinli iframe sitelerini geçerli URL olarak tek tek ekleyin.
4. Paylaşım bağlantısını veya iframe HTML'ini kopyalayın.

Site listesi yalnızca iframe gömmeyi sınırlar. Paylaşımı iptal etmek bağlantı ve iframe'i birlikte geçersiz kılar. Özel paylaşım erişiminde kullanıcı giriş sayfasına yönlendirilir.

### `/account/profile` — Hesabım

- Görünen ad, e-posta ve varsayılan arayüz dili güncellenir.
- E-posta değişikliği mevcut parola doğrulaması ister.
- Parola değiştirilebilir.
- Analysis API credential'ları oluşturulabilir ve iptal edilebilir.
- API üzerinden oluşturulan analizler ile kullanıcının son API istek kayıtları görülür.

### `/privacy` — İşleme, kullanım ve saklama

Uygulamanın medya işleme, dış sağlayıcı kullanımı, saklama ve silme davranışlarını açıklar. Üretime çıkmadan önce kurumunuza ait işletmeci ve gizlilik bilgileriyle güncelleyin.

## 4. Yönetici sayfaları

### `/admin/providers` — AI sağlayıcıları

STT, ikinci STT, sohbet modeli ve embedding rotalarını birbirinden bağımsız yönetir. Profil kartından endpoint/model/credential düzenlenir; üstteki seçimlerden aktif rotalar kaydedilir. Devam eden iş başladığı yapılandırmayla tamamlanır, yeni seçim sonraki işlerde kullanılır.

### `/admin/system` — Sistem yönetimi

- Toplam/aktif/kilitli kullanıcı ve yönetici sayılarını gösterir.
- Kullanıcı, e-posta, medya sayısı, hazır/hatalı analiz ve son yükleme bilgilerini listeler.
- Video adına veya hesap bilgisine göre arama, durum filtresi ve detay görünümü sağlar.
- Video detayında transkript, konu başlıkları, konuşmacılar, işlem adımları ve AI kullanım kayıtları bulunur.
- Son iş/AI hataları hata kodu ve trace bilgileriyle izlenir.

### `/admin/operations` — İşlemler ve kullanım

Son 100 işi, kuyruk/işlem durumu, adım, deneme ve hata koduyla gösterir. Günlük AI çağrı sayısı, işlenen ses, tahmini/sağlayıcının bildirdiği maliyet ve limitler burada izlenir.

### `/admin/api` — API yönetimi

- Kayıtlı kullanıcı seçilerek credential oluşturulur.
- Okuma/yazma/silme kapsamları ve geçerlilik süresi belirlenir.
- Aktif, süresi dolmuş veya iptal edilmiş credential'lar görüntülenir ve iptal edilir.
- Son API isteklerinde yöntem, yol, HTTP sonucu, süre, video ve trace kimliği izlenir.
- Authorization başlığı, ham secret, istek gövdesi, medya ve transkript loglanmaz.

### `/admin/localization` — Diller ve içerikler

1. Dil kodu/adı ekleyin veya mevcut dili açın.
2. Dili etkinleştirin ve gerekiyorsa varsayılan yapın.
3. Her statik metin anahtarının çevirisini düzenleyin.
4. JSON paketi içe aktarın veya mevcut dili JSON olarak dışa aktarın.
5. Sohbet sağlayıcısı yapılandırılmışsa **AI ile eksikleri doldur** ile yalnızca boş değerleri taslak olarak üretin.

AI mevcut insan çevirilerinin üzerine yazmaz. Yeni çevirileri yayınlamadan önce insan kontrolünden geçirin.

## 5. Önerilen ilk kullanım sırası

1. Uygulamayı kurun ve health endpoint'ini doğrulayın.
2. İlk kullanıcıyı oluşturup yönetici rolü verin.
3. `/admin/providers` üzerinden STT, LLM ve embedding ayarlarını yapılandırın.
4. Kısa ve hassas olmayan bir ses kaydı yükleyin.
5. İlk, orta ve son transkript zamanlarını dinleyerek kontrol edin.
6. Konu başlıklarını ve kaynaklı sohbet cevaplarını doğrulayın.
7. Gerekirse konuşmacı adlarını, terimleri ve hatalı metinleri düzeltin.
8. Test paylaşımı oluşturup yeni gizli pencerede bağlantı/iframe erişimini kontrol edin; sonra paylaşımı iptal edin.
9. Harici entegrasyon gerekiyorsa sınırlı kapsamlı ve kısa süreli Analysis API credential'ı oluşturun.
10. `/admin/operations`, `/admin/system` ve `/admin/api` ekranlarından iş ve hata kayıtlarını takip edin.

## 6. Güvenlik kontrol listesi

- Gerçek anahtarları Git'e veya dokümana eklemeyin.
- Sohbette daha önce paylaşılmış anahtarları sağlayıcı panelinden iptal edip yenileyin.
- Production `.env` dosyasına yalnızca deployment hesabının erişmesini sağlayın.
- API credential'ına yalnızca gereken kapsamı ve süreyi verin.
- SQLite, medya ve `/data/keys` dizinini şifreli ve erişim kontrollü yedekleyin.
- TLS olmadan production credential göndermeyin.
- n8n execution loglarında Authorization başlığı ve medya içeriği saklanmayacak şekilde workflow'u ayarlayın.
- Anahtar değişiminden sonra kısa bir kabul testi yapın; profil üzerinde “anahtar kayıtlı” yazması canlı bağlantının başarılı olduğunu kanıtlamaz.

## 7. Sık karşılaşılan durumlar

- **AI bağlantısı yapılandırılmamış:** `/admin/providers` içinde ilgili profil credential'ını kaydedin ve aktif rotayı seçin. Alternatif olarak SRT içe aktarın.
- **Analiz `AwaitingTranscript` durumunda:** STT kapalı/hatalı olabilir veya medyada çözülebilir ses bulunmamıştır. İş logunu kontrol edin ve gerekirse SRT içe aktarın.
- **Konuşmacı ayrımı desteklenmiyor:** ElevenLabs, Local Whisper veya uyumlu n8n STT profili seçin; ardından tam yeniden analiz başlatın.
- **API `401`:** credential yanlış, süresi dolmuş veya iptal edilmiş olabilir.
- **API `403`:** gereken `analysis:read`, `analysis:write` veya `analysis:delete` kapsamı yoktur.
- **Credential kayboldu:** geri görüntülenemez; yenisini oluşturup eskisini iptal edin.
- **Şifreli sağlayıcı anahtarı açılamıyor:** SQLite başka ortama taşınırken `/data/keys` eksik kalmış olabilir.
- **Yerel Whisper ilk işte yavaş:** model ilk analizde indirilir; ilerlemeyi uygulama ve container loglarından izleyin.
