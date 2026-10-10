using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

public sealed class LocalWhisperSpeechAdapter(ILogger<LocalWhisperSpeechAdapter> logger) : ISpeechToTextAdapter
{
    public string Kind => "local-whisper";
    public bool SupportsSpeakerDiarization => true;

    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, CancellationToken ct) =>
        TranscribeAsync(connection, audioPath, offset, VideoContext.Empty, ct);

    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, VideoContext context, CancellationToken ct)
    {
        if (!File.Exists(audioPath)) throw new AppException(409, "media_missing", "Transkripsiyon için ses dosyası bulunamadı.");
        var outputDirectory = Path.Combine(Path.GetTempPath(), "videoatlas-whisper", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = ResolveExecutable(),
                WorkingDirectory = outputDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            start.ArgumentList.Add(audioPath);
            start.ArgumentList.Add("--model");
            start.ArgumentList.Add(ValidModel(connection.Model));
            start.ArgumentList.Add("--output_dir");
            start.ArgumentList.Add(outputDirectory);
            start.ArgumentList.Add("--output_format");
            start.ArgumentList.Add("srt");
            start.ArgumentList.Add("--verbose");
            start.ArgumentList.Add("False");
            if (context.Language is "tr" or "en")
            {
                start.ArgumentList.Add("--language");
                start.ArgumentList.Add(context.Language);
            }

            using var process = Process.Start(start) ?? throw new AiException("ai_unavailable");
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(output, error);
            if (process.ExitCode != 0)
            {
                logger.LogWarning("Local Whisper exited with code {ExitCode}.", process.ExitCode);
                throw new AiException("ai_unavailable");
            }
            var srt = Directory.EnumerateFiles(outputDirectory, "*.srt", SearchOption.TopDirectoryOnly).SingleOrDefault();
            if (srt is null) throw new AiException("ai_invalid_response");
            // Pipeline windows are short; keep a generous parser bound so valid relative SRT timestamps survive.
            IReadOnlyList<TranscriptSegment> segments = Subtitles.Parse(await File.ReadAllTextAsync(srt, ct), context.DiarizationEnabled ? 7200 : 3600)
                .Select((segment, index) => new TranscriptSegment(index, segment.Start + offset, segment.End + offset, segment.Text))
                .ToArray();
            if (context.DiarizationEnabled)
                segments = await DiarizeAsync(audioPath, offset, segments, context.SpeakerCount, outputDirectory, ct);
            return ProviderResponse.ValidateSegments(segments, offset, context.DiarizationEnabled ? 7200 : 480);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (AppException) { throw; }
        catch (AiException) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Local Whisper could not transcribe an audio chunk.");
            throw new AiException("ai_unavailable");
        }
        finally
        {
            try { Directory.Delete(outputDirectory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string ResolveExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("WHISPER_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured) || !File.Exists(configured))
                throw new AppException(400, "whisper_missing", "WHISPER_EXECUTABLE geçerli bir Whisper çalıştırılabilir dosyasını göstermiyor.");
            return configured;
        }
        return "whisper";
    }

    private static string ValidModel(string model) => model is "tiny" or "base" or "small" or "medium" or "large" or "turbo" or "large-v3" or "large-v3-turbo"
        ? model == "base" ? "large-v3-turbo" : model : "large-v3-turbo";

    private async Task<IReadOnlyList<TranscriptSegment>> DiarizeAsync(string audioPath, double offset,
        IReadOnlyList<TranscriptSegment> segments, int? speakerCount, string workingDirectory, CancellationToken ct)
    {
        if (speakerCount is not (>= 2 and <= 32))
            throw new AppException(400, "invalid_speaker_count", "Konuşmacı sayısı 2–32 arasında olmalıdır.");
        var script = Path.Combine(AppContext.BaseDirectory, "local_speaker_diarization.py");
        if (!File.Exists(script))
            throw new AppException(409, "local_diarization_unavailable", "Yerel konuşmacı ayrımı bileşeni bulunamadı. Uygulamayı yeniden yayımlayın.");
        var input = Path.Combine(workingDirectory, "diarization-input.json");
        var output = Path.Combine(workingDirectory, "diarization-output.json");
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(segments.Select(segment => new
        {
            id = segment.Id,
            start = Math.Max(0, segment.Start - offset),
            end = Math.Max(0, segment.End - offset)
        })), ct);
        var start = new ProcessStartInfo
        {
            FileName = ResolvePythonExecutable(), WorkingDirectory = workingDirectory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("--audio"); start.ArgumentList.Add(audioPath);
        start.ArgumentList.Add("--segments"); start.ArgumentList.Add(input);
        start.ArgumentList.Add("--speaker-count"); start.ArgumentList.Add(speakerCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--output"); start.ArgumentList.Add(output);
        using var process = Process.Start(start) ?? throw new AppException(409, "local_diarization_unavailable", "Yerel konuşmacı ayrımı başlatılamadı.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct); await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0 || !File.Exists(output))
        {
            logger.LogWarning("Local speaker diarization exited with code {ExitCode}.", process.ExitCode);
            throw new AppException(409, "local_diarization_unavailable",
                "Yerel konuşmacı ayrımı çalıştırılamadı. Python için requirements-local-whisper.txt bağımlılıklarını kurun.");
        }
        DiarizationLabel[] labels;
        try
        {
            labels = JsonSerializer.Deserialize<DiarizationLabel[]>(await File.ReadAllTextAsync(output, ct),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch (JsonException)
        {
            throw new AppException(409, "local_diarization_unavailable", "Yerel konuşmacı ayrımı geçerli sonuç döndürmedi.");
        }
        if (labels.Length != segments.Count || labels.Select(label => label.Id).Distinct().Count() != labels.Length)
            throw new AppException(409, "local_diarization_unavailable", "Yerel konuşmacı ayrımı eksik veya yinelenen sonuç döndürdü.");
        var byId = labels.ToDictionary(label => label.Id, label => label.Speaker);
        if (byId.Count != segments.Count || segments.Any(segment =>
                !byId.TryGetValue(segment.Id, out var speaker) || !ValidSpeakerLabel(speaker)))
            throw new AppException(409, "local_diarization_unavailable", "Yerel konuşmacı ayrımı eksik veya geçersiz sonuç döndürdü.");
        return segments.Select(segment => segment with { Speaker = byId[segment.Id] }).ToArray();
    }

    private static string ResolvePythonExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("PYTHON_EXECUTABLE");
        if (string.IsNullOrWhiteSpace(configured)) return "python";
        if (!Path.IsPathFullyQualified(configured) || !File.Exists(configured))
            throw new AppException(400, "local_diarization_unavailable", "PYTHON_EXECUTABLE geçerli bir Python çalıştırılabilir dosyasını göstermiyor.");
        return configured;
    }

    private static bool ValidSpeakerLabel(string? value) => value is { Length: >= 9 and <= 20 } &&
        value.StartsWith("speaker_", StringComparison.Ordinal) && value[8..].All(char.IsAsciiDigit);
    private sealed record DiarizationLabel(int Id, string Speaker);
}

public sealed class ElevenLabsSpeechAdapter(ProviderTransport transport) : ISpeechToTextAdapter
{
    public string Kind => "elevenlabs";
    public bool SupportsSpeakerDiarization => true;
    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, CancellationToken ct) => TranscribeAsync(connection, audioPath, offset, VideoContext.Empty, ct);
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, VideoContext context, CancellationToken ct)
    {
        await using var stream = File.OpenRead(audioPath);
        using var body = SpeechMultipart.Create(stream, "file");
        body.Add(new StringContent(connection.Model), "model_id");
        body.Add(new StringContent("word"), "timestamps_granularity");
        body.Add(new StringContent(context.DiarizationEnabled ? "true" : "false"), "diarize");
        if (context.DiarizationEnabled && context.SpeakerCount is { } speakerCount)
            body.Add(new StringContent(speakerCount.ToString(System.Globalization.CultureInfo.InvariantCulture)), "num_speakers");
        body.Add(new StringContent("false"), "tag_audio_events");
        // Scribe v1 does not support keyterm prompting. Never send unsupported fields to it.
        if (connection.Model == "scribe_v2") foreach (var term in context.Terms.Take(50)) body.Add(new StringContent(term.Term), "keyterms");
        using var json = await transport.PostAsync(connection, "speech-to-text", body, ct);
        var words = ProviderResponse.Read(() => json.RootElement.GetProperty("words").EnumerateArray()
            .Where(w => w.GetProperty("type").GetString() == "word")
            .Select(w => new Word(w.GetProperty("start").GetDouble(), w.GetProperty("end").GetDouble(),
                w.GetProperty("text").GetString() ?? "", w.TryGetProperty("speaker_id", out var speaker) ? speaker.GetString() : null)).ToArray());
        var result = new List<TranscriptSegment>();
        var text = new StringBuilder();
        double start = 0, end = 0, previous = 0;
        string? currentSpeaker = null;
        void Flush()
        {
            if (text.Length == 0) return;
            result.Add(new(result.Count, offset + start, offset + Math.Max(end, start + .01), text.ToString().Trim(), currentSpeaker));
            text.Clear();
        }
        foreach (var word in words)
        {
            var maximumDuration = context.DiarizationEnabled ? 7200.5 : 480.5;
            if (!double.IsFinite(word.Start) || !double.IsFinite(word.End) || word.Start < previous || word.End < word.Start || word.End > maximumDuration)
                throw new AiException("ai_invalid_response");
            previous = word.Start;
            if (string.IsNullOrWhiteSpace(word.Text)) continue;
            if (text.Length > 0 && (word.Speaker != currentSpeaker || word.Start - start >= 7 || text.Length + word.Text.Length > 220)) Flush();
            if (text.Length == 0) { start = word.Start; currentSpeaker = word.Speaker; }
            else text.Append(' ');
            text.Append(word.Text.Trim()); end = word.End;
            if (word.Text.EndsWith('.') || word.Text.EndsWith('?') || word.Text.EndsWith('!')) Flush();
        }
        Flush();
        return ProviderResponse.ValidateSegments(result, offset, context.DiarizationEnabled ? 7200 : 480);
    }
    private sealed record Word(double Start, double End, string Text, string? Speaker);
}
public sealed class OpenAiSpeechAdapter(ProviderTransport transport) : ISpeechToTextAdapter
{
    public string Kind => "openai";
    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, CancellationToken ct) => TranscribeAsync(connection, audioPath, offset, VideoContext.Empty, ct);
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, VideoContext context, CancellationToken ct)
    {
        await using var stream = File.OpenRead(audioPath);
        using var body = SpeechMultipart.Create(stream, "file");
        body.Add(new StringContent(connection.Model), "model");
        body.Add(new StringContent("verbose_json"), "response_format");
        body.Add(new StringContent("segment"), "timestamp_granularities[]");
        if (context.Terms.Count > 0)
        {
            var vocabulary = string.Join(", ", context.Terms.Select(t => t.Term));
            body.Add(new StringContent(vocabulary[..Math.Min(800, vocabulary.Length)]), "prompt");
        }
        using var json = await transport.PostAsync(connection, "audio/transcriptions", body, ct);
        return ProviderResponse.Segments(ProviderResponse.Read(() => json.RootElement.GetProperty("segments")), offset);
    }
}
public sealed class AzureSpeechAdapter(ProviderTransport transport) : ISpeechToTextAdapter
{
    public string Kind => "azure-speech";
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, CancellationToken ct)
    {
        await using var stream = File.OpenRead(audioPath);
        using var body = SpeechMultipart.Create(stream, "audio");
        body.Add(new StringContent(JsonSerializer.Serialize(new { locales = new[] { connection.Model }, profanityFilterMode = "None" }), Encoding.UTF8, "application/json"), "definition");
        using var json = await transport.PostAsync(connection, "speechtotext/transcriptions:transcribe?api-version=2024-11-15", body, ct);
        var result = ProviderResponse.Read(() => json.RootElement.GetProperty("phrases").EnumerateArray().Select((p, i) =>
            new TranscriptSegment(i, offset + p.GetProperty("offsetMilliseconds").GetDouble() / 1000,
                offset + (p.GetProperty("offsetMilliseconds").GetDouble() + p.GetProperty("durationMilliseconds").GetDouble()) / 1000,
                p.GetProperty("text").GetString() ?? "")).OrderBy(s => s.Start).ToArray());
        return ProviderResponse.ValidateSegments(result, offset);
    }
}
internal static class SpeechMultipart
{
    public static MultipartFormDataContent Create(Stream stream, string name)
    {
        var body = new MultipartFormDataContent();
        var audio = new StreamContent(stream);
        audio.Headers.ContentType = new("audio/mpeg");
        body.Add(audio, name, "audio.mp3");
        return body;
    }
}
