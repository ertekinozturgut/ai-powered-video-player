# Gerçek UI kullanım demosu

Demo mevcut Razor/JavaScript arayüzünü gerçek Chromium ve gerçek .NET sunucusuyla kaydeder. Uygulama ekranı maketle değiştirilmez; API yanıtları taklit edilmez. Açıklamalar kayıt sonrasında görüntünün **dışındaki** üst/alt alanlara eklenir.

## Yeniden oluşturma

```bash
dotnet build VideoAtlas.sln
npx playwright install ffmpeg
CHROMIUM_PATH=/usr/bin/chromium DOTNET_PATH=/workspace/.dotnet/dotnet node scripts/record-demo.mjs
node scripts/package-demo.mjs
node scripts/verify-demo.mjs
```

Sistemde FFmpeg/FFprobe, libx264, libvpx, libass, DejaVu Sans ve zip bulunmalıdır. Gerekirse DOTNET_PATH ve CHROMIUM_PATH değerlerini yerel kurulumunuza göre değiştirin.

Playwright FFmpeg indirme alanlarına bu buluttan erişilemiyorsa sistemdeki FFmpeg kullanılabilir. Kayıt aracı 1.58.2'nin beklediği ffmpeg-1011 dizini için **ayrı** bir araç önbelleği hazırlayın; kurulu tarayıcı önbelleğini değiştirmeyin:

```bash
mkdir -p artifacts/demo/tools/ffmpeg-1011
# Hedef yoksa oluşturun; mevcut dosyanın üzerine yazmayın.
test -e artifacts/demo/tools/ffmpeg-1011/ffmpeg-linux || ln -s /usr/bin/ffmpeg artifacts/demo/tools/ffmpeg-1011/ffmpeg-linux
PLAYWRIGHT_BROWSERS_PATH="$PWD/artifacts/demo/tools" node scripts/record-demo.mjs
node scripts/package-demo.mjs
node scripts/verify-demo.mjs
```

Script 5084 portunda yalnızca loopback'e bağlı ayrı Development uygulaması, 5085 portunda örnek iframe barındıran yerel site başlatır. 5080'deki geliştirme uygulamasına ve mevcut veri dizinine dokunmaz. Ayrı veri alanını işletim sisteminin geçici dizininde oluşturur ve konumunu bildirir. Kayıt sonrası kendi süreçlerini kapatır; geçici veriyi otomatik silmez. Parola çalışma anında rastgele oluşturulur, paketlenmez.

## Senaryo ve doğrulamalar

Giriş → kütüphane → video yükleme → SRT bekleme → örnek SRT içe aktarma → metinden 00:45'e atlama → metin araması → konu grupları → bütçe sorusu → kaynak koduna atlama → gerçek SRT indirme → iframe oluşturma → farklı yerel sitede gerçek iframe → anonim mobil kullanım → paylaşımı iptal etme → kütüphaneye dönüş.

Durumlar, metin sayısı, video zamanı, cevap içeriği, kaynaklar, indirilen SRT ve paylaşım iptalinden sonra HTTP 404 kontrol edilir. Yakalanan JavaScript hataları başarısızlıktır. Masaüstü kaydında yalnızca ayrı mobil kayıt çekilirken oluşan bekleme bölümü çıkarılır.

## Dürüst demo sınırları

- Video ve SRT, hassas veya üçüncü taraf içerik içermeyen sentetik örneklerdir.
- Örnek video sessiz bir sunumdur. SRT bu sunuma eşlik eden hazırlanmış örnektir; konuşma tanıma sonucu değildir.
- Demo sunucusunda AI anahtarı özellikle boş bırakılır. Gerçek alıntı arama modu ve yerel konu grupları gösterilir; üretken AI yanıtı veya otomatik transkripsiyon doğrulaması yapılmaz.
- MP4'ler sessizdir; Türkçe açıklamalar görüntüye işlenir, SRT'ler ayrıca verilir.
- HTML dosyası çevrimdışı **demo izleyicisidir**, canlı uygulama değildir. Paylaşım kayıttan sonra iptal edilir; paket çalışan paylaşım yetkisi veya oturum bilgisi içermez.

## Çıktılar

`artifacts/demo/videoatlas-kullanim-demo.mp4`, mobil MP4, açıklama SRT'leri, örnek giriş MP4/SRT, gerçek uygulamadan indirilen SRT, ekran görüntüleri, bölüm zamanları ve çevrimdışı izleyici. Teslim arşivi: `artifacts/videoatlas-demo-paketi.zip`. Ham kayıtlar, sunucu veri alanı, parolalar ve araç önbelleği teslim arşivine alınmaz.

Son kayıt: 3 Ekim 2026; masaüstü 98,92 saniye, H.264 1440×1144; mobil yaklaşık 14 saniye. Ayrı `verify-demo.mjs` kontrolü iki MP4'ün tarayıcıda oynadığını, 15 bölümlü izleyicide zaman atlamayı, mobilde yatay taşma olmadığını, indirilmiş SRT'nin girişle eşleştiğini ve FFmpeg ile videoların tamamının hatasız çözüldüğünü doğruladı. ZIP bütünlük testi geçti. Mevcut 5080 uygulaması kayıt sonrasında sağlıklı kaldı.
