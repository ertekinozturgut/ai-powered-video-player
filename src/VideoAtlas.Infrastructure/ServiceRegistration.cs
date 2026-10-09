using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VideoAtlas.Application;

namespace VideoAtlas.Infrastructure;

public static class ServiceRegistration
{
    public static IServiceCollection AddVideoAtlas(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<StorageOptions>().Bind(config.GetSection("Storage"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Root) && o.MaxUploadBytes is > 0 and <= 536_870_912 && o.UserQuotaBytes >= o.MaxUploadBytes && o.MaxDurationSeconds is > 0 and <= 7200).ValidateOnStart();
        services.AddOptions<AiOptions>().Bind(config.GetSection("Ai"))
            .Validate(o => o.TranscriptionModel == "whisper-1" && !string.IsNullOrWhiteSpace(o.ChatModel)).ValidateOnStart();
        services.AddOptions<AccountOptions>().Bind(config.GetSection("Accounts"));
        services.AddOptions<ProviderSecurityOptions>().Bind(config.GetSection("ProviderSecurity"));
        services.AddOptions<ProviderCredentialOptions>().Bind(config.GetSection("ProviderCredentials"));
        services.AddScoped<ProviderSettingsStore>();
        services.AddScoped<IProviderAdministration>(s => s.GetRequiredService<ProviderSettingsStore>());
        services.AddSingleton<ProviderEndpointPolicy>();
        services.AddScoped<IAiProviderFactory, AiProviderFactory>();
        services.AddScoped<ProviderTransport>();
        services.AddOptions<UsageOptions>().Bind(config.GetSection("Usage"))
            .Validate(o => o.MaxCallsPerVideoDay > 0 && o.MaxCallsPerOwnerDay > 0 && double.IsFinite(o.MaxEstimatedUsdPerOwnerDay) && o.MaxEstimatedUsdPerOwnerDay > 0 &&
                o.Rates.All(r => double.IsFinite(r.InputPerMillion) && r.InputPerMillion >= 0 && double.IsFinite(r.OutputPerMillion) && r.OutputPerMillion >= 0 &&
                    (r.AudioPerHour is null || double.IsFinite(r.AudioPerHour.Value) && r.AudioPerHour >= 0))).ValidateOnStart();
        services.AddScoped<AiUsageContext>();
        services.AddScoped<IAiUsageContext>(s => s.GetRequiredService<AiUsageContext>());
        services.AddSingleton<AiUsageRecorder>();
        services.AddScoped<TranscriptIndex>();
        services.AddScoped<VideoIntelligence>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MutationGate>();
        services.AddSingleton<LibraryLimits>(s =>
        {
            var o = s.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new(o.MaxUploadBytes, o.UserQuotaBytes);
        });
        services.AddDbContext<AppDbContext>((s, options) =>
        {
            var root = Path.GetFullPath(s.GetRequiredService<IOptions<StorageOptions>>().Value.Root);
            Directory.CreateDirectory(root);
            options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "videoatlas.db"), ForeignKeys = true }.ToString());
        });
        services.AddIdentity<IdentityUser, IdentityRole>(o =>
        {
            o.Password.RequiredLength = 12;
            o.User.RequireUniqueEmail = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000);
        services.AddScoped<VideoStore>();
        services.AddScoped<IVideoStore>(s => s.GetRequiredService<VideoStore>());
        services.AddScoped<ILibraryStore>(s => s.GetRequiredService<VideoStore>());
        services.AddSingleton<IMediaStorage, MediaStorage>();
        services.AddScoped<VideoAccess>();
        services.AddScoped<VideoLibrary>();
        services.AddScoped<IShareAudience, ShareAudience>();
        services.AddScoped<VideoChat>();
        services.AddScoped<TranscriptReviewService>();
        services.AddScoped<ITranscriptReviewer, TranscriptReviewer>();
        services.AddScoped<ITranscriptAlignmentAdapter, ElevenLabsAlignmentAdapter>();
        services.AddScoped<MediaProcessor>();
        services.AddScoped<VideoPipeline>();
        services.AddSingleton<VideoJobRegistry>();
        services.AddHttpClient("AiProviders", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
            client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
          .RemoveAllLoggers();
        services.AddScoped<ISpeechToTextAdapter, ElevenLabsSpeechAdapter>();
        services.AddScoped<ISpeechToTextAdapter, OpenAiSpeechAdapter>();
        services.AddScoped<ISpeechToTextAdapter, AzureSpeechAdapter>();
        services.AddScoped<ISpeechToTextAdapter, N8nAdapter>();
        services.AddScoped<ILlmAdapter, OpenRouterAdapter>();
        services.AddScoped<ILlmAdapter, OpenAiAdapter>();
        services.AddScoped<ILlmAdapter, AzureFoundryAdapter>();
        services.AddScoped<ILlmAdapter, N8nAdapter>();
        services.AddScoped<ILlmAdapter, OllamaAdapter>();
        services.AddScoped<IEmbeddingAdapter, OpenRouterAdapter>();
        services.AddScoped<IEmbeddingAdapter, OpenAiAdapter>();
        services.AddScoped<IEmbeddingAdapter, AzureFoundryAdapter>();
        services.AddScoped<IEmbeddingAdapter, N8nAdapter>();
        services.AddScoped<IEmbeddingAdapter, OllamaAdapter>();
        services.AddScoped<IVideoIntelligence>(s => s.GetRequiredService<VideoIntelligence>());
        services.AddHostedService<VideoWorker>();
        return services;
    }
}
