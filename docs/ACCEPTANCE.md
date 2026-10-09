# Gerçek video kabul çalışması

Bu paket gerçek STT doğruluğu veya canlı OpenRouter/ElevenLabs erişimi için başarı iddiası içermez. Kontrollü sağlayıcı testleri API/arayüz davranışını doğrular; gerçek ses doğruluğunu ölçmez. Bu çalışma için yayın/işleme hakkınız olan örnekler, etkin sağlayıcı hesabı ve harcama limiti gerekir.

## Örnek seti

Her sınıftan en az bir, tercihen üç gerçek kayıt seçin: temiz Türkçe konuşma; Türkçe/İngilizce teknik terimler; gürültülü/uzak mikrofon; birden fazla konuşmacı; uzun toplantı (baş/orta/son). Kişisel veri içermeyen 1–3 dakikalık kayıtla başlayın. Sessizlik ve videoda olmayan bilgi sorusunu ayrıca deneyin.

Her video için insanın dinleyerek hazırladığı referans SRT, 5 cevaplanabilir soru + kaynak aralıkları, 2 videoda olmayan soru, özel terimler ve beklenen başlıklar hazırlayın. Sağlayıcı/model, tarih, sözlük, ücret, işleme süresi ve metin sürümünü sonuç formuna yazın. Tam kalite taramasını kapalı/açık ayrı ölçün; sesle onaylanmış düzeltme sonrası sonucu ham STT ile karıştırmayın.

## Yerel ölçüm

```bash
node scripts/evaluate-transcript.mjs reference.srt downloaded.srt
# Yalnızca iki dosyanın bölümleri insan tarafından birebir eşleştirildiyse:
node scripts/evaluate-transcript.mjs reference.srt downloaded.srt --paired-boundaries
npm run test:metrics
```

Araç ağa veri göndermez; çıktıda metin yoktur. Türkçe küçük harf / harf-rakam normalizasyonu ve kelime düzenleme uzaklığıyla WER hesaplar. WER %100'ü aşabilir; doğrudan doğruluk yüzdesi değildir. Aynı konuşmaya karşılık gelmeyen SRT bölümlerinin zamanlarını indeksle eşleştirmek geçersiz ölçümdür; bu yüzden zaman metriği varsayılan olarak kapalıdır. Büyük dosyaları aynı aralıklara bölün; WER'i referans kelime sayısıyla ağırlıklandırın.

## Önerilen yayın kapıları (ölçülmüş sonuç değildir)

- Temiz ses için WER ≤ %15 başlangıç hedefi; özel ad/sayı/para/tarih kritik hataları ayrıca sıfırlanmalı veya belirgin uyarı taşımalı.
- Elle eşleştirilmiş bölüm sınırlarında ortalama mutlak kayma ≤ 1 saniye; başlangıç/orta/son kaynağına tıklama dinlenerek doğrulanmalı.
- Değerlendirilen her cevap iddiası ilgili alıntıda desteklenmeli; bulunmayan sorularda içerik uydurulmamalı. En az 20 soru ve ikinci insan gözden geçirmesi önerilir.
- Özel paylaşım: anonim / izinsiz hesap reddi; izinli hesap girişi ve süre dolması/iptal. Açık iframe: farklı HTTPS sitede, üçüncü taraf çerezler kapalıyken oynatma ve sohbet.
- İptal / yeniden başlatma / sağlayıcı kesintisi: tamamlanan ses parçaları tekrar gönderilmemeli; sonuçsuz son çağrının ücret belirsizliği görünür olmalı.
- Sunucu tahmini kullanımını sağlayıcı panelindeki gerçek ücretle karşılaştırın; fiyat tarifesini düzeltin. Ücret/süre kabul eşiğini kendi kullanıcı hacminize göre belirleyin.

## Sonuç kaydı şablonu

| Örnek | Sağlayıcı/model | Ham WER | Onay sonrası WER | Sınır kayması | Destekli iddia/toplam | Uydurma yanıt | Süre | Sağlayıcı ücreti | Karar |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Henüz gerçek örnek çalıştırılmadı | — | — | — | — | — | — | — | — | Yayın kabulü bekliyor |

Sözlük/yönergelerin içindeki komutlar, videodaki prompt injection, para/sayı çelişkisi ve gerçek konu değişimi senaryolarını da değerlendirin. Üretime çıkış; canlı kabul, HTTPS, erişim/yedek testi ve işletmecinin gizlilik metni tamamlandıktan sonra yapılmalıdır.
