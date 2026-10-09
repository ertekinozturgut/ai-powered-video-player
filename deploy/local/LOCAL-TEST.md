# Yerel test paketi

Docker Desktop (Linux containers) veya Docker Engine + Compose v2 gerekir. ZIP'i açın, terminalde `VideoAtlas` klasörüne girin:

```sh
docker compose -f compose.local.yaml up -d --build --wait --wait-timeout 180
```

Tarayıcı: **http://localhost:5080**. Kendi hesabınızı oluşturun. Hazır kullanıcı/parola veya API anahtarı yoktur. İlk derleme internet ve birkaç dakika gerektirir. Host'a ayrıca .NET/FFmpeg kurulmaz. Bu adres kendi bilgisayarınızda başlattığınız uygulamadır; bulut önizlemesi değildir.

AI anahtarları olmadan kendi videonuzu ve SRT'nizi yükleyip zaman kodlarına atlama, konu başlıkları, alıntı araması, metin/zaman düzeltme ve paylaşımı deneyebilirsiniz. Otomatik gerçek transkripsiyon ve üretken AI için sağlayıcıları yapılandırın. Yönetici rolü için, kayıt olduktan sonra e-postayı değiştirerek:

```sh
docker compose -f compose.local.yaml exec -e BOOTSTRAP_EMAIL=hesabiniz@example.com app dotnet VideoAtlas.Web.dll --grant-admin
```

Çıkış/giriş yapın. **AI sağlayıcıları** panelinde ElevenLabs STT, OpenRouter sohbet ve embedding profillerine yenilenmiş anahtarları ayrı kaydedip aktif sağlayıcıları seçin. Anahtarları sohbet veya kaynak dosyasında paylaşmayın. Ağ, bakiye ve model erişimi gerekir. Gerçek kabul adımları `docs/ACCEPTANCE.md`, yeni özellikler `docs/IMPROVEMENTS.md`, test kanıtları `docs/VALIDATION.md` içindedir.

```sh
docker compose -f compose.local.yaml stop
docker compose -f compose.local.yaml start
docker compose -f compose.local.yaml ps
```

Veriler `videoatlas-local_local_data` volume'ünde korunur. **`down -v` kullanmayın**; verileri siler. 5080 doluysa `VIDEOATLAS_PORT=5081` ortam değişkenini ayarlayın. Development portunu internete açmayın; yalnızca loopback'e bağlıdır. Yerel iframe başka bilgisayarlardaki ziyaretçilere hizmet vermez; internet yayını için `docs/OPERATIONS.md` gerekir.

Bu paket kullanıcının bilgisayarında çalıştırılmış değildir. Güncel uygulama .NET ve Playwright ile bulut çalışma alanında test edilir; eski Docker testleri güncel image doğrulaması sayılmaz. Güncel sonuç ve çalıştırılmayan kontroller `docs/VALIDATION.md` içinde belirtilir.
