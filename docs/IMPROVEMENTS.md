# İşleme, kalite, paylaşım ve işletim geliştirmeleri

## Nereden kullanılır?

- Video ekranı → **İşlem adımları, kullanım ve saklama**: aşamalar, tamamlanan 8 dakikalık ses parçaları, iptal, tam metin AI taraması, kullanım ve otomatik silme tercihi.
- **Transkript kalite kontrolü**: ikinci STT karşılaştırması, ElevenLabs ses/metin hizalama önerisi, elle zaman düzeltmesi. Dinleme onayı olmadan öneri uygulanmaz; özgün metin ve son 10 sürüm korunur.
- Sohbette **Yanıt türü**: soru, özet, karar, yapılacak işler veya saniye aralığına göre bölüm açıklaması. Yeni AI yanıtlarında her iddianın kaynakları ayrı gösterilir.
- **Paylaş ve göm**: Orbital/minimal tema, başlangıç saniyesi, metin/konular/sohbet görünürlüğü. Ayarlar sunucuda saklanır; URL değiştirilerek sohbet/altyazı açılmaz.
- **Yalnızca izinli hesaplar**: 1–10 mevcut hesabın e-postasıyla özel bağlantı. Giriş ve hesap yetkisi her uçta kontrol edilir. Bu bağlantının iframe gömmesi kapalıdır; üçüncü taraf çerez engellerine dayanıklı özel iframe/SSO bu sürümde yoktur. Yeni bağlantı eskisini geçersiz kılar.
- Yönetici → **AI sağlayıcıları**: ana STT'den farklı bir ikinci STT profili seçilebilir.
- Yönetici → **İşlemler ve kullanım** (`/admin/operations`): son 100 iş, hata kodları ve bugünün UTC kullanım toplamı. Başka kullanıcının video içeriğini açma yetkisi vermez.

## Kurtarma ve maliyet sınırları

Her başarılı STT parçası ve AI kalite taraması grubu veritabanına kaydedilir. Tekrar deneme bu kayıtları kullanır. STT/model/endpoint/sözlük değişmişse parçalar sessizce karıştırılmaz; eski ayara dönülmesi veya açıkça sıfırlanması gerekir. Konu ve indeks aşamaları yeniden ücret doğurabilir. Sağlayıcı yanıtından sonra veritabanına yazılamayan son çağrıda kesin tek-ücret garantisi yoktur. İptal sağlayıcıdaki başlamış işi/ücreti geri almaz.

`Usage` yapılandırması:

| Alan | Varsayılan |
| --- | --- |
| `MaxCallsPerVideoDay` | 250 |
| `MaxCallsPerOwnerDay` | 1000 |
| `MaxEstimatedUsdPerOwnerDay` | 10 USD |
| `Rates` | Sağlayıcı + model bazlı düzenlenebilir planlama tarifeleri |

Tarifeler güncel fiyat listesi veya fatura değildir. Sağlayıcının döndürdüğü OpenRouter `usage.cost` ayrı gösterilir. Token sayısı yoksa giriş karakterlerinden ve en fazla 2500 çıkış tokenından tahmin yapılır. Bilinmeyen model/hizalamada ücret **bilinmiyor**; dolar sınırı bunu kapsayamaz, çağrı sınırı uygulanır. Gerçek sağlayıcı hesaplarında da harcama sınırı/alarm kurun. Bu sınırlar sunucu yapılandırmasıdır, panelden fiyat düzenleme henüz yoktur.

Başarısız, iptal edilmiş veya sonucu kaydedilememiş istekler sıfır maliyet sayılmaz. Kullanım kaydı video/hesap kimliği, sağlayıcı/model, işlem, zaman, token/ses miktarı ve ücret içerir; metin, soru, ses veya anahtar içermez. Video silinerek günlük bütçe sıfırlanamasın diye bu içeriksiz kayıtlar 30 gün tutulur; saatlik temizlikle kaldırılır. Token/ses toplamları aynı bölümün tekrar işlemesini de sayar.

## Kalite sınırları

Tam metin taraması 40 konuşma / yaklaşık 14.000 karakterlik gruplarla, önceki komşu konuşma ve kullanıcı sözlüğüyle çalışır. Tüm konuşmalar taranır; uzak iki bölüm arasındaki her çelişkiyi bulma garantisi yoktur. En fazla 300 uyarı saklanır. Sessizlik taraması -40 dB / en az 1 saniye aralıklarında, metin süresinin %80'i düşük sesliyse uyarır; çok kısık gerçek konuşma da uyarı alabilir.

STT sözlük desteği: OpenAI Whisper `prompt`, ElevenLabs **scribe_v2** `keyterms`, n8n multipart `terms` (JSON `[{"term":"ARR","description":"Yıllık gelir"}]`). ElevenLabs scribe_v1 ve mevcut Azure Speech adaptörüne desteklenmeyen sözlük parametresi gönderilmez; sözlük LLM analizinde yine kullanılır. ElevenLabs hizalama mevcut metni sese yerleştirme önerisidir; metnin doğru olduğunu kanıtlamaz. Canlı API sözleşmeleri gerçek hesaplarla ayrıca kabul testinden geçirilmelidir.

Kaynak ID kontrolü iddianın semantik doğruluğunu kanıtlamaz. Eski n8n yanıtları `claims` olmadan dönüyorsa eski yanıt + genel kaynak biçimi desteklenir; iddia bazlı deneyim için akışı güncelleyin. Uzun video özeti zaman bölgelerine dengeli dağılmış en fazla 48.000 karakterden hazırlanır; eksiksiz toplantı tutanağı değildir. AI ayarlı değilse sonuç açıkça alıntı aramasıdır.

## Saklama ve ölçek

Otomatik silme varsayılan olarak kapalıdır. Video sahibi 1–365 gün seçip açıkça onaylar; 0 süresiz saklar. İşçi saatte bir en fazla 20 süresi dolmuş, işlemde olmayan videoyu temizler. İşlemdeki video ve yedek kopyalar anında silinmez; yedek politikasını işletmeci ayrıca yönetir. Bu davranış kesin saniyeli hukuki silme SLA'sı değildir.

Tek uygulama örneği + SQLite + kalıcı disk korunmuştur. Ayrı worker servisi, PostgreSQL, nesne depolama, dağıtık kuyruk ve SSO **henüz uygulanmadı**. Bu sürüm çoklu replika ile başlatılmamalıdır. Bunlar yeni altyapı/dağıtım kararı gerektiren sonraki ölçekleme işidir.

Yükseltmeden önce veritabanı, medya ve Data Protection anahtarlarını birlikte yedekleyin. Migration mevcut videolarda listeleri boş, paylaşım sekmelerini açık, saklamayı süresiz başlatır; içerik silmez. Geri dönüş için doğrulanmış yedek + önceki sürüm kullanın.
