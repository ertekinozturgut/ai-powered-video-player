using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<ProviderProfileEntity> ProviderProfiles => Set<ProviderProfileEntity>();
    public DbSet<ProviderSelectionEntity> ProviderSelections => Set<ProviderSelectionEntity>();
    public DbSet<AiUsageEntry> AiUsage => Set<AiUsageEntry>();
    public DbSet<UiLanguageEntity> UiLanguages => Set<UiLanguageEntity>();
    public DbSet<UiTranslationEntity> UiTranslations => Set<UiTranslationEntity>();
    public DbSet<UserLanguagePreferenceEntity> UserLanguagePreferences => Set<UserLanguagePreferenceEntity>();
    public DbSet<UiContentDefinitionEntity> UiContentDefinitions => Set<UiContentDefinitionEntity>();
    public DbSet<ApiCredentialEntity> ApiCredentials => Set<ApiCredentialEntity>();
    public DbSet<ApiRequestLogEntity> ApiRequestLogs => Set<ApiRequestLogEntity>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AiUsageEntry>().HasKey(u => u.Id);
        builder.Entity<AiUsageEntry>().HasIndex(u => new { u.OwnerId, u.CreatedAt });
        // Keep content-free metering briefly after deletion; deleting videos must not reset daily budgets.
        builder.Entity<AiUsageEntry>().HasIndex(u => u.VideoId);
        builder.Entity<AiUsageEntry>().Property(u => u.CreatedAt).HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        builder.Entity<ProviderProfileEntity>().HasKey(p => p.Id);
        builder.Entity<ProviderProfileEntity>().Property(p => p.Version).IsConcurrencyToken();
        builder.Entity<ProviderSelectionEntity>().HasKey(p => p.Id);
        builder.Entity<ProviderSelectionEntity>().Property(p => p.Version).IsConcurrencyToken();
        builder.Entity<ProviderSelectionEntity>().Property(p => p.UpdatedAt).HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        builder.Entity<UiLanguageEntity>().HasKey(x => x.Code);
        builder.Entity<UiLanguageEntity>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<UiLanguageEntity>().Property(x => x.UpdatedAt).HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        builder.Entity<UiTranslationEntity>().HasKey(x => new { x.LanguageCode, x.Key });
        builder.Entity<UiTranslationEntity>().Property(x => x.Version).IsConcurrencyToken();
        builder.Entity<UiTranslationEntity>().Property(x => x.UpdatedAt).HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        builder.Entity<UserLanguagePreferenceEntity>().HasKey(x => x.UserId);
        builder.Entity<UserLanguagePreferenceEntity>().Property(x => x.UpdatedAt).HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        builder.Entity<UiContentDefinitionEntity>().HasKey(x => x.Key);
        var credential = builder.Entity<ApiCredentialEntity>();
        credential.HasKey(x => x.Id);
        credential.HasIndex(x => x.Prefix).IsUnique();
        credential.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        credential.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        credential.Property(x => x.Name).HasMaxLength(80);
        credential.Property(x => x.Prefix).HasMaxLength(16);
        credential.Property(x => x.SecretHash).HasMaxLength(64);
        credential.Property(x => x.Scopes).HasMaxLength(100);
        credential.Property(x => x.CreatedAt).HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        credential.Property(x => x.ExpiresAt).HasConversion(v => v.HasValue ? v.Value.ToUnixTimeMilliseconds() : (long?)null, v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : null);
        credential.Property(x => x.LastUsedAt).HasConversion(v => v.HasValue ? v.Value.ToUnixTimeMilliseconds() : (long?)null, v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : null);
        credential.Property(x => x.RevokedAt).HasConversion(v => v.HasValue ? v.Value.ToUnixTimeMilliseconds() : (long?)null, v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : null);
        var apiLog = builder.Entity<ApiRequestLogEntity>();
        apiLog.HasKey(x => x.Id);
        apiLog.HasIndex(x => new { x.OwnerId, x.CreatedAt });
        apiLog.HasIndex(x => new { x.CredentialId, x.CreatedAt });
        apiLog.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.SetNull);
        apiLog.Property(x => x.Method).HasMaxLength(12);
        apiLog.Property(x => x.Path).HasMaxLength(500);
        apiLog.Property(x => x.CredentialPrefix).HasMaxLength(16);
        apiLog.Property(x => x.RemoteAddressHash).HasMaxLength(16);
        apiLog.Property(x => x.TraceId).HasMaxLength(100);
        apiLog.Property(x => x.ErrorCode).HasMaxLength(100);
        apiLog.Property(x => x.CreatedAt).HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        var video = builder.Entity<Video>();
        video.HasKey(v => v.Id);
        video.HasIndex(v => v.OwnerId);
        video.HasIndex(v => v.ShareHash).IsUnique();
        video.HasIndex(v => v.Status);
        video.Property(v => v.Title).HasMaxLength(180);
        video.Property(v => v.MediaKind).HasMaxLength(12);
        video.Property(v => v.CreatedVia).HasMaxLength(12);
        video.HasIndex(v => new { v.OwnerId, v.CreatedVia, v.CreatedAt });
        video.Property(v => v.CreatedAt).HasConversion(v => v.ToUnixTimeMilliseconds(), v => DateTimeOffset.FromUnixTimeMilliseconds(v));
        video.Ignore(v => v.IsBusy);
        video.Ignore(v => v.Context);
        video.Ignore(v => v.Presentation);
        video.Property(v => v.DeleteAfter).HasConversion(v => v.HasValue ? v.Value.ToUnixTimeMilliseconds() : (long?)null,
            v => v.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(v.Value) : (DateTimeOffset?)null);
        video.Property(v => v.ProcessingSteps).HasConversion(v => Serialize(v), v => Deserialize<ProcessingStep>(v))
            .Metadata.SetValueComparer(Comparer<ProcessingStep>());
        video.Property(v => v.TranscriptChunks).HasConversion(v => Serialize(v), v => Deserialize<TranscriptChunk>(v))
            .Metadata.SetValueComparer(Comparer<TranscriptChunk>());
        video.Property(v => v.ShareViewers).HasConversion(v => Serialize(v), v => Deserialize<string>(v))
            .Metadata.SetValueComparer(Comparer<string>());
        video.Property(v => v.Terms).HasConversion(v => Serialize(v), v => Deserialize<VideoTerm>(v))
            .Metadata.SetValueComparer(Comparer<VideoTerm>());
        video.Property(v => v.SpeakerNames).HasConversion(v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v) ?? new())
            .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>>(
                (a, b) => a != null && b != null && a.OrderBy(x => x.Key).SequenceEqual(b.OrderBy(x => x.Key)),
                value => value.OrderBy(x => x.Key).Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                value => value.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
        video.Property(v => v.OriginalSegments).HasConversion(v => Serialize(v), v => Deserialize<TranscriptSegment>(v))
            .Metadata.SetValueComparer(Comparer<TranscriptSegment>());
        video.Property(v => v.TranscriptHistory).HasConversion(v => Serialize(v), v => Deserialize<TranscriptRevision>(v))
            .Metadata.SetValueComparer(Comparer<TranscriptRevision>());
        video.Property(v => v.TranscriptIssues).HasConversion(v => Serialize(v), v => Deserialize<TranscriptIssue>(v))
            .Metadata.SetValueComparer(Comparer<TranscriptIssue>());
        video.Property(v => v.Segments).HasConversion(v => Serialize(v), v => Deserialize<TranscriptSegment>(v))
            .Metadata.SetValueComparer(Comparer<TranscriptSegment>());
        video.Property(v => v.Chapters).HasConversion(v => Serialize(v), v => Deserialize<Chapter>(v))
            .Metadata.SetValueComparer(Comparer<Chapter>());
        video.Property(v => v.Vectors).HasConversion(v => Serialize(v), v => Deserialize<TranscriptVector>(v))
            .Metadata.SetValueComparer(Comparer<TranscriptVector>());
    }
    private static string Serialize<T>(List<T> value) => JsonSerializer.Serialize(value);
    private static List<T> Deserialize<T>(string value) => JsonSerializer.Deserialize<List<T>>(value) ?? [];
    private static ValueComparer<List<T>> Comparer<T>() => new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        value => value.Aggregate(0, (a, b) => HashCode.Combine(a, b)), value => value.ToList());
}
