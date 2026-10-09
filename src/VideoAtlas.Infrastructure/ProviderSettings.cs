using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoAtlas.Application;

namespace VideoAtlas.Infrastructure;

public sealed class ProviderProfileEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ProviderCapability Capability { get; set; }
    public string Kind { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public int Dimensions { get; set; } = 512;
    [VideoAtlas.Domain.SensitiveData] public string? ProtectedSecret { get; set; }
    public int Version { get; set; }
}
public sealed class ProviderSelectionEntity
{
    public int Id { get; set; } = 1;
    public string? Speech { get; set; }
    public string? Chat { get; set; }
    public string? Embedding { get; set; }
    public string? SecondarySpeech { get; set; }
    public int Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "system";
}
public sealed class ProviderSecurityOptions
{
    public string[] AllowedHosts { get; set; } = ["api.openai.com", "openrouter.ai", "api.elevenlabs.io"];
}
public sealed class ProviderCredentialOptions
{
    public string OpenRouter { get; set; } = "";
    public string ElevenLabs { get; set; } = "";
}
public sealed class ProviderEndpointPolicy(IOptions<ProviderSecurityOptions> options)
{
    public void Validate(string endpoint, string kind)
    {
        if (kind == "ollama")
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var local) || local.Scheme != "http" ||
                !local.IsLoopback || local.Port != 11434 || local.UserInfo.Length > 0 || local.Query.Length > 0 ||
                local.Fragment.Length > 0 || !endpoint.EndsWith("/", StringComparison.Ordinal))
                throw new AppException(400, "provider_endpoint", "Yerel Ollama yalnızca http://127.0.0.1:11434/ üzerinde çalışabilir.");
            return;
        }
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            !uri.IsDefaultPort || !options.Value.AllowedHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
            throw new AppException(400, "provider_endpoint", "HTTPS uç noktası sunucunun izinli host listesinde olmalı; port, kullanıcı bilgisi, sorgu ve fragment içermemelidir.");
        var expected = kind switch { "openai" => "api.openai.com", "openrouter" => "openrouter.ai", "elevenlabs" => "api.elevenlabs.io", _ => null };
        if (expected is not null && !uri.IdnHost.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new AppException(400, "provider_endpoint", "Bu adaptör yalnızca kendi sağlayıcısının host adresini kullanabilir.");
        if (kind != "n8n" && !endpoint.EndsWith("/", StringComparison.Ordinal))
            throw new AppException(400, "provider_endpoint", "API taban adresi / ile bitmelidir.");
    }
}

public sealed class ProviderSettingsStore(AppDbContext db, IDataProtectionProvider protection,
    ProviderEndpointPolicy endpoints, IOptions<ProviderSecurityOptions> security, TimeProvider clock,
    IOptions<ProviderCredentialOptions> credentials, IOptions<AiOptions> legacyCredentials) : IProviderAdministration
{
    private IDataProtector Protector(string id) => protection.CreateProtector("VideoAtlas.ProviderSecrets.v1", id);
    public async Task InitializeAsync(AiOptions legacy, CancellationToken ct)
    {
        var profiles = new[]
        {
            Profile("speech-elevenlabs", "ElevenLabs Scribe", ProviderCapability.Speech, "elevenlabs", "https://api.elevenlabs.io/v1/", "scribe_v1"),
            Profile("speech-openai", "OpenAI Whisper", ProviderCapability.Speech, "openai", "https://api.openai.com/v1/", legacy.TranscriptionModel),
            Profile("speech-azure", "Azure Speech Fast Transcription", ProviderCapability.Speech, "azure-speech", "", "tr-TR"),
            Profile("speech-n8n", "n8n STT akışı", ProviderCapability.Speech, "n8n", "", "speech-workflow-v1"),
            Profile("chat-openrouter", "OpenRouter LLM gateway", ProviderCapability.Chat, "openrouter", "https://openrouter.ai/api/v1/", "openai/gpt-4.1-mini"),
            Profile("chat-openai", "OpenAI", ProviderCapability.Chat, "openai", "https://api.openai.com/v1/", legacy.ChatModel),
            Profile("chat-foundry", "Azure AI Foundry / OpenAI v1", ProviderCapability.Chat, "azure-foundry", "", "deployment-name"),
            Profile("chat-n8n", "n8n LLM akışı", ProviderCapability.Chat, "n8n", "", "chat-workflow-v1"),
            Profile("chat-ollama", "Yerel Ollama LLM", ProviderCapability.Chat, "ollama", "http://127.0.0.1:11434/", "qwen2.5:14b-instruct"),
            Profile("embedding-openrouter", "OpenRouter embeddings", ProviderCapability.Embedding, "openrouter", "https://openrouter.ai/api/v1/", "openai/text-embedding-3-small"),
            Profile("embedding-openai", "OpenAI embeddings", ProviderCapability.Embedding, "openai", "https://api.openai.com/v1/", legacy.EmbeddingModel),
            Profile("embedding-foundry", "Azure AI Foundry embeddings", ProviderCapability.Embedding, "azure-foundry", "", "embedding-deployment"),
            Profile("embedding-n8n", "n8n embedding akışı", ProviderCapability.Embedding, "n8n", "", "embedding-workflow-v1"),
            Profile("embedding-ollama", "Yerel Ollama embedding", ProviderCapability.Embedding, "ollama", "http://127.0.0.1:11434/", "nomic-embed-text:latest", 768)
        };
        // Environment/proxy bindings must remain runtime references, not copied into the database.
        var existing = await db.ProviderProfiles.Select(p => p.Id).ToListAsync(ct);
        db.ProviderProfiles.AddRange(profiles.Where(p => !existing.Contains(p.Id, StringComparer.Ordinal)));
        if (await db.ProviderSelections.AnyAsync(ct)) { await db.SaveChangesAsync(ct); return; }
        var enabled = !string.IsNullOrWhiteSpace(legacy.ApiKey);
        db.ProviderSelections.Add(new() { Speech = !string.IsNullOrWhiteSpace(credentials.Value.ElevenLabs) ? "speech-elevenlabs" : enabled ? "speech-openai" : null,
            Chat = !string.IsNullOrWhiteSpace(credentials.Value.OpenRouter) ? "chat-openrouter" : enabled ? "chat-openai" : null,
            Embedding = !string.IsNullOrWhiteSpace(credentials.Value.OpenRouter) ? "embedding-openrouter" : enabled ? "embedding-openai" : null, UpdatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
    }
    private static ProviderProfileEntity Profile(string id, string name, ProviderCapability capability, string kind, string endpoint, string model, int dimensions = 512) =>
        new() { Id = id, Name = name, Capability = capability, Kind = kind, Endpoint = endpoint, Model = model, Dimensions = dimensions };
    public async Task<ProviderAdminView> ReadAsync(CancellationToken ct)
    {
        var profiles = await db.ProviderProfiles.AsNoTracking().OrderBy(p => p.Capability).ThenBy(p => p.Name).ToListAsync(ct);
        var selection = await db.ProviderSelections.AsNoTracking().SingleAsync(ct);
        return new(profiles.Select(p => new ProviderProfileView(p.Id, p.Name, p.Capability, p.Kind, p.Endpoint, p.Model,
            p.Dimensions, p.ProtectedSecret is not null || EnvironmentSecret(p).Length > 0, p.Version,
            p.ProtectedSecret is null && EnvironmentSecret(p).Length > 0)).ToArray(),
            new(selection.Speech, selection.Chat, selection.Embedding, selection.Version, selection.UpdatedAt, selection.UpdatedBy, selection.SecondarySpeech), security.Value.AllowedHosts);
    }
    public async Task SaveAsync(SaveProviderCommand command, string actor, CancellationToken ct)
    {
        var p = await db.ProviderProfiles.SingleOrDefaultAsync(p => p.Id == command.Id, ct)
            ?? throw new AppException(404, "provider_missing", "Sağlayıcı bulunamadı.");
        if (p.Version != command.Version) throw Conflict();
        var endpoint = command.Endpoint.Trim();
        endpoints.Validate(endpoint, p.Kind);
        if (string.IsNullOrWhiteSpace(command.Model) || command.Model.Length > 200 || command.Model.Any(char.IsControl) ||
            command.Dimensions is < 128 or > 3072 || command.NewSecret?.Length > 4096 ||
            command.NewSecret?.Any(char.IsControl) == true || (command.RemoveSecret && !string.IsNullOrEmpty(command.NewSecret)))
            throw new AppException(400, "provider_invalid", "Model, vektör boyutu veya anahtar geçersiz.");
        if (p.ProtectedSecret is not null && p.Endpoint.Length > 0 && new Uri(p.Endpoint).Authority != new Uri(endpoint).Authority &&
            string.IsNullOrWhiteSpace(command.NewSecret) && !command.RemoveSecret)
            throw new AppException(400, "provider_host_change", "Host değişikliğinde yeni sağlayıcının anahtarını yeniden girin.");
        var selection = await db.ProviderSelections.SingleAsync(ct);
        if (command.RemoveSecret && new[] { selection.Speech, selection.Chat, selection.Embedding, selection.SecondarySpeech }.Contains(p.Id))
            throw new AppException(409, "provider_active", "Anahtarı kaldırmadan önce bu sağlayıcıyı devre dışı bırakın.");
        p.Endpoint = endpoint; p.Model = command.Model.Trim(); p.Dimensions = command.Dimensions;
        if (command.RemoveSecret) p.ProtectedSecret = null;
        else if (!string.IsNullOrWhiteSpace(command.NewSecret)) p.ProtectedSecret = Protector(p.Id).Protect(command.NewSecret);
        p.Version++;
        selection.Version++; selection.UpdatedAt = clock.GetUtcNow(); selection.UpdatedBy = actor;
        await SaveChangesAsync(ct);
    }
    public async Task SelectAsync(SelectProvidersCommand command, string actor, CancellationToken ct)
    {
        // A transaction serializes selection validation with concurrent profile edits.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var selection = await db.ProviderSelections.SingleAsync(ct);
        if (selection.Version != command.Version) throw Conflict();
        if (!string.IsNullOrEmpty(command.SecondarySpeech) && command.SecondarySpeech == command.Speech)
            throw new AppException(400, "secondary_provider", "Karşılaştırma için ana STT'den farklı bir profil seçin.");
        foreach (var (id, capability) in new[] { (command.Speech, ProviderCapability.Speech), (command.Chat, ProviderCapability.Chat), (command.Embedding, ProviderCapability.Embedding), (command.SecondarySpeech, ProviderCapability.Speech) })
        {
            if (string.IsNullOrEmpty(id)) continue;
            var profile = await db.ProviderProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.Capability == capability, ct)
                ?? throw new AppException(400, "provider_capability", "Seçilen sağlayıcı bu işlem türüne uygun değil.");
            _ = Connection(profile);
        }
        selection.Speech = NullIfEmpty(command.Speech); selection.Chat = NullIfEmpty(command.Chat); selection.Embedding = NullIfEmpty(command.Embedding);
        selection.SecondarySpeech = NullIfEmpty(command.SecondarySpeech);
        selection.Version++; selection.UpdatedAt = clock.GetUtcNow(); selection.UpdatedBy = actor;
        await SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private async Task SaveChangesAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Conflict(); }
    }
    private static AppException Conflict() => new(409, "provider_conflict", "Ayarlar başka bir işlemde değişti. Sayfayı yenileyip tekrar deneyin.");
    internal ProviderConnection Connection(ProviderProfileEntity profile)
    {
        endpoints.Validate(profile.Endpoint, profile.Kind);
        if (profile.Kind == "ollama") return new(profile.Id, profile.Kind, profile.Endpoint, profile.Model, profile.Dimensions, "");
        if (profile.ProtectedSecret is null)
        {
            var secret = EnvironmentSecret(profile);
            if (string.IsNullOrWhiteSpace(secret)) throw new AppException(400, "provider_secret_missing", "Önce seçilen sağlayıcının API anahtarını kaydedin.");
            return new(profile.Id, profile.Kind, profile.Endpoint, profile.Model, profile.Dimensions, secret);
        }
        try { return new(profile.Id, profile.Kind, profile.Endpoint, profile.Model, profile.Dimensions, Protector(profile.Id).Unprotect(profile.ProtectedSecret)); }
        catch (CryptographicException) { throw new AiException("provider_secret_unavailable"); }
    }
    private string EnvironmentSecret(ProviderProfileEntity profile) => profile.Kind switch
    {
        "openrouter" => credentials.Value.OpenRouter, "elevenlabs" => credentials.Value.ElevenLabs,
        "openai" => legacyCredentials.Value.ApiKey, _ => ""
    };
    internal async Task<IReadOnlyDictionary<ProviderCapability, ProviderProfileEntity>> SnapshotAsync(CancellationToken ct)
    {
        // One database snapshot across all three routes; no mixed model during a job.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var selection = await db.ProviderSelections.AsNoTracking().SingleAsync(ct);
        var routes = new[] { (selection.Speech, ProviderCapability.Speech), (selection.Chat, ProviderCapability.Chat), (selection.Embedding, ProviderCapability.Embedding), (selection.SecondarySpeech, ProviderCapability.SecondarySpeech) };
        var ids = routes.Select(r => r.Item1).ToArray();
        var profiles = await db.ProviderProfiles.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var result = routes.Where(r => r.Item1 is not null && profiles.Any(p => p.Id == r.Item1))
            .ToDictionary(r => r.Item2, r => profiles.Single(p => p.Id == r.Item1));
        await transaction.CommitAsync(ct);
        return result;
    }
}

public sealed class AiProviderFactory(ProviderSettingsStore store, IEnumerable<ISpeechToTextAdapter> speech,
    IEnumerable<ILlmAdapter> chat, IEnumerable<IEmbeddingAdapter> embedding) : IAiProviderFactory
{
    private Task<IReadOnlyDictionary<ProviderCapability, ProviderProfileEntity>>? snapshot;
    private async Task<ProviderBinding<T>?> ResolveAsync<T>(ProviderCapability capability, IEnumerable<T> adapters, Func<T, string> kind, CancellationToken ct)
    {
        var settings = await (snapshot ??= store.SnapshotAsync(ct));
        if (!settings.TryGetValue(capability, out var profile)) return null;
        ProviderConnection connection;
        try { connection = store.Connection(profile); }
        catch (AppException) { throw new AiException("ai_configuration"); }
        var adapter = adapters.SingleOrDefault(a => kind(a) == connection.Kind)
            ?? throw new AiException("provider_adapter_missing");
        return new(connection, adapter);
    }
    public Task<ProviderBinding<ISpeechToTextAdapter>?> SpeechAsync(CancellationToken ct) => ResolveAsync(ProviderCapability.Speech, speech, a => a.Kind, ct);
    public Task<ProviderBinding<ISpeechToTextAdapter>?> SecondarySpeechAsync(CancellationToken ct) => ResolveAsync(ProviderCapability.SecondarySpeech, speech, a => a.Kind, ct);
    public Task<ProviderBinding<ILlmAdapter>?> ChatAsync(CancellationToken ct) => ResolveAsync(ProviderCapability.Chat, chat, a => a.Kind, ct);
    public Task<ProviderBinding<IEmbeddingAdapter>?> EmbeddingAsync(CancellationToken ct) => ResolveAsync(ProviderCapability.Embedding, embedding, a => a.Kind, ct);
}
