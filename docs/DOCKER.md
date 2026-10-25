# VideoAtlas Docker kurulumu

Bu kılavuz projeyi Windows veya Linux üzerinde Docker Compose ile çalıştırır. Varsayılan imaj şunları birlikte içerir:

- ASP.NET Core 10 web uygulaması ve arka plan işçisi
- SQLite ve kalıcı medya depolaması
- FFmpeg / FFprobe
- Local Whisper STT
- Python tabanlı konuşmacı ayrımı
- Caddy ile isteğe bağlı otomatik HTTPS

Uygulama **tek replica** olarak çalışır. SQLite, medya, AI ayarları ve Data Protection anahtarları aynı kalıcı volume içinde tutulur. Aynı volume'u iki uygulama container'ına bağlamayın.

## 1. Gereksinimler

- Docker Desktop 4+ (Windows/macOS) veya Docker Engine + Compose Plugin (Linux)
- Local Whisper için en az 4 CPU, 8 GB RAM ve model/veriler için yeterli disk
- Üretim HTTPS kurulumu için sunucuya yönlendirilmiş bir alan adı ve açık TCP 80/443

İlk tam imaj derlemesi Python ve CPU Torch paketlerini indirdiğinden uzun sürebilir. Whisper modeli imaja gömülmez; ilk analizde indirilir ve `video_data`/`local_data` volume'undaki `/data/home` altında kalıcı olarak önbelleğe alınır.

## 2. Yerel kurulum — Windows PowerShell

Proje kökünde:

```powershell
Copy-Item .env.example .env
docker compose -f compose.local.yaml config
docker compose -f compose.local.yaml up -d --build
docker compose -f compose.local.yaml ps
Invoke-RestMethod http://127.0.0.1:5080/health/ready
```

Tarayıcıdan `http://127.0.0.1:5080` adresini açın ve geliştirme hesabınızı oluşturun.

Hesabı yönetici yapmak için:

```powershell
$env:BOOTSTRAP_EMAIL = "admin@example.com"
docker compose -f compose.local.yaml exec -e BOOTSTRAP_EMAIL app dotnet VideoAtlas.Web.dll --grant-admin
Remove-Item Env:BOOTSTRAP_EMAIL
```

Local Whisper'ı etkin STT sağlayıcısı yapmak için:

```powershell
docker compose -f compose.local.yaml exec app dotnet VideoAtlas.Web.dll --configure-local-whisper
docker compose -f compose.local.yaml restart app
```

Ardından tekrar giriş yapın. LLM ve embedding sağlayıcılarını `/admin/providers` ekranından ayrı seçebilirsiniz.

## 3. Yerel kurulum — Linux/macOS

```bash
cp .env.example .env
docker compose -f compose.local.yaml config
docker compose -f compose.local.yaml up -d --build
docker compose -f compose.local.yaml ps
curl -fsS http://127.0.0.1:5080/health/ready
```

Hesabı oluşturmak için `http://127.0.0.1:5080` adresindeki kayıt ekranını kullanın. Sonra yönetici rolü ve Local Whisper seçimi:

```bash
export BOOTSTRAP_EMAIL='admin@example.com'
docker compose -f compose.local.yaml exec -e BOOTSTRAP_EMAIL app dotnet VideoAtlas.Web.dll --grant-admin
unset BOOTSTRAP_EMAIL

docker compose -f compose.local.yaml exec app dotnet VideoAtlas.Web.dll --configure-local-whisper
docker compose -f compose.local.yaml restart app
```

## 4. Daha küçük, dış sağlayıcı imajı

Local Whisper kullanmayacak; ElevenLabs, OpenAI, Azure veya n8n üzerinden STT yapacaksanız `.env` içinde şu hedefi seçebilirsiniz:

```dotenv
VIDEOATLAS_DOCKER_TARGET=runtime-lite
```

Sonra imajı yeniden oluşturun:

```bash
docker compose -f compose.local.yaml up -d --build
```

Bu imaj Python/Torch/Whisper içermez. Admin panelinde Local Whisper seçilirse transkripsiyon çalışmaz.

## 5. Üretim kurulumu — domain ve HTTPS

`.env.example` dosyasını `.env` olarak kopyalayın ve en az aşağıdaki değerleri değiştirin:

```dotenv
DOMAIN=video.example.com
ACME_EMAIL=operations@example.com
VIDEOATLAS_DOCKER_TARGET=final
VIDEOATLAS_MEMORY_LIMIT=8g
VIDEOATLAS_CPU_LIMIT=4
OPENROUTER_API_KEY=
ELEVENLABS_API_KEY=
```

Anahtarları `.env` dosyasına yazmak istemiyorsanız boş bırakıp yönetici panelinden şifreli olarak kaydedebilirsiniz. `.env` Git tarafından dışlanır. Linux'ta erişimini kısıtlayın:

```bash
chmod 600 .env
docker compose config --quiet
docker compose up -d --build
docker compose ps
curl -fsS "https://${DOMAIN}/health/ready"
```

Caddy TLS sertifikasını otomatik alır ve yalnızca uygulamanın 8080 portuna iç ağdan erişir. DNS A/AAAA kayıtları sunucuya yönelmeli; güvenlik duvarında TCP 80/443 ve istenirse HTTP/3 için UDP 443 açık olmalıdır.

Üretimde kayıt kapalıdır. İlk kullanıcıyı güvenli ortam değişkenleriyle oluşturun:

```bash
read -r -p 'Kullanıcı e-postası: ' BOOTSTRAP_EMAIL
read -r -s -p 'İlk parola: ' BOOTSTRAP_PASSWORD; echo
export BOOTSTRAP_EMAIL BOOTSTRAP_PASSWORD
docker compose exec -e BOOTSTRAP_EMAIL -e BOOTSTRAP_PASSWORD app dotnet VideoAtlas.Web.dll --create-user
docker compose exec -e BOOTSTRAP_EMAIL app dotnet VideoAtlas.Web.dll --grant-admin
unset BOOTSTRAP_EMAIL BOOTSTRAP_PASSWORD
```

Parola en az 12 karakter; büyük/küçük harf, rakam ve sembol içermelidir.

## 6. AI sağlayıcılarının seçimi

### Tamamen yerel STT

```bash
docker compose exec app dotnet VideoAtlas.Web.dll --configure-local-whisper
docker compose restart app
```

Bu seçim yalnızca STT rotasını değiştirir. Konu çıkarımı, bağlamsal düzeltme, embedding ve video sohbeti için `/admin/providers` ekranından Ollama, OpenRouter, OpenAI, Azure veya n8n seçilebilir.

### ElevenLabs + OpenRouter

`.env` içindeki `ELEVENLABS_API_KEY` ve `OPENROUTER_API_KEY` değerlerini doldurup:

```bash
docker compose up -d --force-recreate app
docker compose exec app dotnet VideoAtlas.Web.dll --configure-providers
docker compose restart app
```

Alternatif olarak anahtarları yalnızca `/admin/providers` panelinden girin. Anahtarları Git'e, Dockerfile'a veya Compose dosyasına sabit yazmayın.

## 7. Kontrol ve sorun giderme

```bash
docker compose ps
docker compose logs --tail=200 app
docker compose logs --tail=100 proxy
docker compose exec app ffmpeg -version
docker compose exec app /opt/videoatlas-whisper/bin/whisper --help
docker compose exec app /opt/videoatlas-whisper/bin/python -c "import whisper, torch, librosa, sklearn, resemblyzer; print('local AI ready')"
```

Yerel compose kullanıyorsanız tüm komutlara `-f compose.local.yaml` ekleyin.

Yaygın durumlar:

- `whisper_missing`: tam imaj yerine `runtime-lite` oluşturulmuş olabilir.
- Container `137` koduyla kapanıyorsa bellek sınırı yetersizdir; `.env` içindeki `VIDEOATLAS_MEMORY_LIMIT` değerini yükseltin veya daha küçük Whisper modeli seçin.
- İlk transkripsiyon uzun sürüyorsa model indiriliyor olabilir; `docker compose logs -f app` ile takip edin.
- `instance.lock` hatası aynı `/data` volume'unu birden fazla uygulama sürecinin kullandığını gösterir.
- HTTPS sertifikası alınamıyorsa DNS, 80/443 portları ve Caddy loglarını kontrol edin.

Kurumsal antivirüs/proxy HTTPS trafiğini kendi CA sertifikasıyla tarıyorsa TLS doğrulamasını kapatmayın. CA'nın yalnızca açık sertifikasını PEM/CRT olarak dışa aktarın ve build secret kullanın:

```bash
docker build --secret id=proxy_ca,src=/secure/path/company-proxy-ca.crt --target final -t videoatlas:local-test .
docker compose -f compose.local.yaml up -d --no-build
```

Bu yol private key istemez. Sertifika dosyasını repoya veya `.env` içine koymayın.

## 8. Güncelleme, durdurma ve veri

Uygulamayı güncelleme:

```bash
git pull
docker compose build --pull
docker compose up -d
docker compose ps
```

Durdurma ve yeniden başlatma:

```bash
docker compose stop
docker compose start
```

Container'ları kaldırmak veriyi silmez:

```bash
docker compose down
```

`docker compose down -v` kalıcı video, veritabanı, oturum anahtarı ve model önbelleği volume'larını da siler. Yedeğiniz yoksa bu komutu kullanmayın. Yedekleme ve geri yükleme adımları için [OPERATIONS.md](OPERATIONS.md) belgesini izleyin.

## 9. Doğrulama listesi

Kurulum sonrasında:

1. `/health/ready` HTTP 200 dönüyor.
2. Kullanıcı giriş yapabiliyor ve yönetici `/admin/providers` ekranını açabiliyor.
3. Kısa, hassas olmayan bir ses dosyası yüklenebiliyor.
4. İş `Queued → Processing → Ready` akışını tamamlıyor.
5. Transkript, SRT ve konu başlıkları oluşuyor.
6. Konuşmacı ayrımı etkinse konuşmacı etiketleri geliyor.
7. Container yeniden başlatıldığında kullanıcı, medya ve analizler korunuyor.

Detaylı işletim, yedekleme ve güvenlik bilgileri için [OPERATIONS.md](OPERATIONS.md); sağlayıcılar için [PROVIDERS.md](PROVIDERS.md); üçüncü taraf API için [API.md](API.md) belgelerine bakın.
