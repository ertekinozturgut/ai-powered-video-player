# Üretime alma ve işletim

## AI sağlayıcıları ve yönetim

Güncel kurulum ve anahtar yönetimi için [PROVIDERS.md](PROVIDERS.md) esas alınmalıdır. STT için ElevenLabs, LLM/embedding için OpenRouter önerilir; bu alanlar bağımsız seçilir. `ProviderCredentials__OpenRouter` ve `ProviderCredentials__ElevenLabs` güvenli bağlamalarını veya admin panelindeki şifreli kayıt yolunu kullanın. Aşağıdaki OpenAI model/anahtar açıklamaları eski uyumlu OpenAI profiline aittir; yeni kurulum için zorunlu değildir. Admin rolü `--grant-admin` / `--revoke-admin` operator komutlarıyla yönetilir.

## Dağıtım modeli

İlk sürüm Linux üzerinde **tek uygulama örneği**, SQLite, FFmpeg ve kalıcı disk kullanır. En az 2 vCPU, 4 GB RAM ve başlangıç için 30 GB boş disk önerilir. Uygulama verisi `/data`, SQLite `videoatlas.db`, medya `videos/`, oturum koruma anahtarları `keys/` altındadır. Aynı data dizinini ikinci süreçle açmak kilit tarafından reddedilir. NFS veya birden fazla replica kullanmayın.

Docker Compose, Caddy ile HTTPS sertifikasını otomatik alır. DNS A/AAAA kayıtları sunucuya yönelmeli, TCP 80/443 ve isteğe bağlı UDP 443 açık olmalıdır. Uygulamanın 8080 portu doğrudan internete açılmaz. Compose iç ağı `172.30.0.0/24`, Caddy'nin sabit adresi `172.30.0.3` olarak tanımlıdır. Bu subnet sunucunuzda kullanımda ise network subnet, proxy IP ve `ReverseProxy__KnownAddress` değerlerini birlikte değiştirin. Yalnızca tanımlı proxy ve loopback üzerinden gelen HTTPS/IP başlıklarına güvenilir; tüm proxy'lere açık güven yoktur. AI çıkışı seçili sağlayıcıya göre `openrouter.ai:443`, `api.elevenlabs.io:443` veya eski OpenAI için `api.openai.com:443`; özel Azure/n8n hostları hem bulut ağına hem uygulamanın ProviderSecurity allowlist alanına eklenmelidir; image indirmek için `mcr.microsoft.com`, `*.data.mcr.microsoft.com` ve Docker Hub/registry CDN alanları gerekir.

## İlk kurulum

1. `.env.example` dosyasından `.env` oluşturun. `DOMAIN`, `ACME_EMAIL`, `AI_API_KEY` alanlarını gerçek değerlerle güvenli biçimde doldurun. `.env` Git tarafından yok sayılır. `chmod 600 .env` uygulayın.
2. `docker compose config --quiet` ve `docker compose up -d --build` çalıştırın.
3. `docker compose ps` ile uygulamanın healthy olduğunu, dış domain üzerinde `/health/ready` ve giriş formunun HTTPS ile açıldığını kontrol edin.
4. Kullanıcı oluşturun. Parolayı komut satırına yazmak yerine aşağıdaki gibi gizli okuyun:

```bash
read -r -p 'Kullanıcı e-postası: ' BOOTSTRAP_EMAIL
read -r -s -p 'İlk parola: ' BOOTSTRAP_PASSWORD
export BOOTSTRAP_EMAIL BOOTSTRAP_PASSWORD
docker compose exec -e BOOTSTRAP_EMAIL -e BOOTSTRAP_PASSWORD app dotnet VideoAtlas.Web.dll --create-user
unset BOOTSTRAP_EMAIL BOOTSTRAP_PASSWORD
```

İlk parola en az 12 karakter, büyük/küçük harf, rakam ve sembol içerir. Her kullanıcı için komutu tekrarlayın. `--reset-password` aynı güvenli giriş yöntemiyle parolayı sıfırlar ve Identity güvenlik damgasını değiştirir; mevcut oturumlar sonraki güvenlik damgası kontrolünde geçersizleşir. E-posta ile self-service parola sıfırlama bu sürümde yoktur. Üretimde self-service kayıt kapalıdır. Development ortamını internete açmayın.

CLI işlemleri yalnızca güvenilen sunucu yöneticisi içindir; internete açık yönetici oluşturma ucu yoktur. SQLite migration'ları başlangıçta uygulanır. Şema değişikliğinden önce yedek alın; üretimde `EnsureCreated`, `EnsureDeleted` veya elle tablo silme kullanmayın.

## İlk gerçek AI kabul testi

Yeni ElevenLabs/OpenRouter kurulumu için aynı kontrol listesini gerçek sağlayıcılarla çalıştırın. Mevcut bulut proxy 403 kısıtı kaldırılmadan bu adım tamamlanmış sayılmaz.

Kontrollü test sağlayıcısının geçmesi canlı OpenAI hesabını/model erişimini doğrulamaz. Yayın öncesinde size ait, kısa ve hassas bilgi içermeyen bir konuşma videosuyla şu kontrolleri yapın:

- Yükleme → işleme → **Hazır** durumuna kendiliğinden geçiş.
- SRT indirme; videonun başı, ortası ve sonundaki zaman kodlarını dinleyerek karşılaştırma.
- Konuların gerçekten içerikle eşleşmesi; anlamsal arama indeksinin hazırlanması.
- Videoda bilinen bir ayrıntıya soru; ilgili konuşmanın kaynak düğmesiyle bulunması.
- Videoda bulunmayan bir soru; yetersiz kanıt yanıtı.
- Yetkisiz ayrı hesapla özel videoya erişilememe.
- Gerçek ikinci HTTPS sitede iframe; çerez engellenmiş durumda oynatma/sohbet; bağlantı iptalinden sonra yeni isteklerin reddi.

Whisper ses parçaları 8 dakika, mono MP3 olarak hazırlanır. `whisper-1` zaman kodlu verbose JSON verir. Gruplama ve sohbet `gpt-4o-mini`, anlamsal indeks `text-embedding-3-small` (512 boyut) kullanır. Sunucu API anahtarını tarayıcıya göndermez. İndeks transcript'in tamamından oluşturulur; uzun videolarda yanıt bağlamı ilgili parçalar ve konu açılışlarıyla sınırlandırılır. Model değiştirirseniz videoları yeniden analiz edin.

## Limitler ve hata davranışı

Yeni işleme kontrol noktaları, tahmini bütçe sınırları, 30 günlük içeriksiz kullanım kaydı ve isteğe bağlı video saklama politikası için [IMPROVEMENTS.md](IMPROVEMENTS.md) esas alınmalıdır. Gerçek doğruluk değerlendirmesi [ACCEPTANCE.md](ACCEPTANCE.md) ile yapılır. İşlem/ücret özeti yönetici için `/admin/operations` ekranındadır.

- En fazla 512 MiB yükleme, 2 saat süre; kullanıcı başına 5 GiB **kaynak yükleme toplamı** kotası. Dönüşmüş medya boyutu farklı olabilir; gerçek disk kapasitesini ayrıca izleyin.
- En fazla 16,777,216 giriş pikseli; çıkış en fazla 1280×720 sınırlarına sığdırılır. Tarayıcı için H.264/AAC ve MP4 faststart. Çıkış 1 GiB aşarsa sessiz kesme yerine hata verir.
- 2 eşzamanlı yükleme; işçi tek video işler. FFmpeg 2 thread ve işlem başına 45 dakika zaman sınırı kullanır. Video kuyruğu veritabanında kalıcıdır.
- AI çağrıları 1–3 dakika timeout ile sınırlıdır. Ücretli POST çağrıları otomatik tekrarlanmaz; yanıt kaybında çift ücret riskinden dolayı kullanıcı kontrollü tekrar vardır. Tamamlanan transkripsiyon, konu/indeks hatasından önce kaydedilir ve tekrar kullanılır. İşlem ortasında süreç kaybı son parçayı yeniden ücretlendirebilir.
- Giriş 12 istek/dakika/IP; 5 hatalı parola sonrası 15 dakika hesap kilidi. Sohbet 20 istek/dakika/hesap veya IP; özel ve paylaşılan erişim toplamı video başına 200 soru/UTC günü.
- Caddy'nin sabit IP'si üzerinden iletilen istemci IP'si hız sınırlamada kullanılır. Farklı bir proxy kullanırsanız `ReverseProxy__KnownAddress` değerini onun gerçek iç adresine göre ayarlayın; tüm proxy'lere güvenmeyin. Ölçek büyütmeden önce dağıtık hız sınırlama gerekir.
- Başlangıçta `Processing` kalan işler `Queued` yapılır. `Failed` videolar kullanıcı “Yeniden analiz et” eylemine kadar tekrar edilmez. Ses bulunmayan video SRT bekler.
- AI yapılandırması yoksa SRT içe aktarma, yerel anahtar kelime gruplama ve açıkça etiketli alıntı araması çalışır. “SRT bekliyor” durumunu otomatik transkripsiyon başarısı saymayın.

## Yedekleme ve kurtarma

Transkript kalite sürümünde video kaydı ilk ham metni, son 10 düzenleme sürümünü, terim sözlüğünü ve analiz yönergelerini de içerir. Yükseltmeden önce tam veri yedeği alın; migration mevcut metni başlangıç ham kaydı olarak korur. Metin düzeltmek eski metni geçmişten silmez; kişisel veri/saklama politikası bu geçmişi ve yedekleri kapsamalıdır. Kullanım ve sınırlar: [TRANSCRIPT-QUALITY.md](TRANSCRIPT-QUALITY.md).

SQLite, medya ve `keys/` birlikte yedeklenmelidir. WAL açıkken yalnızca `.db` dosyasını kopyalamayın. Başlangıç için kısa bakım penceresinde tutarlı tam yedek alın:

```bash
umask 077
mkdir -p backups
docker compose stop app
docker compose run --rm --no-deps --entrypoint tar app -C /data -czf - . > "backups/videoatlas-$(date -u +%Y%m%dT%H%M%SZ).tgz"
docker compose start app
```

Arşivi doğrulayın (`tar -tzf ...`), erişimi kısıtlayın ve şifrelenmiş uzak yedek alanına aktarın. Arşiv medya ve oturum anahtarlarını içerir. Saklama süresini kurumunuz belirlemeli; silinen kullanıcı içeriği eski yedeklerde süre bitene kadar bulunabilir. Yedekleri düzenli olarak ayrı, boş bir data volume üzerinde geri yükleyip giriş, medya, metin ve paylaşım iptali kontrollerini yapın. Mevcut veri üstüne doğrudan geri yüklemeyin. Data Protection anahtar dosyaları uygulama tarafından disk üzerinde ayrıca şifrelenmez; disk/volume ve yedek şifrelemesi işletim katmanının sorumluluğudur.

## İzleme, güncelleme ve gizlilik

`/health/live` süreç, `/health/ready` veritabanı erişimi kontrolüdür; gerçek AI erişimini veya alan adının TLS durumunu ölçmez. FFmpeg ve AI kullanılabilirliğini sentetik kabul akışıyla ayrıca izleyin. Loglar yalnızca hata kodu, anonim video ID'si ve trace ID içerir; transcript/parola/key ve paylaşım token'larını kaydetmeyin. Caddy access log'u token sızıntısını önlemek için kapalıdır. Disk doluluğu, Failed iş sayısı, geciken Queued işler ve sağlayıcı maliyet alarmı kurun.

Güncelleme: testleri ve image build'i çalıştırın; yedek alın; `docker compose up -d --build`; migration loglarını, health ve gerçek kullanıcı akışını kontrol edin. SQLite şemasında geri dönüş, doğrulanmış yedek + önceki image ile planlanmalıdır. Gelecekte PostgreSQL/nesne depolama/ayrı worker geçişi için uygulama sözleşmeleri ayrılmıştır; mevcut sürüm çoklu replica değildir.

Yayın öncesinde `/privacy` metnine veri sorumlusunun gerçek kimlik/iletişim bilgileri, hukuki dayanak ve somut saklama süreleri eklenmelidir. Video/ses/metin kişisel veri içerebilir. Katılımcı bilgilendirmesi, işleme/aktarım yetkisi ve OpenAI sözleşme koşulları işletmeci tarafından tamamlanmalıdır. Bu yazılım tek başına mevzuat sertifikasyonu sağlamaz.

## Docker erişimi yoksa

Bu bulut ortamında image build HTTPS proxy üzerinden çalışır. Kurumsal CA image içinde yoksa, sertifika doğrulamasını kapatmayın. Güvenilen ortam sertifikasını sadece build aşamasına verin:

```bash
docker build --secret id=proxy_ca,src=/usr/local/share/ca-certificates/environment-proxy-ca.crt -t videoatlas:local .
```

Bu isteğe bağlı build secret normal internete doğrudan erişen dağıtımlarda gerekmez. Runtime image'a kurumun özel proxy CA'sı gömülmez. Üretim de özel TLS proxy kullanıyorsa runtime güven deposuna kuruluşun CA'sı ayrıca güvenli biçimde kurulmalıdır.

`dotnet publish src/VideoAtlas.Web -c Release -o artifacts/publish` ile derlenen uygulama FFmpeg kurulu Linux'ta doğrudan çalışır. `Storage__Root`, `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS`, `Ai__ApiKey` ve `AllowedHosts` değerlerini servis yöneticisinde ayarlayın. Kalıcı data dizinine servis kullanıcısının erişimini verin ve HTTPS ters proxy arkasında çalıştırın. İkili yayın paketi üretmek, DNS/TLS/AI canlı kabulünün yerine geçmez.
