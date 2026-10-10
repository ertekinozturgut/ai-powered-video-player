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
        ["nav.operations"] = "İşlemler ve kullanım", ["nav.providers"] = "AI sağlayıcıları", ["nav.account"] = "Çalışma hesabı",
        ["nav.password"] = "Parolamı değiştir", ["nav.logout"] = "Güvenli çıkış", ["upload.eyebrow"] = "YENİ BİR KEŞİF",
        ["upload.title"] = "Video yükle", ["upload.file"] = "Videonuzu buraya bırakın", ["upload.fileHelp"] = "veya dosya seçmek için tıklayın",
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
        ["nav.operations"] = "Operations and usage", ["nav.providers"] = "AI providers", ["nav.account"] = "Work account",
        ["nav.password"] = "Change password", ["nav.logout"] = "Sign out securely", ["upload.eyebrow"] = "A NEW DISCOVERY",
        ["upload.title"] = "Upload video", ["upload.file"] = "Drop your video here", ["upload.fileHelp"] = "or click to choose a file",
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
        foreach (var item in allDefinitions.Where(x => !existing.Any(e => e.LanguageCode == "tr" && e.Key == x.Key)))
            db.UiTranslations.Add(new UiTranslationEntity { LanguageCode = "tr", Key = item.Key, Text = item.Value, UpdatedAt = clock.GetUtcNow(), UpdatedBy = "system" });
        await db.SaveChangesAsync(ct);
        ensured = true;
    }

    private IEnumerable<(string Key, string Text, string Context)> DiscoverStaticContent()
    {
        var roots = new[] { Path.Combine(environment.ContentRootPath, "Views"), Path.Combine(environment.ContentRootPath, "wwwroot", "js") };
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
                    select new { definition.DefaultText, Text = translation == null ? definition.DefaultText : translation.Text };
        return await query.GroupBy(x => x.DefaultText).Select(x => x.First()).ToDictionaryAsync(x => x.DefaultText, x => x.Text, ct);
    }
}
