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
            start.ArgumentList.Add("--fp16");
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
            var segments = Subtitles.Parse(await File.ReadAllTextAsync(srt, ct), 3600)
                .Select((segment, index) => new TranscriptSegment(index, segment.Start + offset, segment.End + offset, segment.Text))
                .ToArray();
            return ProviderResponse.ValidateSegments(segments, offset);
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
}

public sealed class ElevenLabsSpeechAdapter(ProviderTransport transport) : ISpeechToTextAdapter
{
    public string Kind => "elevenlabs";
    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, CancellationToken ct) => TranscribeAsync(connection, audioPath, offset, VideoContext.Empty, ct);
    public async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(ProviderConnection connection, string audioPath, double offset, VideoContext context, CancellationToken ct)
    {
        await using var stream = File.OpenRead(audioPath);
        using var body = SpeechMultipart.Create(stream, "file");
        body.Add(new StringContent(connection.Model), "model_id");
        body.Add(new StringContent("word"), "timestamps_granularity");
        body.Add(new StringContent("false"), "diarize");
        body.Add(new StringContent("false"), "tag_audio_events");
        // Scribe v1 does not support keyterm prompting. Never send unsupported fields to it.
        if (connection.Model == "scribe_v2") foreach (var term in context.Terms.Take(50)) body.Add(new StringContent(term.Term), "keyterms");
        using var json = await transport.PostAsync(connection, "speech-to-text", body, ct);
        var words = ProviderResponse.Read(() => json.RootElement.GetProperty("words").EnumerateArray()
            .Where(w => w.GetProperty("type").GetString() == "word")
            .Select(w => new Word(w.GetProperty("start").GetDouble(), w.GetProperty("end").GetDouble(),
                w.GetProperty("text").GetString() ?? "")).ToArray());
        var result = new List<TranscriptSegment>();
        var text = new StringBuilder();
        double start = 0, end = 0, previous = 0;
        void Flush()
        {
            if (text.Length == 0) return;
            result.Add(new(result.Count, offset + start, offset + Math.Max(end, start + .01), text.ToString().Trim()));
            text.Clear();
        }
        foreach (var word in words)
        {
            if (!double.IsFinite(word.Start) || !double.IsFinite(word.End) || word.Start < previous || word.End < word.Start || word.End > 480.5)
                throw new AiException("ai_invalid_response");
            previous = word.Start;
            if (string.IsNullOrWhiteSpace(word.Text)) continue;
            if (text.Length > 0 && (word.Start - start >= 7 || text.Length + word.Text.Length > 220)) Flush();
            if (text.Length == 0) start = word.Start;
            else text.Append(' ');
            text.Append(word.Text.Trim()); end = word.End;
            if (word.Text.EndsWith('.') || word.Text.EndsWith('?') || word.Text.EndsWith('!')) Flush();
        }
        Flush();
        return ProviderResponse.ValidateSegments(result, offset);
    }
    private sealed record Word(double Start, double End, string Text);
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
