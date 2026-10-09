using VideoAtlas.Domain;

namespace VideoAtlas.Application;

public enum ProviderCapability { Speech, Chat, Embedding, SecondarySpeech }

// These DTOs deliberately never carry a stored/decrypted credential to a view.
public sealed record ProviderProfileView(string Id, string Name, ProviderCapability Capability,
    string Kind, string Endpoint, string Model, int Dimensions, bool HasSecret, int Version, bool UsesEnvironmentSecret = false);
public sealed record ProviderSelectionView(string? Speech, string? Chat, string? Embedding, int Version,
    DateTimeOffset UpdatedAt, string UpdatedBy, string? SecondarySpeech = null);
public sealed record ProviderAdminView(IReadOnlyList<ProviderProfileView> Profiles, ProviderSelectionView Selection,
    IReadOnlyList<string> AllowedHosts);
public sealed record SaveProviderCommand(string Id, string Endpoint, string Model, int Dimensions,
    [property: SensitiveData] string? NewSecret, bool RemoveSecret, int Version);
public sealed record SelectProvidersCommand(string? Speech, string? Chat, string? Embedding, int Version, string? SecondarySpeech = null);
public interface IProviderAdministration
{
    Task<ProviderAdminView> ReadAsync(CancellationToken ct);
    Task SaveAsync(SaveProviderCommand command, string actor, CancellationToken ct);
    Task SelectAsync(SelectProvidersCommand command, string actor, CancellationToken ct);
}

// A request/job obtains one immutable routing snapshot; never serialized to HTTP.
public sealed class ProviderConnection(string id, string kind, string endpoint, string model, int dimensions, string secret)
{
    public string Id { get; } = id;
    public string Kind { get; } = kind;
    public string Endpoint { get; } = endpoint;
    public string Model { get; } = model;
    public int Dimensions { get; } = dimensions;
    [SensitiveData] public string Secret { get; } = secret;
    public override string ToString() => $"{Kind}/{Id} (credential redacted)";
}
public interface ISpeechToTextAdapter
{
    string Kind { get; }
    Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, CancellationToken ct);
    Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, VideoContext context, CancellationToken ct) => TranscribeAsync(connection, audioPath, offset, ct);
}
public interface ILlmAdapter
{
    string Kind { get; }
    Task<string> CompleteJsonAsync(ProviderConnection connection, string system, string data, CancellationToken ct);
}
public interface ITranscriptAlignmentAdapter
{
    string Kind { get; }
    Task<(double Start, double End)> AlignAsync(ProviderConnection connection, string audioPath, string text, double offset, CancellationToken ct);
}
public interface IEmbeddingAdapter
{
    string Kind { get; }
    Task<float[][]> EmbedAsync(ProviderConnection connection, string[] texts, CancellationToken ct);
}
public sealed record ProviderBinding<T>(ProviderConnection Connection, T Adapter);
public interface IAiProviderFactory
{
    Task<ProviderBinding<ISpeechToTextAdapter>?> SpeechAsync(CancellationToken ct);
    Task<ProviderBinding<ISpeechToTextAdapter>?> SecondarySpeechAsync(CancellationToken ct) => Task.FromResult<ProviderBinding<ISpeechToTextAdapter>?>(null);
    Task<ProviderBinding<ILlmAdapter>?> ChatAsync(CancellationToken ct);
    Task<ProviderBinding<IEmbeddingAdapter>?> EmbeddingAsync(CancellationToken ct);
}
