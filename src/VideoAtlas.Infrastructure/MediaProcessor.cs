using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

public sealed partial class MediaProcessor(IMediaStorage files, IOptions<StorageOptions> settings)
{
    private static readonly HashSet<string> VideoFormats = new(StringComparer.Ordinal) { "mov", "mp4", "matroska", "webm" };
    private static readonly HashSet<string> AudioFormats = new(StringComparer.Ordinal) { "mp3", "wav", "flac", "ogg", "opus", "aac", "aiff", "asf", "mov", "mp4", "matroska", "webm" };

    public async Task<MediaPreparation> PrepareAsync(Guid id, CancellationToken ct)
    {
        var source = files.PathFor(id, "source.bin");
        var target = files.PathFor(id, "video.mp4");
        var input = File.Exists(source) ? source : target;
        var raw = await RunAsync("ffprobe", ["-v", "error", "-protocol_whitelist", "file,pipe", "-show_format", "-show_streams", "-of", "json", input], ct);
        using var json = JsonDocument.Parse(raw);
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        var visualStreams = streams.Where(IsVisualVideo).ToArray();
        var hasVideo = visualStreams.Length > 0;
        var hasAudio = streams.Any(s => s.GetProperty("codec_type").GetString() == "audio");
        if (!hasVideo && !hasAudio) throw new MediaException("invalid_media");
        if (hasVideo)
        {
            var picture = visualStreams[0];
            var width = picture.GetProperty("width").GetInt32();
            var height = picture.GetProperty("height").GetInt32();
            if (width is <= 0 or > 8192 || height is <= 0 or > 8192 || (long)width * height > 16_777_216)
                throw new MediaException("resolution_limit");
        }
        var duration = double.Parse(json.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        if (!double.IsFinite(duration) || duration is <= 0 || duration > settings.Value.MaxDurationSeconds) throw new MediaException("duration_limit");
        var format = json.RootElement.GetProperty("format").GetProperty("format_name").GetString() ?? "";
        var formats = format.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!formats.Any((hasVideo ? VideoFormats : AudioFormats).Contains)) throw new MediaException("invalid_media");
        if (!File.Exists(target))
        {
            var temporary = files.PathFor(id, "video.processing.mp4");
            var arguments = hasVideo
                ? new[] { "-nostdin", "-y", "-v", "error", "-protocol_whitelist", "file,pipe", "-threads", "2", "-i", input,
                    "-map", "0:v:0", "-map", "0:a:0?", "-map_metadata", "-1", "-vf", "scale=w='min(1280,iw)':h='min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2", "-c:v", "libx264", "-threads", "2",
                    "-preset", "veryfast", "-crf", "23", "-maxrate", "2000k", "-bufsize", "4000k", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "96k", "-fs", "1073741824", "-movflags", "+faststart", temporary }
                : new[] { "-nostdin", "-y", "-v", "error", "-protocol_whitelist", "file,pipe", "-threads", "1", "-i", input,
                    "-map", "0:a:0", "-map_metadata", "-1", "-vn", "-c:a", "aac", "-b:a", "96k", "-ar", "48000", "-ac", "2",
                    "-fs", "1073741824", "-movflags", "+faststart", temporary };
            await RunAsync("ffmpeg", arguments, ct);
            var outputDuration = await RunAsync("ffprobe", ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", temporary], ct);
            if (!double.TryParse(outputDuration.Trim(), CultureInfo.InvariantCulture, out var actualDuration) || actualDuration < duration - 1)
            { File.Delete(temporary); throw new MediaException("output_size_limit"); }
            File.Move(temporary, target, true);
        }
        if (!File.Exists(files.PathFor(id, "poster.jpg")))
        {
            if (hasVideo)
                await RunAsync("ffmpeg", ["-nostdin", "-y", "-v", "error", "-i", target, "-frames:v", "1", "-vf", "scale=640:-2", "-threads", "1", files.PathFor(id, "poster.jpg")], ct);
            else
                await RunAsync("ffmpeg", ["-nostdin", "-y", "-v", "error", "-f", "lavfi", "-i", "color=c=0x11121a:s=640x360", "-frames:v", "1", "-threads", "1", files.PathFor(id, "poster.jpg")], ct);
        }
        if (File.Exists(source)) File.Delete(source);
        return new(duration, hasVideo);
    }

    private static bool IsVisualVideo(JsonElement stream)
    {
        if (stream.GetProperty("codec_type").GetString() != "video") return false;
        return !stream.TryGetProperty("disposition", out var disposition) ||
            !disposition.TryGetProperty("attached_pic", out var attached) || attached.GetInt32() != 1;
    }

    public Task<string?> AudioAsync(Guid id, double start, double duration, CancellationToken ct) => AudioWindowAsync(id, start, duration, "audio.mp3", ct);

    public async Task<IReadOnlyList<SilenceInterval>> TranscriptionWindowsAsync(Guid id, double duration, CancellationToken ct)
    {
        const double maximumWindow = 45, minimumWindow = 8, minimumBreak = .25;
        var silence = await SilenceAsync(id, duration, ct);
        var windows = new List<SilenceInterval>();
        var start = 0d;
        while (start < duration)
        {
            var limit = Math.Min(duration, start + maximumWindow);
            var breakAt = silence.Where(interval => interval.End <= limit && interval.End - interval.Start >= minimumBreak && interval.End - start >= minimumWindow)
                .Select(interval => interval.End).DefaultIfEmpty(limit).Max();
            if (breakAt <= start) breakAt = limit;
            windows.Add(new(start, breakAt));
            start = breakAt;
        }
        return windows;
    }

    public async Task<IReadOnlyList<VideoAtlas.Domain.SilenceInterval>> SilenceAsync(Guid id, double duration, CancellationToken ct)
    {
        var video = files.PathFor(id, "video.mp4");
        using var probe = JsonDocument.Parse(await RunAsync("ffprobe", ["-v", "error", "-select_streams", "a", "-show_entries", "stream=index", "-of", "json", video], ct));
        if (probe.RootElement.GetProperty("streams").GetArrayLength() == 0) return [];
        var metadata = await RunAsync("ffmpeg", ["-nostdin", "-v", "error", "-i", video, "-map", "0:a:0", "-vn", "-af",
            "silencedetect=noise=-40dB:d=1,ametadata=mode=print:file=-", "-f", "null", "-"], ct);
        return ParseSilence(metadata, duration);
    }
    public static IReadOnlyList<VideoAtlas.Domain.SilenceInterval> ParseSilence(string metadata, double duration)
    {
        var result = new List<VideoAtlas.Domain.SilenceInterval>(); double? start = null;
        foreach (Match match in SilencePattern().Matches(metadata))
        {
            if (!double.TryParse(match.Groups[2].Value, CultureInfo.InvariantCulture, out var at) || !double.IsFinite(at)) continue;
            at = Math.Clamp(at, 0, duration);
            if (match.Groups[1].Value == "start") start = at;
            else if (start is { } from && at > from) { result.Add(new(from, at)); start = null; }
        }
        if (start is { } last && duration > last) result.Add(new(last, duration));
        return result;
    }
    [GeneratedRegex(@"lavfi\.silence_(start|end)=([0-9.]+)", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SilencePattern();

    public async Task<string?> AudioWindowAsync(Guid id, double start, double duration, string filename, CancellationToken ct)
    {
        var video = files.PathFor(id, "video.mp4");
        var raw = await RunAsync("ffprobe", ["-v", "error", "-select_streams", "a", "-show_entries", "stream=index", "-of", "json", video], ct);
        using var json = JsonDocument.Parse(raw);
        if (json.RootElement.GetProperty("streams").GetArrayLength() == 0) return null;
        var path = files.PathFor(id, filename);
        await RunAsync("ffmpeg", ["-nostdin", "-y", "-v", "error", "-ss", start.ToString(CultureInfo.InvariantCulture), "-i", video,
            "-t", duration.ToString(CultureInfo.InvariantCulture), "-vn", "-af", "highpass=f=80,lowpass=f=7600,loudnorm=I=-16:TP=-1.5:LRA=11", "-ac", "1", "-ar", "16000", "-b:a", "48k", "-threads", "1", path], ct);
        return path;
    }

    private static async Task<string> RunAsync(string executable, IReadOnlyList<string> args, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(45));
        var info = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        process.Start();
        // Read both pipes concurrently to avoid FFmpeg pipe deadlock. Limit input formats and stderr verbosity.
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await error;
            var result = await output;
            if (process.ExitCode != 0) throw new MediaException("invalid_video");
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            try { await Task.WhenAll(output, error); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { /* Original cancellation is rethrown below. */ }
            if (ct.IsCancellationRequested) throw;
            throw new MediaException("processing_timeout");
        }
    }
}
public sealed class MediaException(string code) : Exception(code) { public string Code { get; } = code; }
public sealed record MediaPreparation(double Duration, bool HasVideo);
