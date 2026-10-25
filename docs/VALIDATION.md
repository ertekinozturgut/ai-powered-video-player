# Doğrulama kanıtları

Tarih: 3 Ekim 2026. Bu belge uygulamanın bu çalışma ortamındaki doğrulamasını kaydeder; kamuya açık yayın veya canlı AI hizmeti onayı değildir.

## Geçen kontroller

| Kontrol | Sonuç / kapsam |
|---|---|
| `bash scripts/cloud-setup.sh` | Sabit SDK + frozen NuGet/npm kurulumunun yeniden çalışması; build **0 hata, 0 uyarı** |
| `dotnet test VideoAtlas.sln` | **35 geçti, 0 başarısız, 0 atlandı** |
| Mimari | Gerçek Domain/Application/Infrastructure/Web assembly'leri üzerinde ArchUnitNET içe bağımlılık; Domain framework bağımsızlığı; HTTP action yetki bildirimleri |
| Medya/API | Gerçek FFmpeg sentetik 10 saniyelik video dönüştürme, kalıcı kuyruk, SRT içe aktarma/indirme, WebVTT, HTTP 206 range |
| Dayanıklılık | Processing durumunda bırakılan işi yeniden başlatmada kurtarma; bozuk medya reddi; kontrollü hata kodları |
| Güvenlik | Kullanıcılar arası video/medya/paylaşım erişiminin engellenmesi; CSRF olmayan silmenin reddi; token hash saklama; paylaşım iptali; sona erme kuralı; HTTPS yönlendirmesi, Secure/HttpOnly session cookie ve üretimde kayıt kapalı |
| AI sözleşmeleri | **Kontrollü HTTP sağlayıcısı** ile transkripsiyon zaman offset'i, konu sınırları, 512 boyutlu indeks, kaynak ID doğrulaması, uydurma ID reddi, günlük soru kotası |
| AI tam iş hattı | Gerçek medya/ses işleme + **kontrollü sağlayıcı**: otomatik transkripsiyon → konu analizi → indeks → kaynaklı soru-cevap |
| Anlamsal arama | Uzun sentetik dökümde kelime eşleşmesi olmadan vektör benzerliğiyle ilgili segmenti bulma |
| Playwright | Güncel arayüzde **2 senaryo geçti** (host): duyarlı koyu tema/klavye erişimi ve kayıt → yükleme → SRT → metinden seek → konu sekmesi → alıntı sohbeti → SRT download → iframe → iptal → silme. Önceki arayüz sürümünde iş akışı Docker üzerinde de geçti. |
| Gerçek iframe | İki farklı HTTPS site origin'i tarayıcıda kontrollü yönlendirmeyle test edildi; anonim/oturumsuz paylaşım; iptalden sonra 404 |
| Release | `dotnet publish ... -c Release` başarılı; çıktı `artifacts/publish` |
| Docker build | `videoatlas:local` başarıyla oluşturuldu. Bulut proxy CA'sı **yalnızca build secret** ile eklendi; TLS doğrulaması açık |
| Docker çalışma | UID 1654 (`app`), root olmayan kullanıcı, salt okunur kök dosya sistemi, cap-drop ALL, no-new-privileges, kalıcı volume; health başarılı |
| Üretim CLI | Container içinde kullanıcı açma ve parola sıfırlama; yeniden başlatmada hesapların korunması |
| Caddy / HTTPS | Compose yapılandırması ve Caddy validation geçti. Yerel CA sertifikasını doğrulayarak **gerçek TLS** → Caddy → üretim uygulaması → CSRF/güvenli giriş → özel API erişimi geçti. Güvenilmeyen forwarded-header isteği HTTPS yönlendirmesini aşamadı |
| Şema | EF Core migration'ları uygulandı; `has-pending-model-changes` değişiklik yok bildirdi |
| Bağımlılıklar | NuGet transitive vulnerability kontrolü: bilinen açık yok; `npm audit`: 0 açık. Bu sonuç container işletim sistemi paketleri için güvenlik taraması değildir |
| Kaynak | `git diff --check` (uygulama dosyaları), shell syntax ve Compose config kontrolleri geçti |

Ekran görüntüleri: `artifacts/library-desktop.png`, `artifacts/player-desktop.png`, `artifacts/player-mobile.png`. Bunlardaki video ve konuşmalar sentetik test verisidir.

## Referans görsele göre arayüz yenilemesi

3 Ekim 2026 tarihinde siyah / mavi-mor-turkuaz Orbital teması uygulandı. Tasarım kararları [DESIGN.md](DESIGN.md) içinde.

- Son .NET çalıştırması: **35 geçti, 0 başarısız, 0 atlandı**.
- Son Playwright çalıştırması: **2 geçti**. Giriş ve oynatıcıda 320, 390, 768, 1024, 1440 px; kütüphane ve iframe'de masaüstü / 390 px. Yatay taşma yok; ana akışta yakalanan JavaScript hatası yok.
- axe-core: giriş ve kütüphanede masaüstü/mobil; yükleme ve paylaşım pencerelerinde masaüstü; oynatıcıda masaüstü/mobil; iframe'de mobil. WCAG 2 A/AA, 2.1 AA ve 2.2 AA etiketli otomatik kurallarda **0 ihlal**. Otomasyonun değerlendiremediği kontroller ve gerçek yardımcı teknoloji kullanıcı testleri tam erişilebilirlik sertifikası olarak yorumlanmamalıdır.
- Klavyeyle içeriğe atlama, görünür form odağı, azaltılmış hareket tercihi doğrulandı.
- Release publish başarılı; yeni SVG'ler ve CSS yayın çıktısında mevcut. `npm audit`: **0 açık**.
- Bu yenilemede Docker/HTTPS dağıtım testleri yeniden çalıştırılmadı; yukarıdaki Docker sonuçları önceki doğrulamaya aittir. Canlı AI ve kamuya açık yayın durumu değişmedi.

Gerçek tarayıcıdan alınan güncel görseller:

- `artifacts/login-desktop.png`, `artifacts/login-mobile.png`
- `artifacts/library-desktop.png`, `artifacts/library-mobile.png`, `artifacts/library-populated-desktop.png`
- `artifacts/player-welcome-desktop.png`, `artifacts/player-welcome-mobile.png`, `artifacts/chat-mobile.png`
- `artifacts/player-desktop.png`, `artifacts/player-mobile.png`
- `artifacts/upload-desktop.png`, `artifacts/share-desktop.png`, `artifacts/embed-desktop.png`

## Giderilen gerçek sorunlar

### Kullanım demosu — 3 Ekim 2026

- Gerçek .NET/Chromium UI kaydı: 15 adımlı masaüstü MP4 (98,92 saniye) ve yaklaşık 14 saniyelik anonim mobil kullanım.
- Kayıt scriptinde gerçek yükleme, SRT içe/dışa aktarma, zaman koduna atlama, metin araması, konu grupları, alıntı cevapları ve ayrı yerel sitedeki gerçek iframe doğrulandı. Yanıt taklidi yok; yakalanan JavaScript hatası yok.
- Demo paylaşımı iptal edildi; HTTP 404 doğrulandı. Geçici ayrı veri alanı kullanıldı; mevcut uygulama verisi değiştirilmedi.
- Çevrimdışı izleyici dosyaları yerel HTTP sunucusunda doğrulandı: her iki MP4 oynatıldı, bölüm seçimi doğru zamana atladı, 390 px genişlikte taşma olmadı. İndirilen SRT giriş metniyle eşleşti.
- FFmpeg ile iki nihai MP4'ün tamamı çözüldü; hata yok. ZIP bütünlük testi geçti.
- Canlı AI **kullanılmadı**; video/SRT sentetik, örnek sunum sessiz. Anahtar kelime gruplama ve alıntı araması açıkça etiketlendi. Bu kayıt otomatik konuşma tanıma veya üretken AI kabul testi değildir.
- Ayrıntılar ve yeniden üretim: [DEMO.md](DEMO.md). Ham görüntünün yalnızca mobil kayıt sırasındaki boş bekleme bölümü kesildi; açıklamalar uygulama çerçevesinin dışına eklendi.

### Uygulama geliştirmede giderilenler

- EF bağımlılığındaki eski SQLite native paketinin bilinen açığı; 2.1.13 bundle'a güncellendi, güvenlik denetimi kapatılmadı.
- MVC positional-record validation özniteliklerinin yanlış hedefinden kaynaklanan hata.
- Antiforgery'nin otomatik X-Frame-Options başlığının paylaşımları engellemesi; özel sayfalarda DENY, paylaşımda doğrulanmış CSP frame-ancestors.
- Docker build'de bulut proxy CA'sının eksik olması; güvenilir CA ile doğrulama geri sağlandı.
- Container'da root olmayan kullanıcının yayın dosyalarına erişimi; COPY sahipliği düzeltildi.
- Gerçek HTTPS proxy altında güvenli çerezler için yalnızca bilinen proxy'ye güvenen forwarded-header yapılandırması.
- Bulut proxy'sinin Docker içindeki `app` adresine trafiği dışarı yönlendirmesi; yalnızca yerel backend için `NO_PROXY` eklendi.

## Sağlayıcı bağımsız AI ve yönetim paneli — 3 Ekim 2026

- Güncel .NET sonucu: **62 geçti, 0 başarısız, 0 atlandı**. Adapter/factory yönlendirmesi, ElevenLabs zaman kodları, OpenRouter sohbet/embedding, Azure ve n8n sözleşmeleri kontrollü HTTP sağlayıcılarıyla doğrulandı.
- Birleşik iş hattı: gerçek FFmpeg ses işleme → kontrollü ElevenLabs transkripsiyonu → kontrollü OpenRouter konu/indeks → kaynaklı soru-cevap. Bu test canlı sağlayıcı doğrulaması değildir.
- Admin güvenliği: normal kullanıcıya 403, CSRF, veritabanından rol iptali, şifreli anahtar saklama, HTML/DTO/hata sayfasında anahtar döndürmeme, etkin anahtarı silme koruması, sürümlü seçim ve yeni scope'ta kalıcılık doğrulandı. Ortam anahtarlarının veritabanına kopyalanmadığı test edildi.
- İndeks model/endpoint/boyut değişiminde eski vektörlerin sorgulanmaması; sabit iş yapılandırması; hatalı vektör ve zamanların reddi; host kısıtlaması ve yönlendirmelerin izlenmemesi test edildi.
- Güncel Playwright sonucu: **3 geçti**. Mevcut video/SRT/sohbet/iframe akışına admin profil düzenleme ve sağlayıcı seçimi eklendi. Admin masaüstü/mobil ekranlarında otomatik axe erişilebilirlik kontrolü ve mobil taşma kontrolü geçti. Testler ayrı geçici veri dizinlerinde ve dış sağlayıcı anahtarları boşaltılarak çalıştı.
- Güncel Release publish başarılı. EF `has-pending-model-changes`: değişiklik yok. Uygulama dosyalarında `git diff --check` geçti; değiştirilmemiş upstream `vendor/agent-squad` dosyalarındaki mevcut whitespace uyarıları kapsam dışında tutuldu. Docker/HTTPS testleri bu sağlayıcı değişikliğinden sonra yeniden çalıştırılmadı; yukarıdaki sonuçlar önceki sürüme aittir.
- Admin ekranları: `artifacts/admin-providers-desktop.png`, `artifacts/admin-providers-mobile.png`, `artifacts/admin-provider-edit.png`; yalnızca sentetik hesap/test ayarları içerir.
- **Canlı bağlantı engeli:** Anahtarsız ağ kontrolünde OpenRouter HTTP 403, ElevenLabs CONNECT 403 döndürdü. Sohbette paylaşılan anahtarlar dış servise gönderilmedi ve kaynak/config/veritabanına alınmadı. Model erişimi, gerçek STT doğruluğu, ücretler ve üretken cevap kalitesi doğrulanmadı.
- `cloud-environment-onboarding:setup` ile mevcut ayarlar korunarak iki API host'u ve güvenli secret bağlama gereksinimleri bulut taslağına kaydedildi. Taslak **çalışan ağın açıldığı anlamına gelmez**. Ortam değişikliğinin uygulanması ve yenilenmiş anahtarların güvenli alanlara girilmesi gerekir; ardından [PROVIDERS.md](PROVIDERS.md) kabul testi uygulanmalıdır.

## Video bağlamı ve transkript kalite kontrolü — 3 Ekim 2026

- Güncel .NET sonucu: **75 geçti, 0 başarısız, 0 atlandı**. Terim/yönerge sınırları, kural tabanlı uyarılar, onay zorunluluğu, sabit zaman kodları, ham metin ve 10 sürümlük geçmiş, hedefli düzeltmeden sonra diğer bölümlerin uyarılarını koruma, eski vektör/konu iptali, geri yükleme ve sürüm çakışması test edildi.
- Eski veritabanı şeması üzerinde sentetik mevcut transcript oluşturulup yeni migration uygulandı: özgün metin korundu, JSON koleksiyonları geçerli başlangıç değerleriyle okundu. EF `has-pending-model-changes`: değişiklik yok.
- Gerçek FFmpeg ile hedefli ses penceresi çıkarıldı; kontrollü STT adaptörüne gönderildi, yalnızca hedefle örtüşen sonuç öneriye alındı ve geçici ses silindi. Bu test insan konuşması doğruluğunu ölçmez.
- Kontrollü LLM ile konu/sohbet/kalite incelemesinde sözlük ve yönergelerin ayrı yardımcı veri alanında gönderildiği, bozuk öneri cevabının reddedildiği doğrulandı. Öneri otomatik uygulanmadı. AI çağrısı sürerken genel yazma kilidinin serbest olduğu ve geç gelen eski önerinin reddedildiği test edildi.
- Gerçek API testinde başka kullanıcıya 404, eksik CSRF'ye 400, onaysız düzeltmeye 400 ve eski sürümle kayda 409 doğrulandı. Iframe API'si ham geçmişi ve özel bağlam alanlarını döndürmedi; paylaşım arayüzünde düzenleme araçları yoktu. Şüpheli kaynak içeren sohbetin uyarı ve metin sürümü taşıdığı doğrulandı.
- Güncel Playwright sonucu: **4 geçti**. Yeni senaryo gerçek UI'da yükleme sırasında sözlük ekleme, sonradan yönerge değiştirme, uyarı filtreleme, gerçek medya sesini oynatma, elle düzeltmeyi onaylama, güncel SRT ve ham metne geri dönüşü kapsıyor. Masaüstü ve 390 px mobil otomatik axe kontrolleri geçti; yeni akışta JavaScript hatası yakalanmadı.
- Kalite tarayıcı senaryosu ayrıca yeniden çalıştırıldı (**1 geçti**): gerçek sunucu sohbet cevabı ağda bekletilirken transkript değiştirildi; geç gelen eski sürüm cevabı ve zaman kodları gösterilmedi, yeniden soru sorma uyarısı doğrulandı.
- Release publish başarılı; Docker/HTTPS kontrolleri bu genişletmede yeniden çalıştırılmadı. Uygulama kapsamındaki whitespace kontrolü geçti.
- Gerçek ekranlar: `artifacts/video-context-desktop.png`, `artifacts/video-context-mobile.png`, `artifacts/transcript-review-desktop.png`, `artifacts/transcript-review-mobile.png`. Veriler sentetik; sağlayıcılar tarayıcı testinde kapalıdır.
- **Canlı AI doğruluğu iddiası yoktur.** OCR, sessizlik/VAD, gerçek STT güven puanları, otomatik ikinci sağlayıcı uzlaşması ve forced alignment bu sürümde uygulanmadı. Kapsam ve kullanım: [TRANSCRIPT-QUALITY.md](TRANSCRIPT-QUALITY.md).

## İşleme, kalite, kaynaklar ve işletim genişlemesi — 3 Ekim 2026

- Güncel .NET tam test sonucu: **92 geçti, 0 başarısız, 0 atlandı**. Yeni testler: 490 saniyelik gerçek sentetik medya üzerinde ikinci STT parçası hatasından sonra ilk parçayı tekrar göndermeden devam; kalite grubu hatasında kaydedilmiş 40. konuşmadan devam; gerçek FFmpeg sessizlik tespiti; çalışan sağlayıcı isteğinin iptali; açık saklama süresi dolan kaydın temizlenmesi.
- Kullanım rezervasyonu/çağrı limiti, sağlayıcı ücretinin ayrı tutulması, bilinmeyen tarife, belirsiz sonuç ve video silindikten sonra günlük kota kaydının korunması test edildi. Private-share anonim/izinsiz erişim, özel sohbet CSRF, görünürlük/altyazı/sohbet sunucu kontrolleri ve saklama onayı doğrulandı.
- İddia başına geçerli kaynak doğrulaması, boş/hatalı iddialarda kapalı davranış, uzun özette son zaman bölgelerinin kapsanması ve seçili bölüm kanıt sınırı test edildi. Eski şemadan geçişte mevcut metin, paylaşım varsayılanları ve JSON koleksiyonları korundu.
- Kontrollü adaptör testleri: ElevenLabs v1'e keyterms gönderilmemesi, v2'ye gönderilmesi; Whisper sözlüğü; hizalama zaman kontrolü/offset; ikinci STT'nin ana rotadan ayrı seçilmesi. **Bunlar canlı sağlayıcı doğrulaması değildir.**
- Playwright: **5 test geçti**. Kullanım/saklama, açık onaylı zaman düzeltmesi, minimal iframe başlangıç zamanı/gizli metin/kapalı sohbet, özel paylaşım seçimi ve yönetici kullanım ekranı eklendi. Önceki sahiplik, geri yükleme, eski cevap sürümü, mobil ve erişilebilirlik senaryoları geçmeye devam etti. Yeni paylaşım formunda axe kontrolü geçti.
- Yerel doğruluk ölçüm aracının **2 Node testi geçti**: Türkçe WER ve isteğe bağlı, elle eşleştirilmiş sınır sapması. Gerçek insan konuşmasına ait WER sonucu üretilmedi.
- Release publish başarılı; EF model/migration farkı yok. Uygulama whitespace kontrolü geçti. Güncel Docker image ve HTTPS smoke bu genişletmede yeniden çalıştırılmadı; önceki container sonuçları yeni sürüm için yeniden yapılmış sayılmaz.
- Gerçek UI görüntüleri: `artifacts/operations-desktop.png`, `artifacts/admin-operations.png`, `artifacts/embed-minimal.png`, `artifacts/share-options.png`. İçerik ve hesaplar sentetiktir.
- Önceki bölümde “yok” diye belirtilen sessizlik taraması, tüm metin AI taraması, kullanıcı başlatmalı ikinci STT ve ElevenLabs hizalama adaptörü bu genişletmede eklendi. OCR, öğrenilmiş VAD, otomatik doğruluk uzlaşması, ayrı worker/nesne depolama ve canlı sağlayıcı kabulü halen kapsam dışı/bekliyor. [Kapsam ve sınırlar](IMPROVEMENTS.md), [gerçek kabul planı](ACCEPTANCE.md).

## Ses dosyası analizi — 10 Ekim 2026

- MP3, WAV, M4A/AAC, FLAC, OGG/Opus, AIFF ve WMA/ASF ses kapsayıcıları gerçek FFprobe akış doğrulamasıyla yükleme hattına eklendi; uzantı veya istemci MIME bilgisine güvenilmiyor.
- Ses kaynakları FFmpeg ile tarayıcıda aralık istekleriyle oynatılabilen AAC/MP4 medyaya dönüştürülüyor, ses kaydı olarak işaretleniyor ve video ile aynı STT, konuşmacı, kalite, konu, indeks ve sohbet hattına giriyor.
- Gerçek sentetik MP3 kullanılan odaklı web testi geçti: yükleme, dönüştürme, `audio` medya türü, poster üretimi, oynatma varlığı ve transkript bekleme durumu doğrulandı (**1 geçti, 0 başarısız**). Test sınıfının bilinen geçici dosya temizleme uyarısı test sonucunu başarısız yapmadı.
- Debug derlemesi uyarısız/hatasız tamamlandı. `AddAudioMediaKind` migrasyonu mevcut yerel veritabanına uygulandı; önceki 5 kayıt `video` olarak korundu. Yerel HTTPS hazır olma kontrolü HTTP 200 döndürdü.
- Bu test gerçek insan konuşması doğruluğunu veya canlı STT sağlayıcı kalitesini ölçmez; 2 saat/512 MiB sınırında yük testi yeniden yapılmadı.

## Yerel ses analiz MCP sunucusu — 10 Ekim 2026

- Resmi C# MCP SDK 2.2.0 ile stdio sunucusu eklendi. Gerçek initialize ve `tools/list` istekleri sunucu tarafından işlendi; altı ses aracı keşfedilebilir durumda.
- Çözüm derlemesi **0 uyarı, 0 hata** ile tamamlandı. MCP odaklı yol allowlist'i ve bağlamsal konu ayrımı testleri **2 geçti, 0 başarısız**.
- Framework-dependent Release publish `artifacts/mcp-smoke` dizinine başarıyla üretildi. Aynı çıktı .NET 10 runtime bulunan Windows ve Linux üzerinde çalışır.
- Yerel makinede FFmpeg, Whisper, Python, Torch, Resemblyzer, librosa ve scikit-learn import/varlık kontrolü geçti. Bu doğrulama gerçek insan konuşmasında WER veya konuşmacı doğruluğu ölçümü değildir.
- Tam çözüm test çalıştırması denendi; MCP testleri geçti ancak mevcut web testlerinin paralel SQLite/`instance.lock` temizleme çakışmaları ve iki mevcut HTTP durum beklentisi nedeniyle tüm paket yeşil raporlanmadı. Bu hatalar MCP odaklı testlerde oluşmadı.

## Otomatik konuşmacı ve konuşma bağlamı incelemesi — 10 Ekim 2026

- Debug çözüm derlemesi **0 uyarı, 0 hata**; Web ve MCP Release publish işlemleri başarılı tamamlandı. `player.js` Node sözdizimi ve yerel konuşmacı Python modülü `py_compile` kontrolünden geçti.
- Otomatik konuşmacı sayısı doğrulaması, konuşma bağlamı profilinin hedefli AI incelemesine aktarılması, yetersiz kanıtın reddi, eski bağlam veri sözleşmesi, hatalı AI yanıtının kapalı davranması ve MCP kontrolleri için odaklı .NET testleri **9 geçti, 0 başarısız**.
- Yerel Python smoke testinde sentetik iki kümeli özellik matrisi iki konuşmacı olarak seçildi ve kümelendi. Bu kontrol gerçek insan sesi diarization doğruluğu veya konuşmacı kimliği ölçümü değildir.
- Tam .NET paketinde **85 test geçti**. Kalan 21 sonuçtan 18'i mevcut WebApplicationFactory SQLite/`instance.lock` temizleme yarışı, üçü önceden kayıtlı HTTP durum/iş kuyruğu beklentileridir; yeni odaklı testlerde hata oluşmadı.
- AI bağlam profili yalnızca transkript içindeki tekrarlar ve kullanıcı sözlüğünden kanıt üretir; sesi dinlediğini veya dış dünyadaki yazımın doğru olduğunu iddia etmez. Öneriler kullanıcı ses doğrulaması olmadan metne uygulanmaz. Otomatik konuşmacı sayısı en fazla sekiz kümeyi değerlendirir; benzer/örtüşen seslerde sabit kişi sayısı kullanılmalıdır.

## Kullanıcıya bağlı üçüncü taraf analiz API'si — 23 Ekim 2026

- Odaklı API güvenlik/entegrasyon testleri **2 geçti, 0 başarısız**: tek sefer gösterilen ve yalnızca özeti saklanan kimlik bilgisi, Bearer doğrulaması, kullanıcı sahipliği, kapsam reddi, API üzerinden analiz oluşturma, API kaynağının kullanıcı kaydında korunması ve denetim logu doğrulandı.
- Tam .NET paketi seri çalıştırmada **105 geçti, 4 başarısız, 0 atlandı**. Kalanlar daha önce belgelenen iş kuyruğu/zamanlama beklentilerinde: iki HTTP `BadRequest`/`Conflict`, SRT içe aktarma sonrasında `Ready`/`Queued` ve parça devam testinde çağrı sayısı. Yeni `ExternalApiTests` içinde hata yoktur; tam paket yeşil sayılmamıştır.
- Release publish başarıyla üretildi. EF `has-pending-model-changes` kontrolü model ile `ExternalAnalysisApi` migrasyonunun eşleştiğini doğruladı; `git diff --check` içerik hatası döndürmedi.
- API anahtarının ham değeri, Authorization başlığı, yüklenen medya, transkript ve istek gövdeleri denetim kaydına alınmaz. Loglarda güvenli operasyon metadatası ve geri döndürülemez uzak adres özeti tutulur; 90 günlük otomatik temizlik uygulanır.
- Canlı üçüncü taraf istemci, uzun medya, yük testi ve tarayıcı uçtan uca ekran testi bu doğrulamada çalıştırılmadı.

## Docker dağıtımı — 25 Ekim 2026

- Tam `final` imajı gerçek Docker Desktop Linux motorunda başarıyla oluşturuldu. Release publish, FFmpeg, SQLite araçları, CPU Torch, Local Whisper, Librosa, scikit-learn ve Resemblyzer aynı imajda yer alıyor.
- Geçici ve izole `videoatlas-docker-smoke` projesi `127.0.0.1:5088` üzerinde çalıştırıldı. Konteyner healthcheck'i `healthy`, `/health/ready` yanıtı HTTP 200 ve gövdesi `{"status":"ready"}` oldu.
- Konteyner içinde Whisper/Torch/Librosa/scikit-learn/Resemblyzer import testi, Whisper CLI yürütülebilirlik kontrolü ve FFmpeg sürüm kontrolü geçti. Uygulama root yerine `app` (UID 1654) kullanıcısıyla çalıştı.
- Üretim, kök yerel ve `deploy/local` Compose dosyalarının `docker compose config` doğrulaması geçti. Gerçek domain, Caddy ACME sertifikası, GPU hızlandırma ve uzun ses yükü bu smoke testin kapsamında değildi.

## Güncel dış doğrulama gereksinimleri

- **Canlı OpenAI:** kullanılabilir `Ai__ApiKey` yoktu. Gerçek konuşma tanıma doğruluğu, gerçek sağlayıcı model/hesap erişimi, ücretler ve cevapların anlamsal doğruluğu canlı hesapla doğrulanmadı. Kontrollü testleri canlı AI testi olarak değerlendirmeyin.
- **İnternet yayını:** gerçek alan adı, sunucu erişimi ve DNS verilmedi; kamuya açık servis veya ACME sertifikası yayımlanmadı. Yerel HTTPS testi kamu yayını değildir.
- 2 saat/512 MiB sınırında yük ve uzun süreli dayanıklılık/kapasite testi yapılmadı. İlk yayın küçük, davetli kullanıcı kitlesi için tek sunucudur.
- Agent Squad'ın Opengrep, CodeQL ve Stryker motorları çalıştırılmadı. Tam 5-motor/0-mutant sertifikasyonu, mevzuat veya bağımsız güvenlik denetimi iddiası yoktur.
- Gerçek DNS/TLS, seçilen sağlayıcıların güvenli anahtar bağlaması/ağ erişimi, işletmeci gizlilik bilgileri ve gerçek video ile kabul testi tamamlanmadan “tüm üretim doğrulaması bitti” sayılmaz. Adımlar `docs/OPERATIONS.md` ve `docs/PROVIDERS.md` içinde.

Bulut yapılandırma taslağına `install_script`, `start_skill`, `Ai__ApiKey` için güvenli secret gereksinimi ve gerekli ağ alanları kaydedildi. Taslak kaydı ortamın yayımlandığını veya yeni görevde snapshot restore testinin yapıldığını göstermez.
