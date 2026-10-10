namespace VideoAtlas.Application;

public sealed record LanguageView(string Code, string Name, bool IsEnabled, bool IsDefault, int TranslationCount, int Version);
public sealed record TranslationView(string Key, string DefaultText, string Text, int Version);
public sealed record LanguageCatalogView(IReadOnlyList<LanguageView> Languages, IReadOnlyList<TranslationView> Translations);
public interface ILocalizationAdministration
{
    Task<LanguageCatalogView> ReadAsync(string? language, CancellationToken ct);
    Task SaveLanguageAsync(string code, string name, bool enabled, bool isDefault, int version, string actor, CancellationToken ct);
    Task SaveTranslationAsync(string language, string key, string text, int version, string actor, CancellationToken ct);
    Task<string> ExportAsync(string language, CancellationToken ct);
    Task ImportAsync(string payload, string actor, CancellationToken ct);
    Task<int> FillWithAiAsync(string language, string actor, CancellationToken ct);
    Task<IReadOnlyList<LanguageView>> EnabledAsync(CancellationToken ct);
    Task<string> TextAsync(string key, string? language, CancellationToken ct);
    Task<string> ResolveLanguageAsync(string? ownerId, string? requestedLanguage, CancellationToken ct);
    Task<bool> SetUserLanguageAsync(string ownerId, string language, CancellationToken ct);
    Task<IReadOnlyDictionary<string, string>> ClientTextsAsync(string language, CancellationToken ct);
}
