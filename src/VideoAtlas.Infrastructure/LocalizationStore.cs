using System.Text.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using VideoAtlas.Application;

namespace VideoAtlas.Infrastructure;

public sealed class UiLanguageEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; }
    public bool IsDefault { get; set; }
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
public sealed class UiTranslationEntity
{
    public string LanguageCode { get; set; } = "";
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
public sealed class UserLanguagePreferenceEntity
{
    public string UserId { get; set; } = "";
    public string LanguageCode { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string DisplayName { get; set; } = "";
}
public sealed class UiContentDefinitionEntity
{
    public string Key { get; set; } = "";
    public string DefaultText { get; set; } = "";
    public string Context { get; set; } = "";
}

public sealed class LocalizationStore(AppDbContext db, IAiProviderFactory providers, TimeProvider clock, IHostEnvironment environment) : ILocalizationAdministration
{
    private bool ensured;
    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["nav.workspace"] = "ÇALIŞMA ALANIM", ["nav.library"] = "Video kütüphanesi", ["nav.privacy"] = "Gizlilik ve veriler",
        ["nav.systemAdmin"] = "Sistem yönetimi", ["nav.operations"] = "İşlemler ve kullanım", ["nav.providers"] = "AI sağlayıcıları", ["nav.account"] = "Çalışma hesabı",
        ["nav.password"] = "Parolamı değiştir", ["nav.logout"] = "Güvenli çıkış", ["upload.eyebrow"] = "YENİ BİR KEŞİF",
        ["upload.title"] = "Medya yükle", ["upload.file"] = "Video veya ses dosyanızı buraya bırakın", ["upload.fileHelp"] = "veya dosya seçmek için tıklayın",
        ["upload.mediaTitle"] = "Medya yükle", ["upload.mediaFile"] = "Video veya ses dosyanızı buraya bırakın",
        ["upload.videoTitle"] = "Video başlığı", ["upload.terms"] = "Terimler ve açıklamaları", ["upload.instructions"] = "Analiz yönergeleri",
        ["upload.language"] = "Analiz dili", ["upload.submit"] = "Yükle ve işlemeye başla →", ["chat.title"] = "Videoya sorun",
        ["speaker.analysis"] = "Konuşmacı analizi", ["speaker.separate"] = "Konuşmacıları ayırarak metne çevir", ["speaker.count"] = "Konuşmacı sayısı",
        ["speaker.countHelp"] = "2–32 arasında, videoda konuşan toplam kişi sayısını girin.", ["speaker.providerHelp"] = "Konuşmacı ayrımı etkin STT sağlayıcısı tarafından desteklenmelidir ve ek ücret doğurabilir.",
        ["speaker.manage"] = "Konuşmacılar", ["speaker.convert"] = "Konuşmacılıya çevir", ["speaker.identitiesHelp"] = "Analizde bulunan konuşmacı etiketlerine gerçek kişi adlarını yazabilirsiniz. Bu işlem yeniden analiz gerektirmez.",
        ["speaker.save"] = "Konuşmacı adlarını kaydet", ["speaker.reanalysisSettings"] = "Yeniden analiz ayarları", ["speaker.separateAnalysis"] = "Konuşmacıları ayırarak analiz et",
        ["speaker.reanalysisWarning"] = "Konuşmacı ayrımı sonradan mevcut metne eklenemez. Ses yeniden STT sağlayıcısında işlenir; metin, konu başlıkları ve indeks baştan hazırlanır. Local Whisper sesi cihazda tutar; harici bir sağlayıcı seçiliyse ek API ücreti doğabilir.",
        ["speaker.reanalyze"] = "Tam yeniden analizi başlat", ["speaker.start"] = "Analizi bu ayarlarla başlat", ["speaker.saved"] = "Konuşmacı adları kaydedildi.",
        ["speaker.started"] = "Konuşmacı ayrımlı tam analiz başlatıldı.", ["speaker.startedPlain"] = "Konuşmacı ayrımı olmadan tam analiz başlatıldı.",
        ["speaker.unsupported"] = "Aktif STT sağlayıcısı konuşmacı ayrımını desteklemiyor. ElevenLabs veya uyumlu bir n8n STT akışı seçin.", ["speaker.invalidCount"] = "Konuşmacı sayısı 2–32 arasında olmalıdır.",
        ["chat.summary"] = "Video özeti", ["chat.highlights"] = "Önemli noktalar", ["chat.chapters"] = "Bölümleri listele",
        ["admin.localization"] = "Diller ve içerikler", ["admin.localizationHelp"] = "Arayüz metinlerini, tanımları ve analiz dillerini yönetin.",
        ["admin.addLanguage"] = "Dil ekle", ["admin.importJson"] = "JSON içe aktar", ["admin.fillAi"] = "AI ile eksikleri doldur",
        ["home.workspace"] = "Kişisel çalışma alanı", ["home.library"] = "Kütüphane", ["home.private"] = "Özel ve güvenli",
        ["home.eyebrow"] = "BİLGİNİZ, TEK BİR YERDE", ["home.title"] = "Video kütüphanesi", ["home.description"] = "Konuşmaları keşfedin, sorular sorun, önemli anları paylaşın.",
        ["home.myVideos"] = "Videolarım", ["home.search"] = "Bu sayfadaki videolarda ara…", ["home.loading"] = "Videolarınız yükleniyor…",
        ["home.emptyTitle"] = "İlk keşfiniz bir videoyla başlar", ["home.emptyDescription"] = "Bir toplantı, ders veya söyleşi yükleyin. Konuşmaları bilgiye dönüştürmeye başlayın.",
        ["home.firstUpload"] = "İlk videoyu yükle", ["home.previous"] = "← Önceki", ["home.next"] = "Sonraki →",
        ["account.title"] = "Hesabım", ["account.back"] = "← Video kütüphanesi", ["account.settings"] = "Hesap ayarları",
        ["account.description"] = "Hesap bilgilerinizi yönetin.", ["account.profile"] = "Profil ve arayüz", ["account.displayName"] = "Görünen ad",
        ["account.language"] = "Varsayılan arayüz dili", ["account.email"] = "E-posta adresi", ["account.currentPassword"] = "Mevcut parola",
        ["account.save"] = "Bilgileri kaydet", ["account.changePassword"] = "Parola değiştir", ["account.newPassword"] = "Yeni parola",
        ["account.confirmPassword"] = "Yeni parola (tekrar)", ["common.close"] = "Kapat"
    };
    private static readonly IReadOnlyDictionary<string, string> EnglishDefaults = new Dictionary<string, string>
    {
        ["nav.workspace"] = "WORKSPACE", ["nav.library"] = "Video library", ["nav.privacy"] = "Privacy and data",
        ["nav.systemAdmin"] = "System administration", ["nav.operations"] = "Operations and usage", ["nav.providers"] = "AI providers", ["nav.account"] = "Work account",
        ["nav.password"] = "Change password", ["nav.logout"] = "Sign out securely", ["upload.eyebrow"] = "A NEW DISCOVERY",
        ["upload.title"] = "Upload media", ["upload.file"] = "Drop your video or audio file here", ["upload.fileHelp"] = "or click to choose a file",
        ["upload.mediaTitle"] = "Upload media", ["upload.mediaFile"] = "Drop your video or audio file here",
        ["upload.videoTitle"] = "Video title", ["upload.terms"] = "Terms and descriptions", ["upload.instructions"] = "Analysis instructions",
        ["upload.language"] = "Analysis language", ["upload.submit"] = "Upload and start processing →", ["chat.title"] = "Ask the video",
        ["speaker.analysis"] = "Speaker analysis", ["speaker.separate"] = "Transcribe with speaker separation", ["speaker.count"] = "Number of speakers",
        ["speaker.countHelp"] = "Enter the total number of speakers in the video, from 2 to 32.", ["speaker.providerHelp"] = "Speaker separation must be supported by the active STT provider and may incur additional charges.",
        ["speaker.manage"] = "Speakers", ["speaker.convert"] = "Convert to speaker-separated", ["speaker.identitiesHelp"] = "You can assign real names or roles to the speaker labels found in the analysis. This does not require reanalysis.",
        ["speaker.save"] = "Save speaker names", ["speaker.reanalysisSettings"] = "Reanalysis settings", ["speaker.separateAnalysis"] = "Analyze with speaker separation",
        ["speaker.reanalysisWarning"] = "Speaker separation cannot be added to an existing transcript in place. The audio is processed again by the STT provider, and the transcript, topics, and index are rebuilt. Local Whisper keeps audio on-device; an external provider may incur additional API charges.",
        ["speaker.reanalyze"] = "Start full reanalysis", ["speaker.start"] = "Start analysis with these settings", ["speaker.saved"] = "Speaker names saved.",
        ["speaker.started"] = "Full speaker-separated analysis started.", ["speaker.startedPlain"] = "Full analysis without speaker separation started.",
        ["speaker.unsupported"] = "The active STT provider does not support speaker separation. Select ElevenLabs or a compatible n8n STT workflow.", ["speaker.invalidCount"] = "The number of speakers must be between 2 and 32.",
        ["chat.summary"] = "Video summary", ["chat.highlights"] = "Key highlights", ["chat.chapters"] = "List chapters",
        ["admin.localization"] = "Languages and content", ["admin.localizationHelp"] = "Manage UI text, definitions, and analysis languages.",
        ["admin.addLanguage"] = "Add language", ["admin.importJson"] = "Import JSON", ["admin.fillAi"] = "Fill missing items with AI",
        ["home.workspace"] = "Personal workspace", ["home.library"] = "Library", ["home.private"] = "Private and secure",
        ["home.eyebrow"] = "YOUR KNOWLEDGE, ALL IN ONE PLACE", ["home.title"] = "Video library", ["home.description"] = "Explore conversations, ask questions, and share important moments.",
        ["home.myVideos"] = "My videos", ["home.search"] = "Search videos on this page…", ["home.loading"] = "Loading your videos…",
        ["home.emptyTitle"] = "Your first discovery starts with a video", ["home.emptyDescription"] = "Upload a meeting, lesson, or interview and turn conversations into knowledge.",
        ["home.firstUpload"] = "Upload your first video", ["home.previous"] = "← Previous", ["home.next"] = "Next →",
        ["account.title"] = "My account", ["account.back"] = "← Video library", ["account.settings"] = "Account settings",
        ["account.description"] = "Manage your account information.", ["account.profile"] = "Profile and interface", ["account.displayName"] = "Display name",
        ["account.language"] = "Default interface language", ["account.email"] = "Email address", ["account.currentPassword"] = "Current password",
        ["account.save"] = "Save information", ["account.changePassword"] = "Change password", ["account.newPassword"] = "New password",
        ["account.confirmPassword"] = "Confirm new password", ["common.close"] = "Close"
    };
    private static readonly IReadOnlyDictionary<string, string> EnglishStaticDefaults = new Dictionary<string, string>
    {
        ["İçeriğe geç"] = "Skip to content", ["Ana gezinme"] = "Main navigation", ["Arayüz dili"] = "Interface language",
        ["DAHA FAZLASINI KEŞFEDİN"] = "DISCOVER MORE", ["Her an, yeni bir fikir."] = "A new idea in every moment.",
        ["Videolarınızla konuşun."] = "Talk to your videos.", ["Bilgiye doğrudan ulaşın."] = "Reach knowledge directly.",
        ["Sağlayıcı durumu"] = "Provider status", ["STT: devre dışı; SRT içe aktarın. Sohbet: alıntı arama modu. Aktif sağlayıcıları yönetici panelinden seçebilirsiniz."] = "STT is disabled; import an SRT file. Chat is in quote-search mode. Select active providers in the administration panel.",
        ["AI sağlayıcı yönetimi"] = "AI provider management", ["HER ANIN BİR CEVABI VAR"] = "EVERY MOMENT HAS AN ANSWER",
        ["Sadece izlemeyin."] = "Do more than watch.", ["Videonuzla konuşun."] = "Talk to your video.", ["Sadece izlemeyin. Videonuzla konuşun."] = "Do more than watch. Talk to your video.",
        ["Bir soru sorun. Doğru anı bulun."] = "Ask a question. Find the exact moment.", ["Konuşmaları, kaynağı belli cevaplara dönüştürün."] = "Turn conversations into source-backed answers.",
        ["Bir soru sorun. Doğru anı bulun. Konuşmaları, kaynağı belli cevaplara dönüştürün."] = "Ask a question. Find the exact moment. Turn conversations into source-backed answers.",
        ["◷ Zaman kodlu metin"] = "◷ Time-coded transcript", ["▤ Konu başlıkları"] = "▤ Topic chapters", ["✦ Kaynaklı sohbet"] = "✦ Source-backed chat",
        ["Yeni bir bakış açısı"] = "A new perspective", ["Video veya ses · 512 MiB / 2 saat"] = "Video or audio · 512 MiB / 2 hours",
        ["Örnek video başlığı"] = "Example video title", ["Konuşmacı analizi"] = "Speaker analysis",
        ["Konuşmacıları ayırarak metne çevir"] = "Transcribe with speaker separation", ["Konuşmacı sayısı"] = "Number of speakers", ["Konuşmacı sayısı (isteğe bağlı)"] = "Number of speakers (optional)", ["Otomatik tespit"] = "Automatic detection",
        ["Boş bırakırsanız sistem ses benzerliği, frekans ve stereo kanal ayrımından konuşmacı sayısını otomatik tahmin eder. İsterseniz 2–32 arasında sabitleyebilirsiniz."] = "Leave this blank to estimate the number of speakers automatically from voice similarity, frequency features, and stereo channel separation. You may also set a fixed value from 2 to 32.",
        ["Boş bırakırsanız ses benzerliği, frekans ve stereo kanal ayrımı birlikte değerlendirilir."] = "Leave this blank to evaluate voice similarity, frequency features, and stereo channel separation together.",
        ["2–32 arasında, videoda konuşan toplam kişi sayısını girin."] = "Enter the total number of speakers in the video, from 2 to 32.",
        ["Konuşmacı ayrımı etkin STT sağlayıcısı tarafından desteklenmelidir ve ek ücret doğurabilir."] = "Speaker separation must be supported by the active STT provider and may incur additional charges.",
        ["Videoya özel terimler ve analiz yönergeleri (isteğe bağlı)"] = "Video-specific terms and analysis instructions (optional)",
        ["TERİM_1 | Örnek açıklama"] = "TERM_1 | Example description", ["En fazla 50 terim. Terim 100, açıklama 500 karakter; her terim ayrı satırda."] = "Up to 50 terms. Each term may contain 100 characters and each description 500 characters; enter one term per line.",
        ["Örnek analiz yönergesi"] = "Example analysis instruction", ["Yönergeler ve sözlük yardımcı bağlamdır; videoda söylenmiş bilgi sayılmaz. Etkin AI sağlayıcısına gönderilir."] = "Instructions and the glossary are supporting context, not statements made in the video. They are sent to the active AI provider.",
        ["Konu başlıkları, kalite önerileri ve AI yanıtları seçtiğiniz dilde üretilir."] = "Topic chapters, quality suggestions, and AI answers are generated in the selected language.",
        ["Bu videoyu işleme hakkına sahibim. Video/ses/metnin işlenmesini ve etkin STT, dil modeli ve indeksleme sağlayıcılarına aktarılmasını"] = "I have the right to process this video. I consent to the processing of the video, audio, and transcript and their transfer to the active STT, language-model, and indexing providers under the",
        ["veri işleme açıklaması"] = "data-processing notice", ["kapsamında onaylıyorum."] = ".",
        ["Tüm metni AI ile bağlam ve dil bilgisi açısından tara; yalnızca güvenli düzeltmeleri otomatik uygula (ek API ücreti doğurabilir)."] = "Review the full transcript with AI for context and grammar; automatically apply only high-confidence corrections (additional API charges may apply).",
        ["Yükleme ilerlemesi"] = "Upload progress", ["Yükleme tamamlanınca analiz arka planda devam eder."] = "Analysis continues in the background after the upload completes.",
        ["◇ Çalışma alanınız"] = "◇ Your workspace", ["VİDEO ÇALIŞMA ALANI"] = "VIDEO WORKSPACE", ["SES ÇALIŞMA ALANI"] = "AUDIO WORKSPACE", ["MEDYA ÇALIŞMA ALANI"] = "MEDIA WORKSPACE",
        ["Hazır"] = "Ready", ["SRT bekliyor"] = "Awaiting SRT", ["İşleniyor"] = "Processing", ["İşlem başarısız"] = "Processing failed", ["İptal edildi"] = "Canceled", ["Sırada"] = "Queued",
        ["◇ Anahtar kelime gruplama"] = "◇ Keyword grouping", ["✦ AI konu analizi"] = "✦ AI topic analysis", ["↓ SRT indir"] = "↓ Download SRT", ["Paylaş ve göm"] = "Share and embed",
        ["Video ve konuşmalar"] = "Video and conversations", ["Video oynatıcı"] = "Video player", ["Ses oynatıcı"] = "Audio player", ["Konuşmaya tıklayın, o ana gidin."] = "Select a transcript line to jump to that moment.",
        ["Video bilgileri"] = "Video details", ["▤ Metin dökümü"] = "▤ Transcript", ["◫ Konu başlıkları"] = "◫ Topic chapters", ["Konuşmalarda ara"] = "Search conversations", ["Konuşmalarda ara…"] = "Search conversations…",
        ["Düzenle"] = "Edit", ["Metni ve zamanı düzenle"] = "Edit text and timing", ["Konu başlığını ve zamanı düzenle"] = "Edit topic and timing", ["İnceleme gerekli"] = "Review required", ["Hata Olabilir"] = "Possible Error",
        ["AI öner"] = "AI suggestion", ["AI ile incele ve doğru alternatif öner"] = "Review with AI and suggest the correct alternative", ["AI ile düzelt"] = "Correct with AI", ["Konuşmayı genel bağlamla AI üzerinden incele ve düzeltme öner"] = "Review the conversation with AI using the full context and suggest a correction",
        ["Terimler ve yönergeler"] = "Terms and instructions", ["Transkript kalite kontrolü"] = "Transcript quality review", ["＋ SRT içe aktar"] = "＋ Import SRT", ["↻ Yeniden analiz et"] = "↻ Reanalyze", ["Videoyu sil"] = "Delete video",
        ["İşlem adımları, kullanım ve saklama"] = "Processing steps, usage, and retention", ["Zaman kodlu, kaynağı belli cevaplar"] = "Time-coded, source-backed answers",
        ["Sohbeti temizle"] = "Clear chat", ["MERHABA, MERAK EDEN."] = "HELLO, CURIOUS MIND.", ["Birlikte ne keşfedelim?"] = "What shall we discover together?",
        ["Birlikte ne"] = "What shall we", ["keşfedelim?"] = "discover together?", ["Videonuzun içindeki cevaplara,"] = "Find answers inside your video,", ["kaynağıyla birlikte ulaşın."] = "together with their sources.",
        ["Videonuzun içindeki cevaplara, kaynağıyla birlikte ulaşın."] = "Find answers inside your video together with their sources.", ["Bu videoda neler konuşuluyor?"] = "What is discussed in this video?",
        ["Kararları ve aksiyonları bul"] = "Find decisions and action items", ["Ana fikri özetle"] = "Summarize the main idea", ["Hızlı aksiyonlar"] = "Quick actions",
        ["Seçili zaman aralığını özetle"] = "Summarize selected time range", ["Videoyla ilgili sorunuz"] = "Your question about the video", ["Bu video hakkında bir soru sorun…"] = "Ask a question about this video…", ["Soruyu gönder"] = "Send question",
        ["Alıntı arama modu · AI bağlantısı yapılandırılmamış."] = "Quote-search mode · AI connection is not configured.", ["Sohbet yalnızca bu sekmede tutulur."] = "Chat is kept only in this tab.",
        ["Zaman aralığını seçin"] = "Select a time range", ["Yalnızca belirlediğiniz bölüm analiz edilir."] = "Only the range you select will be analyzed.", ["Başlangıç"] = "Start", ["Bitiş"] = "End", ["Aralığı onayla ve özetle"] = "Confirm range and summarize",
        ["Video bağlamı ve yönergeler"] = "Video context and instructions", ["Sözlük ve yönergeler AI analizini yönlendirir; videoda söylenmiş bilgi yerine geçmez. Kaydetmek mevcut metni değiştirmez, konu ve indeks analizini yeniden başlatır."] = "The glossary and instructions guide AI analysis but do not replace information spoken in the video. Saving does not change the current transcript; it restarts topic and index analysis.",
        ["Terim ve açıklama listesi"] = "Term and description list", ["Terim"] = "Term", ["Açıklama"] = "Description", ["Terimi kaldır"] = "Remove term", ["+ Terim ekle"] = "+ Add term",
        ["Her satırda bir terim ve açıklaması bulunur. En fazla 50 terim; terim 100, açıklama 500 karakterdir."] = "Each row contains one term and its description. Up to 50 terms; terms may contain 100 characters and descriptions 500 characters.",
        ["Konu başlıkları, AI kalite önerileri ve video sorularının yanıt dili bu seçime göre hazırlanır."] = "Topic chapters, AI quality suggestions, and answers to video questions use this language.",
        ["Etkin AI sağlayıcısına aktarılır. Bu alanlar iframe ziyaretçilerine doğrudan gösterilmez; hassas veya gizli bilgi girmeyin, AI çıktısını etkileyebilir."] = "Sent to the active AI provider. These fields are not shown directly to iframe visitors; do not enter sensitive or confidential information because it may affect AI output.",
        ["Bağlamı kaydet ve yeniden analiz et"] = "Save context and reanalyze", ["Kategori"] = "Category", ["Tüm kategoriler"] = "All categories", ["Anahtar kelimeler"] = "Keywords",
        ["BİLGİ PAYLAŞTIKÇA BÜYÜR"] = "KNOWLEDGE GROWS WHEN SHARED", ["Paylaş ve sitene göm"] = "Share and embed on your site",
        ["Bağlantıya sahip kişiler videoyu, metni ve sohbeti kullanabilir. Gömme izni, bağlantıyı özel hale getirmez."] = "Anyone with the link can use the video, transcript, and chat. Embed permission does not make the link private.",
        ["Bağlantı süresi"] = "Link lifetime", ["Bağlantı süresi içinde sınırsız"] = "No expiry", ["7 gün"] = "7 days", ["30 gün"] = "30 days", ["90 gün"] = "90 days",
        ["İzin verilen siteler"] = "Allowed sites", ["İzinli siteleri seçin"] = "Select allowed sites", ["Siteyi ekleyip açılır listeden seçin."] = "Add a site and select it from the dropdown.", ["tüm sitelerde gömmeye izin verir."] = "allows embedding on all sites.",
        ["Bağlantı ve iframe oluştur"] = "Create link and iframe", ["Paylaşım görünümü"] = "Share appearance", ["Tema"] = "Theme", ["Orbital · koyu neon"] = "Orbital · dark neon", ["Minimal · sade koyu"] = "Minimal · clean dark",
        ["Başlangıç (saniye)"] = "Start (seconds)", ["Bitiş (saniye)"] = "End (seconds)", ["Metin dökümü ve altyazı"] = "Transcript and captions", ["Konu başlıkları"] = "Topic chapters", ["Video sohbeti"] = "Video chat",
        ["Paylaşım bağlantısı"] = "Share link", ["HTML / erişim bilgisi"] = "HTML / access information", ["Iframe kodunu kopyala"] = "Copy iframe code", ["Mevcut paylaşımı kapat"] = "Disable current share",
        ["Yeni bağlantı oluşturmak önceki bağlantıyı iptal eder. Dosyayı indirmiş kişilerin kopyaları geri alınamaz."] = "Creating a new link revokes the previous one. Copies already downloaded by others cannot be revoked.",
        ["KAYNAĞI SESLE DOĞRULAYIN"] = "VERIFY AGAINST THE AUDIO", ["Uyarılar hata kanıtı veya STT güven puanı değildir. AI önerileri otomatik uygulanmaz. Zaman kodları yalnızca açık onayla değiştirilir; ham metin ve son 10 sürüm saklanır."] = "Warnings are not proof of an error or an STT confidence score. AI suggestions are not applied automatically. Time codes change only with explicit approval; the raw transcript and the latest 10 versions are retained.",
        ["Uyarıları yeniden tara"] = "Rescan warnings", ["Yalnızca inceleme gerekenler"] = "Review-required only", ["İncelenecek konuşma"] = "Conversation to review", ["▷ Bölümü dinle"] = "▷ Listen to segment",
        ["AI ile metni incele"] = "Review text with AI", ["Ses parçasını yeniden çözümle"] = "Retranscribe audio segment", ["İkinci STT ile karşılaştır"] = "Compare with secondary STT", ["Ses/metin zamanlarını hizala"] = "Align audio and text timing",
        ["İncelenen bölümün sesi"] = "Audio for the reviewed segment", ["Kural tabanlı uyarı · güven puanı değildir"] = "Rule-based warning · not a confidence score", ["Uyarıyı reddet"] = "Dismiss warning",
        ["İlk ham metni göster"] = "Show original raw transcript", ["İncelenen / düzenlenen metin"] = "Reviewed / edited text", ["Zaman aralığını da düzelt"] = "Also correct the time range",
        ["Sesi dinledim; bu metnin ilgili zaman aralığında söylendiğini doğruladım."] = "I listened to the audio and verified that this text was spoken in the selected time range.", ["Düzeltmeyi onayla ve yeniden analiz et"] = "Confirm correction and reanalyze",
        ["Önceki metin sürümüne dön"] = "Restore a previous transcript version", ["Geri yüklenecek sürüm"] = "Version to restore", ["Seçili sürümü geri yükle"] = "Restore selected version",
        ["Yönetim"] = "Administration", ["Yalnızca yönetici"] = "Administrators only", ["KONTROL SİZDE"] = "YOU ARE IN CONTROL", ["Ses, dil modeli ve indeksleme için bağımsız sağlayıcı seçin."] = "Choose independent providers for speech, language models, and indexing.",
        ["Konuşmayı metne çevirme"] = "Speech to text", ["Video sesini zaman kodlu metne dönüştüren birincil sağlayıcı."] = "The primary provider that converts video audio into a time-coded transcript.", ["Aktif sağlayıcı"] = "Active provider", ["Devre dışı"] = "Disabled",
        ["İkinci STT · karşılaştırma"] = "Secondary STT · comparison", ["Birincil STT’den farklı olmalıdır ve yalnızca karşılaştırma istendiğinde çağrılır."] = "It must differ from the primary STT and is called only when a comparison is requested.",
        ["Yapılandırma gerekli"] = "Configuration required", ["Yerel bağlantı hazır"] = "Local connection ready", ["Ayarları düzenle →"] = "Edit settings →", ["n8n akışını bağla →"] = "Connect n8n workflow →",
        ["Dil modeli"] = "Language model", ["Sohbet, özet, konu başlıkları ve metin kalite analizlerini üretir."] = "Generates chat answers, summaries, topic chapters, and transcript quality analysis.",
        ["Anlamsal indeksleme"] = "Semantic indexing", ["Video metnini aranabilir vektör indeksine dönüştürür."] = "Converts the video transcript into a searchable vector index.", ["Yönlendirme seçimini kaydet"] = "Save routing selection", ["Yeni işler bu seçimleri kullanır; devam eden işler değişmez."] = "New jobs use these selections; jobs already in progress are unchanged.", ["Aktif sağlayıcıları kaydet"] = "Save active providers",
        ["ÇOK DİLLİ KATALOG"] = "MULTILINGUAL CATALOG", ["Arayüz metinlerini, tanımları ve analiz dillerini tek bir katalogdan yönetin."] = "Manage interface text, definitions, and analysis languages from a single catalog.",
        ["Dil paketi"] = "Language pack", ["Dil kodu örneği:"] = "Language code example:", ["Varsayılan dil, ilk kez gelen kullanıcılar için kullanılır."] = "The default language is used for first-time visitors.", ["Kod"] = "Code", ["Kullanıma açık"] = "Enabled", ["Varsayılan"] = "Default", ["Dil paketini kaydet"] = "Save language pack",
        ["Otomasyon ve aktarım"] = "Automation and transfer", ["AI yalnızca boş çevirileri taslak olarak doldurur; mevcut insan çevirilerinin üzerine yazmaz."] = "AI drafts only missing translations and never overwrites existing human translations.", ["JSON indir"] = "Download JSON", ["JSON dil paketi içe aktar"] = "Import JSON language pack", ["Çeviri girin"] = "Enter translation", ["Kaydet"] = "Save", ["Kapat"] = "Close",
        ["Medya hazırlanıyor"] = "Preparing media", ["Konuşmacı adlarını kaydet"] = "Save speaker names", ["Medya yükleniyor…"] = "Uploading media…",
        ["Konuşmacıları ayırarak analiz et"] = "Analyze with speaker separation", ["Analizde bulunan konuşmacı etiketlerine gerçek kişi adlarını yazabilirsiniz. Bu işlem yeniden analiz gerektirmez."] = "You can assign real names or roles to the speaker labels found in the analysis. This does not require reanalysis.",
        ["sn"] = "sec", ["Konuşmacılar"] = "Speakers", ["Yeniden analiz ayarları"] = "Reanalysis settings", ["Analizi yeniden başlat"] = "Restart analysis", ["Medya oynatıcı"] = "Media player",
        ["MP4, MOV, WebM, MP3, WAV, M4A, AAC, FLAC, OGG · 512 MiB · 2 saat"] = "MP4, MOV, WebM, MP3, WAV, M4A, AAC, FLAC, OGG · 512 MiB · 2 hours",
        ["Eşleşen kullanıcı bulunamadı."] = "No matching users found.", ["Hesapları, içerikleri, iş kuyruğunu ve güvenli hata kayıtlarını tek ekrandan izleyin."] = "Monitor accounts, content, the job queue, and sanitized error records from one screen.",
        ["Hesap"] = "Account", ["Aşama"] = "Stage", ["Kullanıcı veya içerik ara"] = "Search users or content", ["Sistem yönetimi"] = "System administration", ["HATA GÖRÜNÜRLÜĞÜ"] = "ERROR VISIBILITY",
        ["Analiz sonucu"] = "Analysis result", ["Yetki"] = "Role", ["Kullanım ayrıntıları"] = "Usage details", ["Medya analizleri"] = "Media analyses", ["Oluşturma"] = "Created",
        ["Tüm durumlar"] = "All statuses", ["Sağlayıcılar"] = "Providers", ["HESAPLAR"] = "ACCOUNTS", ["İçerik"] = "Content", ["Sahibi"] = "Owner", ["İnceleme gereken"] = "Needs review",
        ["İÇERİK VE KUYRUK"] = "CONTENT AND QUEUE", ["Filtrele"] = "Filter", ["Sistem özeti"] = "System overview", ["Toplam kullanıcı"] = "Total users", ["Toplam içerik"] = "Total content",
        ["Gizli veri ve ham exception yerine güvenli hata kodları gösterilir."] = "Sanitized error codes are shown instead of secrets and raw exceptions.", ["Eşleşen içerik bulunamadı."] = "No matching content found.",
        ["Son yükleme"] = "Latest upload", ["E-posta, görünen ad veya video adı"] = "Email, display name, or video title", ["YÖNETİM MERKEZİ"] = "ADMINISTRATION CENTER",
        ["Transkript ve işlem ayrıntılarını incelemek için içerik adına tıklayın."] = "Select a content title to inspect its transcript and processing details.", ["İş durumu"] = "Job status",
        ["Son hata ve belirsiz işlemler"] = "Recent errors and uncertain operations", ["Kayıtlı bir hata bulunmuyor."] = "No recorded errors.", ["Aktif iş yükü"] = "Active workload", ["Temizle"] = "Clear",
        ["Kullanıcılar"] = "Users", ["En fazla 100 eşleşme gösterilir. Parolalar hiçbir zaman görüntülenmez."] = "Up to 100 matches are shown. Passwords are never displayed.", ["YÖNETİCİ İNCELEMESİ"] = "ADMIN REVIEW",
        ["Bu içeriğe ait son 100 sağlayıcı çağrısı."] = "The latest 100 provider calls for this content.", ["KONU BAŞLIKLARI"] = "TOPIC CHAPTERS", ["Süre / maliyet"] = "Duration / cost", ["Süre ve boyut"] = "Duration and size",
        ["Konuşmacı"] = "Speaker", ["tarifesiz"] = "unpriced", ["Bu sayfa kullanıcı transkriptini gösterir. Yalnızca destek ve hata inceleme amacıyla kullanın."] = "This page displays a user's transcript. Use it only for support and troubleshooting.",
        ["İşlem adımları"] = "Processing steps", ["Hassas içerik"] = "Sensitive content", ["Kullanım"] = "Usage", ["Transkript"] = "Transcript", ["← Panele dön"] = "← Back to dashboard",
        ["Kullanım ve hata kayıtları"] = "Usage and error records", ["Analiz"] = "Analysis", ["Henüz işlem adımı kaydı yok."] = "No processing-step records yet.", ["İçerik ayrıntısı"] = "Content details",
        ["İşlem"] = "Operation", ["Bölüm kaydı yok."] = "No chapter records.", ["Bölümler"] = "Chapters", ["Sağlayıcı"] = "Provider", ["Zaman"] = "Time", ["AI kullanım kaydı yok."] = "No AI usage records.",
        ["AI ÇAĞRILARI"] = "AI CALLS", ["İŞ AKIŞI"] = "WORKFLOW", ["Bu içerik için transkript henüz hazır değil."] = "The transcript for this content is not ready yet.",
        ["Yerel konuşmacı ayrımı çalıştırılamadı. Python bağımlılıklarını ve ses kaydını kontrol edin."] = "Local speaker separation could not run. Check the Python dependencies and audio recording.",
        ["Konuşmacı sayısı 2–32 arasında olmalıdır."] = "The number of speakers must be between 2 and 32.", ["Medya dönüştürme zaman sınırına ulaştı."] = "Media conversion timed out.",
        ["Medya süresi 2 saati aşmamalıdır."] = "Media duration must not exceed 2 hours.", ["Dönüştürülen medya 1 GiB sınırını aşıyor. Daha kısa bir kayıt yükleyin."] = "The converted media exceeds the 1 GiB limit. Upload a shorter recording.",
        ["Bu dosya desteklenen bir video veya ses kaydı değil. MP4, MOV, WebM, MP3, WAV, M4A, AAC, FLAC ya da OGG yükleyin."] = "This file is not a supported video or audio recording. Upload MP4, MOV, WebM, MP3, WAV, M4A, AAC, FLAC, or OGG.",
        ["Medya analizi tamamlanamadı. Yeniden deneyebilir veya SRT yükleyebilirsiniz."] = "Media analysis could not be completed. Try again or upload an SRT file.",
        ["Aktif STT sağlayıcısı konuşmacı ayrımını desteklemiyor. ElevenLabs veya uyumlu bir n8n STT akışı seçin."] = "The active STT provider does not support speaker separation. Select ElevenLabs or a compatible n8n STT workflow.",
        ["Analizi bu ayarlarla başlat"] = "Start analysis with these settings", ["Konuşmacılıya çevir"] = "Convert to speaker-separated", ["Konuşmacı ayrımı olmadan tam analiz başlatıldı."] = "Full analysis without speaker separation started.",
        ["Oynatmak için medya oynatıcısındaki başlat düğmesine basın."] = "Press play in the media player to start playback.", ["Analiz tamamlanan metin korunarak kaldığı aşamadan sürdürüldü."] = "Analysis resumed from the last completed stage while preserving the transcript.",
        ["Konuşmacı adları kaydedildi."] = "Speaker names saved.", ["Konuşma parçası"] = "Speech segment", ["Konuşmacı ayrımlı tam analiz başlatıldı."] = "Full speaker-separated analysis started.", ["Tam yeniden analizi başlat"] = "Start full reanalysis",
        ["İşlem tamamlanamadı; ayrıntıyı kontrol edip yeniden deneyin"] = "Processing could not be completed; review the details and try again.", ["Videonuzun içindeki cevaplara,"] = "Find answers inside your video,",
        [". Varsayılan dil, ilk kez gelen kullanıcılar için kullanılır."] = ". The default language is used for first-time visitors.", ["Türkçe metinleri"] = "Turkish texts"
    };

    public async Task EnsureAsync(CancellationToken ct)
    {
        if (ensured) return;
        if (!await db.UiLanguages.AnyAsync(ct))
            db.UiLanguages.AddRange(new UiLanguageEntity { Code = "tr", Name = "Türkçe", IsEnabled = true, IsDefault = true, UpdatedAt = clock.GetUtcNow(), UpdatedBy = "system" },
                new UiLanguageEntity { Code = "en", Name = "English", IsEnabled = true, UpdatedAt = clock.GetUtcNow(), UpdatedBy = "system" });
        var discovered = DiscoverStaticContent().ToArray();
        var definitions = Defaults.Select(x => (x.Key, Text: x.Value, Context: "built-in")).Concat(discovered).ToArray();
        var discoveredKeys = discovered.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var stale = await db.UiContentDefinitions.Where(x => x.Key.StartsWith("static.") && !discoveredKeys.Contains(x.Key)).Select(x => x.Key).ToListAsync(ct);
        if (stale.Count > 0)
        {
            db.UiTranslations.RemoveRange(await db.UiTranslations.Where(x => stale.Contains(x.Key)).ToListAsync(ct));
            db.UiContentDefinitions.RemoveRange(await db.UiContentDefinitions.Where(x => stale.Contains(x.Key)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }
        var existingDefinitions = await db.UiContentDefinitions.Select(x => x.Key).ToListAsync(ct);
        foreach (var definition in definitions.DistinctBy(x => x.Key).Where(x => !existingDefinitions.Contains(x.Key, StringComparer.Ordinal)))
            db.UiContentDefinitions.Add(new UiContentDefinitionEntity { Key = definition.Key, DefaultText = definition.Text, Context = definition.Context });
        await db.SaveChangesAsync(ct);
        var allDefinitions = await db.UiContentDefinitions.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.DefaultText, ct);
        var existing = await db.UiTranslations.Select(x => new { x.LanguageCode, x.Key }).ToListAsync(ct);
        foreach (var item in EnglishDefaults.Where(x => !existing.Any(e => e.LanguageCode == "en" && e.Key == x.Key)))
            db.UiTranslations.Add(new UiTranslationEntity { LanguageCode = "en", Key = item.Key, Text = item.Value, UpdatedAt = clock.GetUtcNow(), UpdatedBy = "system" });
        foreach (var item in allDefinitions.Where(x => !EnglishDefaults.ContainsKey(x.Key) && EnglishStaticDefaults.ContainsKey(x.Value) && !existing.Any(e => e.LanguageCode == "en" && e.Key == x.Key)))
            db.UiTranslations.Add(new UiTranslationEntity { LanguageCode = "en", Key = item.Key, Text = EnglishStaticDefaults[item.Value], UpdatedAt = clock.GetUtcNow(), UpdatedBy = "system" });
        foreach (var item in allDefinitions.Where(x => !existing.Any(e => e.LanguageCode == "tr" && e.Key == x.Key)))
            db.UiTranslations.Add(new UiTranslationEntity { LanguageCode = "tr", Key = item.Key, Text = item.Value, UpdatedAt = clock.GetUtcNow(), UpdatedBy = "system" });
        await db.SaveChangesAsync(ct);
        ensured = true;
    }

    private IEnumerable<(string Key, string Text, string Context)> DiscoverStaticContent()
    {
        var contentRoots = new[]
        {
            environment.ContentRootPath,
            Path.Combine(environment.ContentRootPath, "src", "VideoAtlas.Web")
        }.Where(path => Directory.Exists(Path.Combine(path, "Views"))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var roots = contentRoots.SelectMany(path => new[] { Path.Combine(path, "Views"), Path.Combine(path, "wwwroot", "js") });
        foreach (var root in roots.Where(Directory.Exists))
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(x => x.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(file);
            var view = file.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase);
            var candidates = new List<string>();
            if (view)
            {
                candidates.AddRange(Regex.Matches(source, @">([^<>{}@]+)<").Select(x => x.Groups[1].Value));
                candidates.AddRange(Regex.Matches(source, @"(?:placeholder|aria-label|title)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase).Select(x => x.Groups[1].Value));
            }
            else candidates.AddRange(Regex.Matches(source, @"['""`]([^'""`\r\n]{2,500})['""`]").Select(x => x.Groups[1].Value));
            foreach (var raw in candidates)
            {
                var text = Regex.Replace(WebUtility.HtmlDecode(raw), @"\s+", " ").Trim();
                if (!UserFacing(text, view)) continue;
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..16];
                yield return ($"static.{hash}", text, Path.GetRelativePath(environment.ContentRootPath, file).Replace('\\', '/'));
            }
        }
    }
    private static bool UserFacing(string text, bool view)
    {
        if (text.Length is < 2 or > 500 || text.Contains('@') || text.Contains('{') || text.Contains('}') || text.Contains("=>") || text.StartsWith('/') || text.StartsWith('#') || text.StartsWith('.')) return false;
        if (Regex.IsMatch(text, @"^[,;:]|[:,]\s*$|[$();=\[\]]|\.hidden|\.value|\.textContent|addEventListener|querySelector|dataset|setAttribute|element\(|^new\s", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(text, @"^(btn\s|icon-button\s|inline-edit\s)|^[a-z]+(?:-[a-z]+)+(?:\s+[a-z]+(?:-[a-z]+)+)*$", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(text, @"^[A-Za-z]+(?:Security)?:[A-Za-z]+$")) return false;
        if (view && Regex.IsMatch(text, @"[\p{L}\p{N}]")) return true;
        if (Regex.IsMatch(text, @"[çğıöşüÇĞİÖŞÜ]")) return true;
        if (Regex.IsMatch(text, @"^(Kaydet|Kapat|Sil|Ekle|Vazgeç|Düzenle|Özet|İncele|Dinle|Geri|İleri)$", RegexOptions.IgnoreCase)) return true;
        return text.Contains(' ') && Regex.IsMatch(text, @"[\p{L}]") && !Regex.IsMatch(text, @"^(GET|POST|PUT|DELETE|querySelector|addEventListener|application/|text/)", RegexOptions.IgnoreCase);
    }

    public async Task<LanguageCatalogView> ReadAsync(string? language, CancellationToken ct)
    {
        await EnsureAsync(ct); var languages = await db.UiLanguages.AsNoTracking().OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToListAsync(ct);
        var selected = languages.FirstOrDefault(x => x.Code == language)?.Code ?? languages.First(x => x.IsDefault).Code;
        var translations = await db.UiTranslations.AsNoTracking().Where(x => x.LanguageCode == selected).ToDictionaryAsync(x => x.Key, ct);
        var definitions = await db.UiContentDefinitions.AsNoTracking().OrderBy(x => x.Context).ThenBy(x => x.Key).ToListAsync(ct);
        var result = definitions.Select(x => translations.TryGetValue(x.Key, out var value) ? new TranslationView(x.Key, x.DefaultText, value.Text, value.Version) : new TranslationView(x.Key, x.DefaultText, "", 0)).ToList();
        var counts = await db.UiTranslations.GroupBy(x => x.LanguageCode).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return new(languages.Select(x => new LanguageView(x.Code, x.Name, x.IsEnabled, x.IsDefault, counts.GetValueOrDefault(x.Code), x.Version)).ToList(), result);
    }
    public async Task<IReadOnlyList<LanguageView>> EnabledAsync(CancellationToken ct) => (await ReadAsync(null, ct)).Languages.Where(x => x.IsEnabled).ToList();
    public async Task<string> ResolveLanguageAsync(string? ownerId, string? requestedLanguage, CancellationToken ct)
    {
        await EnsureAsync(ct);
        var preferred = string.IsNullOrWhiteSpace(ownerId) ? null : await db.UserLanguagePreferences.Where(x => x.UserId == ownerId).Select(x => x.LanguageCode).SingleOrDefaultAsync(ct);
        var enabled = await db.UiLanguages.Where(x => x.IsEnabled).OrderByDescending(x => x.IsDefault).Select(x => x.Code).ToListAsync(ct);
        return enabled.Contains(preferred ?? "", StringComparer.Ordinal) ? preferred! : enabled.Contains(requestedLanguage ?? "", StringComparer.Ordinal) ? requestedLanguage! : enabled.First();
    }
    public async Task<bool> SetUserLanguageAsync(string ownerId, string language, CancellationToken ct)
    {
        await EnsureAsync(ct);
        if (!await db.UiLanguages.AnyAsync(x => x.Code == language && x.IsEnabled, ct)) return false;
        var preference = await db.UserLanguagePreferences.SingleOrDefaultAsync(x => x.UserId == ownerId, ct);
        if (preference is null) { preference = new UserLanguagePreferenceEntity { UserId = ownerId }; db.UserLanguagePreferences.Add(preference); }
        preference.LanguageCode = language; preference.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); return true;
    }
    public async Task<string> TextAsync(string key, string? language, CancellationToken ct)
    {
        await EnsureAsync(ct); var code = language ?? "tr";
        var text = await db.UiTranslations.Where(x => x.LanguageCode == code && x.Key == key).Select(x => x.Text).SingleOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(text)) return text;
        return await db.UiContentDefinitions.Where(x => x.Key == key).Select(x => x.DefaultText).SingleOrDefaultAsync(ct) ?? Defaults.GetValueOrDefault(key, key);
    }
    public async Task SaveLanguageAsync(string code, string name, bool enabled, bool isDefault, int version, string actor, CancellationToken ct)
    {
        code = (code ?? "").Trim().ToLowerInvariant(); name = (name ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z]{2,3}(-[a-z]{2,4})?$") || name.Length is < 2 or > 80) throw new AppException(400, "invalid_language", "Dil kodu BCP-47 kısa biçiminde, ad 2–80 karakter olmalıdır.");
        var row = await db.UiLanguages.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null) { row = new UiLanguageEntity { Code = code }; db.UiLanguages.Add(row); }
        else if (row.Version != version) throw new AppException(409, "language_conflict", "Dil kaydı değişti; sayfayı yenileyin.");
        if (isDefault) foreach (var item in await db.UiLanguages.Where(x => x.IsDefault).ToListAsync(ct)) item.IsDefault = false;
        row.Name = name; row.IsEnabled = enabled; row.IsDefault = isDefault; row.Version++; row.UpdatedAt = clock.GetUtcNow(); row.UpdatedBy = actor;
        await db.SaveChangesAsync(ct);
    }
    public async Task SaveTranslationAsync(string language, string key, string text, int version, string actor, CancellationToken ct)
    {
        if (!await db.UiContentDefinitions.AnyAsync(x => x.Key == key, ct) || string.IsNullOrWhiteSpace(text) || text.Length > 2000) throw new AppException(400, "invalid_translation", "Tanım anahtarı veya metin geçersiz.");
        var row = await db.UiTranslations.SingleOrDefaultAsync(x => x.LanguageCode == language && x.Key == key, ct);
        if (row is null) { row = new UiTranslationEntity { LanguageCode = language, Key = key }; db.UiTranslations.Add(row); }
        else if (row.Version != version) throw new AppException(409, "translation_conflict", "Metin değişti; sayfayı yenileyin.");
        row.Text = text.Trim(); row.Version++; row.UpdatedAt = clock.GetUtcNow(); row.UpdatedBy = actor; await db.SaveChangesAsync(ct);
    }
    public async Task<string> ExportAsync(string language, CancellationToken ct)
    {
        var model = await ReadAsync(language, ct);
        var l = model.Languages.Single(x => x.Code == language);
        return JsonSerializer.Serialize(new { code = l.Code, name = l.Name, enabled = l.IsEnabled, isDefault = l.IsDefault, translations = model.Translations.ToDictionary(x => x.Key, x => string.IsNullOrWhiteSpace(x.Text) ? x.DefaultText : x.Text) }, new JsonSerializerOptions { WriteIndented = true });
    }
    public async Task ImportAsync(string payload, string actor, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payload); var root = document.RootElement;
        var code = root.GetProperty("code").GetString() ?? ""; var name = root.GetProperty("name").GetString() ?? code;
        var old = await db.UiLanguages.SingleOrDefaultAsync(x => x.Code == code, ct);
        await SaveLanguageAsync(code, name, root.TryGetProperty("enabled", out var enabled) && enabled.GetBoolean(), root.TryGetProperty("isDefault", out var primary) && primary.GetBoolean(), old?.Version ?? 0, actor, ct);
        if (!root.TryGetProperty("translations", out var map) || map.ValueKind != JsonValueKind.Object) return;
        var validKeys = await db.UiContentDefinitions.Select(x => x.Key).ToListAsync(ct);
        foreach (var property in map.EnumerateObject().Where(x => validKeys.Contains(x.Name, StringComparer.Ordinal)))
        {
            var current = await db.UiTranslations.SingleOrDefaultAsync(x => x.LanguageCode == code && x.Key == property.Name, ct);
            await SaveTranslationAsync(code, property.Name, property.Value.GetString() ?? "", current?.Version ?? 0, actor, ct);
        }
    }
    public async Task<int> FillWithAiAsync(string language, string actor, CancellationToken ct)
    {
        var binding = await providers.ChatAsync(ct) ?? throw new AppException(409, "ai_not_configured", "AI çevirisi için etkin bir sohbet sağlayıcısı seçin.");
        var catalog = await ReadAsync(language, ct); var missing = catalog.Translations.Where(x => string.IsNullOrWhiteSpace(x.Text)).ToArray(); if (missing.Length == 0) return 0;
        var validKeys = await db.UiContentDefinitions.Select(x => x.Key).ToListAsync(ct);
        var count = 0;
        foreach (var batch in missing.Chunk(40))
        {
            var source = batch.ToDictionary(x => x.Key, x => x.DefaultText);
            var json = await binding.Adapter.CompleteJsonAsync(binding.Connection, $"Translate Turkish UI strings into language code {language}. Return only one JSON object using exactly the supplied keys. Preserve placeholders, product name VideoAtlas, technical model/provider names, symbols and punctuation. Use concise natural interface language; no explanations.", JsonSerializer.Serialize(source), ct);
            using var result = JsonDocument.Parse(json);
            foreach (var property in result.RootElement.EnumerateObject().Where(x => validKeys.Contains(x.Name, StringComparer.Ordinal) && x.Value.ValueKind == JsonValueKind.String))
            {
                var row = await db.UiTranslations.SingleOrDefaultAsync(x => x.LanguageCode == language && x.Key == property.Name, ct);
                await SaveTranslationAsync(language, property.Name, property.Value.GetString()!, row?.Version ?? 0, actor, ct); count++;
            }
        }
        return count;
    }
    public async Task<IReadOnlyDictionary<string, string>> ClientTextsAsync(string language, CancellationToken ct)
    {
        await EnsureAsync(ct);
        var query = from definition in db.UiContentDefinitions.AsNoTracking()
                    join translation in db.UiTranslations.AsNoTracking().Where(x => x.LanguageCode == language) on definition.Key equals translation.Key into values
                    from translation in values.DefaultIfEmpty()
                    select new { definition.DefaultText, Text = translation == null ? definition.DefaultText : translation.Text, HasTranslation = translation != null };
        var rows = await query.ToListAsync(ct);
        var result = rows.GroupBy(x => x.DefaultText).Select(x => x.OrderByDescending(y => y.HasTranslation).First()).ToDictionary(x => x.DefaultText, x => x.Text);
        if (language == "en")
            foreach (var item in EnglishStaticDefaults.Where(x => !result.ContainsKey(x.Key) || result[x.Key] == x.Key)) result[item.Key] = item.Value;
        return result;
    }
}
