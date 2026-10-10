using System.Globalization;
using System.Text.Json;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Mcp;

public sealed class AudioAnalysisService(PathPolicy paths, ExternalProcessRunner processes, TopicSegmenter topics, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> Models = new(StringComparer.Ordinal) { "tiny", "base", "small", "medium", "large", "turbo", "large-v3", "large-v3-turbo" };
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".mp4", ".mov", ".webm", ".mkv" };

    public async Task<DependencyStatus> CheckDependenciesAsync(CancellationToken ct)
    {
        var commands = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ffmpeg"] = Resolve("FFMPEG_EXECUTABLE", "ffmpeg"),
            ["whisper"] = Resolve("WHISPER_EXECUTABLE", "whisper"),
            ["python"] = Resolve("PYTHON_EXECUTABLE", OperatingSystem.IsWindows() ? "python" : "python3")
        };
        var missing = new List<string>();
        foreach (var command in commands)
        {
            try
            {
                var args = command.Key == "ffmpeg" ? new[] { "-version" } : new[] { "--help" };
                await processes.RunAsync(command.Value, args, Environment.CurrentDirectory, TimeSpan.FromSeconds(20), ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                missing.Add(command.Key);
            }
        }
        return new(missing.Count == 0, commands, missing, paths.AllowedRoots);
    }

    public async Task<AudioAnalysisManifest> AnalyzeAsync(string inputPath, string? outputDirectory, string model,
        string? language, int? speakerCount, CancellationToken ct)
    {
        var source = ValidateMedia(inputPath);
        ValidateOptions(model, language, speakerCount);
        var output = paths.NewOutputDirectory(outputDirectory, source);
        var normalized = Path.Combine(output, "audio-16khz-mono.wav");
        await processes.RunAsync(Resolve("FFMPEG_EXECUTABLE", "ffmpeg"),
            ["-nostdin", "-hide_banner", "-loglevel", "error", "-i", source, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", normalized],
            output, Timeout("VIDEOATLAS_MCP_FFMPEG_TIMEOUT_MINUTES", 30), ct);

        var transcript = await TranscribeNormalizedAsync(source, normalized, output, model, language, ct);
        var segments = speakerCount.HasValue
            ? await DiarizeAsync(normalized, transcript.Segments, speakerCount.Value, output, ct)
            : transcript.Segments;
        transcript = transcript with { Segments = segments };
        var chapters = topics.Split(segments);

        var transcriptPath = Path.Combine(output, "transcript.json");
        var srtPath = Path.Combine(output, "transcript.srt");
        var topicPath = Path.Combine(output, "topics.json");
        await File.WriteAllTextAsync(transcriptPath, JsonSerializer.Serialize(transcript, Json), ct);
        await File.WriteAllTextAsync(srtPath, Subtitles.Format(segments), ct);
        await File.WriteAllTextAsync(topicPath, JsonSerializer.Serialize(new TopicDocument("local-context-similarity", clock.GetUtcNow(), chapters), Json), ct);
        var manifest = new AudioAnalysisManifest(source, output, model, language, speakerCount, transcriptPath, srtPath, topicPath,
            segments.Count, chapters.Count, segments.Count == 0 ? 0 : segments.Max(item => item.End));
        await File.WriteAllTextAsync(Path.Combine(output, "analysis.json"), JsonSerializer.Serialize(manifest, Json), ct);
        return manifest;
    }

    public Task<AudioAnalysisManifest> TranscribeAsync(string inputPath, string? outputDirectory, string model, string? language, CancellationToken ct) =>
        AnalyzeAsync(inputPath, outputDirectory, model, language, null, ct);

    public async Task<string> ExportTopicsAsync(string transcriptJsonPath, string? outputPath, CancellationToken ct)
    {
        var transcript = await ReadTranscriptAsync(transcriptJsonPath, ct);
        var document = new TopicDocument("local-context-similarity", clock.GetUtcNow(), topics.Split(transcript.Segments));
        var target = paths.WriteFile(outputPath ?? Path.Combine(Path.GetDirectoryName(paths.ReadFile(transcriptJsonPath))!, "topics.json"));
        await File.WriteAllTextAsync(target, JsonSerializer.Serialize(document, Json), ct);
        return target;
    }

    public async Task<string> ExportSrtAsync(string transcriptJsonPath, string? outputPath, CancellationToken ct)
    {
        var transcript = await ReadTranscriptAsync(transcriptJsonPath, ct);
        var target = paths.WriteFile(outputPath ?? Path.Combine(Path.GetDirectoryName(paths.ReadFile(transcriptJsonPath))!, "transcript.srt"));
        await File.WriteAllTextAsync(target, Subtitles.Format(transcript.Segments), ct);
        return target;
    }

    public async Task<string> DiarizeTranscriptAsync(string audioPath, string transcriptJsonPath, int speakerCount, string? outputPath, CancellationToken ct)
    {
        if (speakerCount is < 2 or > 32) throw new ArgumentOutOfRangeException(nameof(speakerCount), "Speaker count must be between 2 and 32.");
        var audio = ValidateMedia(audioPath);
        var transcript = await ReadTranscriptAsync(transcriptJsonPath, ct);
        var target = paths.WriteFile(outputPath ?? Path.Combine(Path.GetDirectoryName(paths.ReadFile(transcriptJsonPath))!, "transcript-diarized.json"));
        var workspace = Path.Combine(Path.GetDirectoryName(target)!, $"diarization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var normalized = Path.Combine(workspace, "audio-16khz-mono.wav");
            await processes.RunAsync(Resolve("FFMPEG_EXECUTABLE", "ffmpeg"),
                ["-nostdin", "-hide_banner", "-loglevel", "error", "-i", audio, "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", normalized],
                workspace, Timeout("VIDEOATLAS_MCP_FFMPEG_TIMEOUT_MINUTES", 30), ct);
            var segments = await DiarizeAsync(normalized, transcript.Segments, speakerCount, workspace, ct);
            await File.WriteAllTextAsync(target, JsonSerializer.Serialize(transcript with { Segments = segments }, Json), ct);
            return target;
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<TranscriptDocument> TranscribeNormalizedAsync(string source, string normalized, string output, string model, string? language, CancellationToken ct)
    {
        var args = new List<string> { normalized, "--model", model, "--output_dir", output, "--output_format", "json", "--verbose", "False" };
        if (!string.IsNullOrWhiteSpace(language)) { args.Add("--language"); args.Add(language); }
        await processes.RunAsync(Resolve("WHISPER_EXECUTABLE", "whisper"), args, output,
            Timeout("VIDEOATLAS_MCP_WHISPER_TIMEOUT_MINUTES", 180), ct);
        var whisperJson = Path.Combine(output, Path.GetFileNameWithoutExtension(normalized) + ".json");
        if (!File.Exists(whisperJson)) throw new InvalidDataException("Whisper did not produce its JSON output.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(whisperJson, ct));
        var segments = document.RootElement.GetProperty("segments").EnumerateArray().Select((item, index) => new TranscriptSegment(
            index, item.GetProperty("start").GetDouble(), item.GetProperty("end").GetDouble(),
            item.GetProperty("text").GetString()?.Trim() ?? string.Empty)).Where(item => item.Text.Length > 0 && item.End > item.Start).ToArray();
        if (segments.Length == 0) throw new InvalidDataException("Whisper returned no timestamped speech segments.");
        return new(source, model, language, clock.GetUtcNow(), segments);
    }

    private async Task<IReadOnlyList<TranscriptSegment>> DiarizeAsync(string normalizedAudio, IReadOnlyList<TranscriptSegment> segments,
        int speakerCount, string workspace, CancellationToken ct)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "local_speaker_diarization.py");
        if (!File.Exists(script)) throw new FileNotFoundException("The local speaker diarization component is missing.", script);
        var input = Path.Combine(workspace, "diarization-input.json");
        var output = Path.Combine(workspace, "diarization-output.json");
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(segments.Select(item => new { id = item.Id, start = item.Start, end = item.End }), Json), ct);
        await processes.RunAsync(Resolve("PYTHON_EXECUTABLE", OperatingSystem.IsWindows() ? "python" : "python3"),
            [script, "--audio", normalizedAudio, "--segments", input, "--speaker-count", speakerCount.ToString(CultureInfo.InvariantCulture), "--output", output],
            workspace, Timeout("VIDEOATLAS_MCP_DIARIZATION_TIMEOUT_MINUTES", 120), ct);
        var labels = JsonSerializer.Deserialize<DiarizationLabel[]>(await File.ReadAllTextAsync(output, ct), Json) ?? [];
        var byId = labels.ToDictionary(item => item.Id, item => item.Speaker);
        if (byId.Count != segments.Count || segments.Any(item => !byId.ContainsKey(item.Id)))
            throw new InvalidDataException("Speaker diarization returned incomplete labels.");
        return segments.Select(item => item with { Speaker = byId[item.Id] }).ToArray();
    }

    private async Task<TranscriptDocument> ReadTranscriptAsync(string path, CancellationToken ct)
    {
        var document = JsonSerializer.Deserialize<TranscriptDocument>(await File.ReadAllTextAsync(paths.ReadFile(path), ct), Json);
        return document is { Segments.Count: > 0 } ? document : throw new InvalidDataException("Transcript JSON is empty or invalid.");
    }

    private string ValidateMedia(string inputPath)
    {
        var source = paths.ReadFile(inputPath);
        if (!Extensions.Contains(Path.GetExtension(source))) throw new InvalidDataException("Unsupported audio/video extension.");
        var maxBytes = long.TryParse(Environment.GetEnvironmentVariable("VIDEOATLAS_MCP_MAX_INPUT_BYTES"), out var configured) ? configured : 2L * 1024 * 1024 * 1024;
        if (new FileInfo(source).Length > maxBytes) throw new InvalidDataException("Input exceeds VIDEOATLAS_MCP_MAX_INPUT_BYTES.");
        return source;
    }

    private static void ValidateOptions(string model, string? language, int? speakerCount)
    {
        if (!Models.Contains(model)) throw new ArgumentException("Unsupported Whisper model.", nameof(model));
        if (!string.IsNullOrWhiteSpace(language) && (language.Length is < 2 or > 12 || language.Any(character => !char.IsAsciiLetter(character) && character != '-')))
            throw new ArgumentException("Language must be an ISO-style code such as tr, en, or de.", nameof(language));
        if (speakerCount.HasValue && speakerCount is < 2 or > 32)
            throw new ArgumentOutOfRangeException(nameof(speakerCount), "Speaker count must be between 2 and 32.");
    }

    private static string Resolve(string variable, string fallback)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configured)) return fallback;
        if (!Path.IsPathFullyQualified(configured) || !File.Exists(configured))
            throw new InvalidOperationException($"{variable} must point to an existing absolute executable path.");
        return configured;
    }

    private static TimeSpan Timeout(string variable, int fallbackMinutes) =>
        TimeSpan.FromMinutes(int.TryParse(Environment.GetEnvironmentVariable(variable), out var minutes) && minutes is >= 1 and <= 1440 ? minutes : fallbackMinutes);

    private sealed record DiarizationLabel(int Id, string Speaker);
}
