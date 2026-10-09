namespace VideoAtlas.Infrastructure;

public sealed class StorageOptions
{
    public string Root { get; set; } = "data";
    public long MaxUploadBytes { get; set; } = 536_870_912;
    public long UserQuotaBytes { get; set; } = 5_368_709_120;
    public int MaxDurationSeconds { get; set; } = 7200;
}
public sealed class AiOptions
{
    public string ApiKey { get; set; } = "";
    public string ChatModel { get; set; } = "gpt-4o-mini";
    public string TranscriptionModel { get; set; } = "whisper-1";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
}
public sealed class AccountOptions
{
    public bool AllowRegistration { get; set; }
}
