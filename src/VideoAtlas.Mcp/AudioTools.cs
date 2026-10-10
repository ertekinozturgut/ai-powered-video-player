using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace VideoAtlas.Mcp;

[McpServerToolType]
public static class AudioTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    [McpServerTool(Name = "check_local_audio_dependencies"), Description("Checks whether FFmpeg, local Whisper, Python, and the configured file roots are available.")]
    public static async Task<string> CheckDependencies(AudioAnalysisService service, CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await service.CheckDependenciesAsync(cancellationToken), Json);

    [McpServerTool(Name = "analyze_audio"), Description("Runs the complete local pipeline: normalize media, transcribe with Whisper, optionally separate speakers, split the transcript into contextual topics, and create JSON plus SRT files. The source file is never modified.")]
    public static async Task<string> AnalyzeAudio(
        AudioAnalysisService service,
        [Description("Absolute path to an allowed local audio or video file.")] string inputPath,
        [Description("New or empty output directory. Omit to create a unique directory next to the source.")] string? outputDirectory = null,
        [Description("Local Whisper model: tiny, base, small, medium, large, turbo, large-v3, or large-v3-turbo.")] string model = "large-v3-turbo",
        [Description("Optional ISO language code, for example tr or en. Omit for automatic detection.")] string? language = null,
        [Description("Optional expected speaker count from 2 to 32. Omit to skip speaker separation.")] int? speakerCount = null,
        [Description("Detect the speaker count automatically from voice, frequency, and channel evidence.")] bool automaticSpeakerDetection = false,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(await service.AnalyzeAsync(inputPath, outputDirectory, model, language, speakerCount, automaticSpeakerDetection, cancellationToken), Json);

    [McpServerTool(Name = "transcribe_audio"), Description("Transcribes an allowed local audio or video file with local Whisper and creates timestamped transcript JSON, SRT, and local topic files without speaker separation.")]
    public static async Task<string> TranscribeAudio(
        AudioAnalysisService service,
        [Description("Absolute path to an allowed local audio or video file.")] string inputPath,
        [Description("New or empty output directory, or null for an automatically named directory.")] string? outputDirectory = null,
        [Description("Local Whisper model name.")] string model = "large-v3-turbo",
        [Description("Optional ISO language code.")] string? language = null,
        CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(await service.TranscribeAsync(inputPath, outputDirectory, model, language, cancellationToken), Json);

    [McpServerTool(Name = "diarize_transcript"), Description("Assigns local speaker labels to an existing VideoAtlas transcript JSON by analyzing voice embeddings, pitch, frequency, MFCC, and spectral characteristics.")]
    public static Task<string> DiarizeTranscript(
        AudioAnalysisService service,
        [Description("Absolute path to the original audio or video file.")] string audioPath,
        [Description("Absolute path to transcript.json created by this MCP server.")] string transcriptJsonPath,
        [Description("Expected number of speakers from 2 to 32, or 0 for automatic detection.")] int speakerCount,
        [Description("Optional output JSON path. Defaults to transcript-diarized.json next to the input transcript.")] string? outputPath = null,
        CancellationToken cancellationToken = default) =>
        service.DiarizeTranscriptAsync(audioPath, transcriptJsonPath, speakerCount, outputPath, cancellationToken);

    [McpServerTool(Name = "split_transcript_topics"), Description("Splits an existing VideoAtlas transcript into chronological topics using local contextual similarity and writes topics.json.")]
    public static Task<string> SplitTopics(
        AudioAnalysisService service,
        [Description("Absolute path to transcript.json created by this MCP server.")] string transcriptJsonPath,
        [Description("Optional output JSON path.")] string? outputPath = null,
        CancellationToken cancellationToken = default) =>
        service.ExportTopicsAsync(transcriptJsonPath, outputPath, cancellationToken);

    [McpServerTool(Name = "export_srt"), Description("Exports a VideoAtlas transcript JSON as UTF-8 SRT, including speaker labels when present.")]
    public static Task<string> ExportSrt(
        AudioAnalysisService service,
        [Description("Absolute path to transcript.json or transcript-diarized.json.")] string transcriptJsonPath,
        [Description("Optional output SRT path.")] string? outputPath = null,
        CancellationToken cancellationToken = default) =>
        service.ExportSrtAsync(transcriptJsonPath, outputPath, cancellationToken);
}
