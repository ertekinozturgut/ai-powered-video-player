using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace VideoAtlas.Infrastructure;

public static class ExternalApiScopes
{
    public const string Read = "analysis:read";
    public const string Write = "analysis:write";
    public const string Delete = "analysis:delete";
    public static readonly string[] All = [Read, Write, Delete];
}

public sealed class ApiCredentialEntity
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string SecretHash { get; set; } = "";
    public string Scopes { get; set; } = ExternalApiScopes.Read + " " + ExternalApiScopes.Write;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class ApiRequestLogEntity
{
    public long Id { get; set; }
    public Guid? CredentialId { get; set; }
    public string? OwnerId { get; set; }
    public string? CredentialPrefix { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public long? RequestBytes { get; set; }
    public long? ResponseBytes { get; set; }
    public string RemoteAddressHash { get; set; } = "";
    public string TraceId { get; set; } = "";
    public string? ErrorCode { get; set; }
    public Guid? VideoId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record CreatedApiCredential(ApiCredentialEntity Credential, string Secret);

public sealed class ApiCredentialService(AppDbContext db, TimeProvider clock)
{
    public async Task<CreatedApiCredential> CreateAsync(string ownerId, string name, IEnumerable<string> scopes, int expiresInDays, CancellationToken ct)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80) throw new ArgumentException("Credential name must be 1-80 characters.", nameof(name));
        if (expiresInDays is < 0 or > 365) throw new ArgumentOutOfRangeException(nameof(expiresInDays));
        if (await db.ApiCredentials.CountAsync(x => x.OwnerId == ownerId && x.RevokedAt == null, ct) >= 10)
            throw new InvalidOperationException("At most 10 active API credentials are allowed.");
        var allowedScopes = scopes.Distinct(StringComparer.Ordinal).Where(ExternalApiScopes.All.Contains).ToArray();
        if (allowedScopes.Length == 0) throw new ArgumentException("At least one valid scope is required.", nameof(scopes));
        var prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var secret = $"vat_{prefix}.{Base64Url(RandomNumberGenerator.GetBytes(32))}";
        var now = clock.GetUtcNow();
        var entity = new ApiCredentialEntity
        {
            Id = Guid.NewGuid(), OwnerId = ownerId, Name = name, Prefix = prefix,
            SecretHash = Hash(secret), Scopes = string.Join(' ', allowedScopes), CreatedAt = now,
            ExpiresAt = expiresInDays == 0 ? null : now.AddDays(expiresInDays)
        };
        db.ApiCredentials.Add(entity);
        await db.SaveChangesAsync(ct);
        return new(entity, secret);
    }

    public async Task<ApiCredentialEntity?> ValidateAsync(string token, CancellationToken ct)
    {
        if (!TryPrefix(token, out var prefix)) return null;
        var credential = await db.ApiCredentials.SingleOrDefaultAsync(x => x.Prefix == prefix, ct);
        if (credential is null || credential.RevokedAt is not null || credential.ExpiresAt <= clock.GetUtcNow()) return null;
        var expected = Convert.FromHexString(credential.SecretHash);
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;
        if (credential.LastUsedAt is null || credential.LastUsedAt < clock.GetUtcNow().AddMinutes(-1))
        {
            credential.LastUsedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        return credential;
    }

    public static bool TryPrefix(string token, out string prefix)
    {
        prefix = "";
        if (!token.StartsWith("vat_", StringComparison.Ordinal) || token.Length > 128) return false;
        var dot = token.IndexOf('.');
        if (dot != 20) return false;
        prefix = token[4..dot];
        return prefix.Length == 16 && prefix.All(char.IsAsciiHexDigit);
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
