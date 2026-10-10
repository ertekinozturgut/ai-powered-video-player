# Video bağlamı ve transkript kalite kontrolü

Yeni tüm metin taraması, sessizlik, ikinci STT ve zaman hizalama davranışı: [IMPROVEMENTS.md](IMPROVEMENTS.md). Gerçek doğruluk ölçümü: [ACCEPTANCE.md](ACCEPTANCE.md).

## Nereden kullanılır?

- **Video yükle → Videoya özel terimler ve analiz yönergeleri:** ilk analizden önce yardımcı bağlam girin.
- **Video çalışma alanı → Terimler ve yönergeler:** mevcut videonun sözlüğünü/yönergelerini düzenleyin. Bu bölüm normal video sahibine açıktır; sağlayıcı admin panelinden ayrıdır.
- **Video çalışma alanı → Transkript kalite kontrolü:** tüm konuşmaları veya yalnızca uyarılı bölümleri inceleyin. Iframe ziyaretçileri bu yönetim araçlarını kullanamaz.
- **Video yükle → Konuşmacı analizi:** destekleyen STT sağlayıcısıyla konuşmacı ayrımını açın ve beklenen sayıyı 2–32 arasında belirtin. Analizden sonra video araç çubuğundaki **Konuşmacılar** alanından teknik etiketlere ad veya rol verin.

Sözlük biçimi, her satırda `terim | açıklama`:

```text
VideoAtlas | Bu kayıtta anlatılan video analiz ürünü
ARR | Yıllık yinelenen gelir
```

En fazla 50 terim; terim 100, açıklama 500 karakter. Aynı terim tekrar edemez. Açıklama tek satırda olmalıdır. Analiz yönergeleri en fazla 4000 karakter; örneğin: “Kararları, sorumluları ve açık kalan riskleri öne çıkar.”

## Yardımcı bağlamın sınırı

Terimler ve yönergeler **konu analizi, kaynaklı soru-cevap ve seçili bölümün AI kalite incelemesinde** modele ayrı bir veri alanı olarak gönderilir. Sistem kurallarını geçersiz kılamaz; videoda konuşulmuş bilgi veya kaynak kanıtı değildir. Örneğin sözlükteki bir ürün tanımı, konuşmacı söylemediyse söylenmiş gibi alıntılanmamalıdır. Bu kural istemde uygulanır; her LLM yanıtının anlamsal doğruluğuna matematiksel garanti vermez.

Bu sürümde sözlük STT sağlayıcısının özel keyword/prompt parametresine aktarılmaz. Adaptörler arası farklı STT sözlük desteği sonraki genişletmedir. Sözlük, ilk STT sonucundaki benzer yazımları tespit etmeye ve sonraki metin incelemesine yardımcı olur.

Alanlar yalnızca sahibin yönetim API'sinde döndürülür; paylaşım API'si ham metin geçmişini, sözlüğü veya yönergeleri doğrudan açmaz. Ancak bağlam AI çıktısını etkiler ve modele gönderilir: bu alanlara sır/anahtar veya paylaşılmaması gereken kişisel bilgi girmeyin. AI'nın yönergeleri hiçbir koşulda açıklamayacağı iddia edilmez.

## İnceleme akışı

1. İlk transkripsiyondan veya SRT içe aktarmadan sonra kural tabanlı uyarılar hesaplanır: süreye göre aşırı metin yoğunluğu, ardışık tekrar, örtüşen zamanlar ve sözlükteki tek sözcüklü terimlere bir harf farkla benzeyen yazımlar. Bu göstergeler **STT güven puanı değildir**. Normal konu değişimi tek başına hata sayılmaz. En fazla 300 uyarı saklanır.
2. **Bölümü dinle:** mevcut videonun sesini ilgili aralıktan oynatır. Bu kontrol tarayıcıdaki gerçek medya üzerinden yapılır.
3. **AI ile metni incele:** seçili konuşmayı, komşu konuşmaları ve video bağlamını aktif LLM'ye gönderir. Olası yazım/bağlam sorununa gerekçe ve isteğe bağlı öneri döner. LLM bu aşamada sesi dinlemez. Belirsiz durumda öneri boş kalabilir; “uyarı yok” metnin doğru olduğu anlamına gelmez.
4. **Ses parçasını yeniden çözümle:** en fazla 90 saniyelik seçili bölümün başına/sonuna üçer saniye bağlam eklenir; yalnızca bu parça aktif STT sağlayıcısına gönderilir. Geçici ses dosyası istek sonunda, hata olsa da silinir. Örtüşen STT segmentlerinden öneri üretilir; komşu kelimeler taşabilir, kullanıcı düzenlemelidir. Ayrı bir ikinci STT sağlayıcısı otomatik seçilmez.
5. **Öneriyi düzenleme alanına al** yalnızca taslağı doldurur. **Düzeltmeyi onayla** için ses doğrulama kutusu zorunludur. Sunucu aynı onayı kontrol eder; kişinin gerçekten dinlediğini otomatik olarak kanıtlamaz. Sayı/tarih/tutar/isim değişiklikleri dahil hiçbir metin değişikliği onaysız uygulanmaz.
6. Bir uyarıyı reddetmek metni değiştirmez. “Uyarıları yeniden tara” işlemi, açık onaydan sonra önceki önerileri ve ret durumlarını temizleyip yalnızca kuralları yeniden hesaplar. Tek bir metin düzeltmesi başka bölümlerin AI/STT uyarılarını silmez.

## Sürümler, zamanlar ve yeniden analiz

- İlk ham metin ayrı saklanır. Önceden var olan kayıtlarda migration sırasındaki metin başlangıç kabul edilir; migration'dan önce kaybedilmiş bir ham transkript geri getirilemez.
- Son 10 metin sürümü ve değişiklik nedeni saklanır; bu sınırdan eski düzenleme sürümleri tutulmaz, ilk ham metin korunur. SRT içe aktarma da sürümlenir.
- Kullanıcı düzeltmesi segment ID'sini korur. Zamanlar varsayılan olarak korunur; **Zaman aralığını da düzelt** seçilip sesle onaylanırsa değişir. ElevenLabs hizalama yalnızca öneri üretir, otomatik uygulamaz. SRT içe aktarma ve önceki sürüme dönüş de kullanılabilir.
- Onay/geri yükleme eski konu ve vektörleri geçersiz kılar ve kalıcı iş kuyruğunu yeniden başlatır. STT tekrar çalışmaz; kabul edilmiş metin kullanılır. Başarıyla tamamlanınca SRT, konu başlıkları, indeks ve sohbet yeni metne dayanır. Sağlayıcı hatasında video başarısız durumda görünür; eski indeksle başarı gösterilmez. Sağlayıcılar kapalıysa yerel konu gruplama/alinti araması kullanılır, anlamsal indeks oluşmaz.
- İnceleme gereken bir segmente atıf yapan sohbet cevabı sesle doğrulama uyarısı taşır. Metin sürümü değişince sohbet, yeniden yükleme sırasında temizlenir. Her sohbet cevabı metin sürümünü taşır; tarayıcı yanıtı göstermeden güncel kaydı kontrol eder, eski sürüm cevabını yeni kaynaklara bağlamaz. Soru sormadan izlemeye devam eden diğer açık sekmeleri/iframe'leri yeni metni görmek için yenileyin.
- Bağlam değişikliği metni değiştirmez, mevcut kalite önerilerini geçersiz kılar ve konu/indeks işini tekrar kuyruğa alır.
- Bitmiş bir kayıtta **Konuşmacılıya çevir** seçeneği yerinde bir etiketleme değildir. Video sesi aktif STT sağlayıcısına yeniden gönderilir; transkript, konu başlıkları ve indeks yeniden oluşturulur. Arayüz bu maliyet ve veri aktarımını gönderimden önce açıkça bildirir. Önceki metin sürüm geçmişine alınır. Konuşmacı kimlikleri yalnızca yeni transkriptte gerçekten dönen etiketler için tanımlanabilir ve SRT/VTT dışa aktarımında adla birlikte gösterilir.

## Güvenlik ve maliyet

Tüm inceleme/yazma uçları sahiplik ve oturum kontrolü yapar; mutasyonlarda CSRF zorunludur. Paylaşım token'ıyla düzenleme yapılamaz. Metin/öneri HTML olarak çalıştırılmaz. İşlenen videoda düzenleme reddedilir. İstemci sürümü eskiyse 409 döner; başka sekmede değişmiş metne eski öneri uygulanmaz. Pahalı AI/ses işi sırasında uygulamanın genel yazma kilidi tutulmaz; dönüşte sahiplik, iş durumu ve sürüm yeniden kontrol edilir.

LLM inceleme ve hedefli STT birlikte hesap başına dakikada altı istekle sınırlıdır. İstekler ücretli olabilir; otomatik yeniden deneme yoktur. Uygulama yeniden başlaması/istemci bağlantısının kopması gibi durumlarda ücretli çağrının tamamlanıp tamamlanmadığını sağlayıcıdan kontrol etmek gerekebilir. Sağlayıcı maliyet bütçelerini ayrıca ayarlayın.

SQLite ve Data Protection anahtarlarıyla birlikte metin geçmişi de yedeklenir; eski yanlış/hassas ifadeler geçmişte kalır. Video silme kayıtla birlikte geçmişi de kaldırır; eski yedeklerin silinmesi ayrı saklama politikasına tabidir.

## Doğrulama ve kapsam dışı

.NET testleri gerçek FFmpeg ile hedefli ses çıkarma ve temizlemeyi; kontrollü sağlayıcılarla inceleme sözleşmesini; veri koruyan migration'ı, sürüm çakışmasını, sahipliği, CSRF'yi, eski indeksi geçersiz kılmayı ve ham metne dönüşü kapsar. Tarayıcı testi gerçek UI üzerinden sözlük yükleme/düzenleme, uyarı filtreleme, ses oynatma, düzeltme, SRT ve geri yükleme akışını doğrular. Test videosu sentetik sinyal içerir; insan konuşmasını doğru tanıma testi değildir.

Gerçek ElevenLabs/OpenRouter kalite sonuçları bu teslimde doğrulanmadı. Ağ/anahtar aktivasyonu koşulları [PROVIDERS.md](PROVIDERS.md) içinde. Konuşmacı ayrımı ElevenLabs ve sözleşmeye uyan n8n STT akışlarında vardır; OpenAI/Azure/local Whisper adaptörlerinde yoktur. OCR, öğrenilmiş VAD, sağlayıcıya özgü güven puanları ve ikinci sağlayıcıyla otomatik doğruluk uzlaşması yoktur. Eşik tabanlı sessizlik taraması, isteğe bağlı tüm metin AI incelemesi, kullanıcı başlatmalı ikinci STT ve ElevenLabs hizalama adaptörü eklendi; bunlar doğruluk garantisi değildir.
