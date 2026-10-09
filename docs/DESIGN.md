# VideoAtlas · Orbital tasarım dili

Kullanıcının paylaştığı mobil AI arayüzü referansından uyarlanan tasarım: neredeyse siyah yüzeyler, mavi/mor/turkuaz ışık geçişleri, parlak küre, yuvarlatılmış kartlar ve cam etkili sohbet alanı. Başka ürünlerin marka ve logoları kullanılmaz.

## Ekranlar

- **Giriş / kayıt:** ortak Razor partial; yörüngeler, özgün vektör küre ve ışıklı ufuk. Mobilde formu geriye itmemek için daha kısa bir görsel alan.
- **Kütüphane:** koyu gezinme, ışıklı tanıtım paneli, yuvarlak arama alanı, gerçek video kapakları; boş/yükleniyor/hata durumları aynı tema içinde.
- **Oynatıcı:** videonun renklerine müdahale edilmez. Metin ve konu panelleri nötr koyu yüzeyde; etkin konuşma ve zaman kodları belirgin.
- **Sohbet:** siyah üst alan, özgün küre, mor/turkuaz alt ışık, cam etkili soru alanı. Gerçek yanıtlar kaynak düğmeleriyle gösterilir.
- **Paylaşım / iframe:** aynı görsel sistem; yükleme, paylaşma ve silme pencerelerinde koyu form kontrolleri.

## Uygulama

Tasarım token'ları ve duyarlı yerleşimler `src/VideoAtlas.Web/wwwroot/css/site.css` içinde. Ana token'lar:

| Amaç | Token |
|---|---|
| Sayfa / kart / yükseltilmiş yüzey | `--canvas`, `--paper`, `--surface-raised` |
| Birincil / yardımcı metin | `--ink`, `--muted` |
| Mor vurgu / zaman kodları | `--primary`, `--cyan` |
| Ana eylem / cam yüzey | `--action-gradient`, `--glass-gradient` |
| Başarı / hata | `--success`, `--danger` |

Küre ve oklar yerel SVG; logo üçgeni CSS çizimidir. Harici font, CDN, takip servisi, satır içi betik veya görsel üretim servisine çalışma zamanı bağımlılığı yoktur. Yazı tipi sistem fontudur. Küre dekoratiftir, ekran okuyucularda tekrar okunmaz.

Görünür klavye odağı, dar ekranda da erişilebilir gezinme adları, gerçek form etiketleri ve azaltılmış hareket tercihi korunur. Referanstaki mikrofon veya mod seçicisi gibi üründe işlevi olmayan kontroller eklenmez. AI yapılandırılmamışsa mevcut açıklama ve alıntı arama modu görünür kalır.

## Doğrulama

`CHROMIUM_PATH=/usr/bin/chromium npm run test:e2e` gerçek uygulamadan ekran görüntülerini `artifacts/` altına üretir. Login, kütüphane, oynatıcı, yükleme/paylaşım pencereleri ve gömme ekranı kontrol edilir; 320, 390, 768, 1024 ve 1440 piksel yerleşimler kapsam içindedir. axe-core kontrolleri WCAG A/AA ile etiketlenmiş otomatik kuralları kapsar; tek başına tam erişilebilirlik sertifikası değildir.

Güncel çalışma sonuçları: [VALIDATION.md](VALIDATION.md).
