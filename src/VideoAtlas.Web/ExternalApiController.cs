using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VideoAtlas.Application;
using VideoAtlas.Domain;
using VideoAtlas.Infrastructure;

namespace VideoAtlas.Web;

public sealed class ExternalAnalysisForm
{
    public IFormFile File { get; set; } = null!;
    public string Title { get; set; } = "";
    public bool Consent { get; set; }
    public string Language { get; set; } = "tr";
    public bool FullReview { get; set; } = true;
    public bool SpeakerDiarization { get; set; }
    public int? SpeakerCount { get; set; }
    public string? Terms { get; set; }
    public string? Instructions { get; set; }
}

public sealed record ExternalAnalysisList(IReadOnlyList<ExternalAnalysisSummary> Items, int Page, int PageSize, int Total);
public sealed record ExternalAnalysisSummary(Guid Id, string Title, VideoStatus Status, string Stage, int Progress,
    DateTimeOffset CreatedAt, double Duration, string MediaKind, string? ErrorCode);
public sealed record ExternalAnalysisResult(ExternalAnalysisSummary Analysis, IReadOnlyList<TranscriptSegment> Transcript,
    IReadOnlyList<Chapter> Topics, IReadOnlyDictionary<string, string> Speakers, int TranscriptVersion);

[ApiController, Route("api/v1/analyses"), IgnoreAntiforgeryToken, EnableRateLimiting("external-api")]
[Authorize(AuthenticationSchemes = ApiKeyDefaults.Scheme)]
public sealed class ExternalApiController(AppDbContext db, VideoLibrary library, VideoAccess access,
    IVideoStore store, IVideoIntelligence intelligence, VideoJobRegistry jobs) : ControllerBase
{
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private Guid CredentialId => Guid.Parse(User.FindFirstValue(ApiKeyDefaults.CredentialIdClaim)!);

    [HttpPost, Authorize(Policy = "ExternalApiWrite"), RequestSizeLimit(538_000_000), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Create([FromForm] ExternalAnalysisForm form, CancellationToken ct)
    {
        if (form.File is null) throw new AppException(400, "file_required", "A video or audio file is required.");
        if (form.SpeakerDiarization && !await intelligence.SpeakerDiarizationConfiguredAsync(ct))
            throw new AppException(409, "diarization_unsupported", "The active speech provider does not support speaker diarization.");
        var terms = (form.Terms ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            var pair = line.Split('|', 2);
            return new VideoTerm(pair[0], pair.Length == 2 ? pair[1] : "");
        }).ToArray();
        var context = TranscriptQuality.ValidateContext(terms, form.Instructions ?? "", form.Language,
            form.SpeakerDiarization, form.SpeakerCount);
        await using var stream = form.File.OpenReadStream();
        var video = await library.UploadAsync(new(Owner, form.Title, form.File.Length, form.Consent, stream, context,
            form.FullReview, CredentialId), ct);
        HttpContext.Items["ExternalApiVideoId"] = video.Id;
        return AcceptedAtAction(nameof(Get), new { id = video.Id }, new
        {
            video.Id, video.Status, video.Stage, video.Progress,
            statusUrl = Url.ActionLink(nameof(Get), values: new { id = video.Id }),
            resultUrl = Url.ActionLink(nameof(Result), values: new { id = video.Id }),
            subtitlesUrl = Url.ActionLink(nameof(Subtitles), values: new { id = video.Id })
        });
    }

    [HttpGet, Authorize(Policy = "ExternalApiRead")]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.Videos.AsNoTracking().Where(v => v.OwnerId == Owner && v.CreatedVia == "api");
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(v => v.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(v => new ExternalAnalysisSummary(v.Id, v.Title, v.Status, v.Stage, v.Progress, v.CreatedAt,
                v.Duration, v.MediaKind, v.ErrorCode)).ToListAsync(ct);
        return Ok(new ExternalAnalysisList(items, page, pageSize, total));
    }

    [HttpGet("{id:guid}"), Authorize(Policy = "ExternalApiRead")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var v = await ApiVideoAsync(id, ct);
        return Ok(Summary(v));
    }

    [HttpGet("{id:guid}/result"), Authorize(Policy = "ExternalApiRead")]
    public async Task<IActionResult> Result(Guid id, CancellationToken ct)
    {
        var v = await ApiVideoAsync(id, ct);
        if (v.Status != VideoStatus.Ready) throw new AppException(409, "analysis_not_ready", "The analysis result is not ready.");
        return Ok(new ExternalAnalysisResult(Summary(v), v.Segments, v.Chapters, v.SpeakerNames, v.TranscriptVersion));
    }

    [HttpGet("{id:guid}/transcript"), Authorize(Policy = "ExternalApiRead")]
    public async Task<IActionResult> Transcript(Guid id, CancellationToken ct)
    {
        var v = await ApiVideoAsync(id, ct);
        if (v.Status != VideoStatus.Ready) throw new AppException(409, "analysis_not_ready", "The transcript is not ready.");
        return Ok(new { v.Id, v.TranscriptVersion, segments = v.Segments, speakers = v.SpeakerNames });
    }

    [HttpGet("{id:guid}/subtitles.srt"), Authorize(Policy = "ExternalApiRead")]
    public async Task<IActionResult> Subtitles(Guid id, CancellationToken ct)
    {
        var v = await ApiVideoAsync(id, ct); VideoAccess.RequireReady(v);
        return File(Encoding.UTF8.GetBytes(Application.Subtitles.Format(v.Segments, false, v.SpeakerNames)),
            "application/x-subrip; charset=utf-8", $"{v.Id}.srt");
    }

    [HttpPost("{id:guid}/cancel"), Authorize(Policy = "ExternalApiWrite"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var v = await ApiVideoAsync(id, ct);
        if (!v.IsBusy) throw new AppException(409, "not_processing", "The analysis is not processing.");
        v.RequestCancellation(); if (v.Status == VideoStatus.Queued) v.Cancel();
        await store.SaveAsync(ct); jobs.Cancel(id); return Accepted();
    }

    [HttpPost("{id:guid}/retry"), Authorize(Policy = "ExternalApiWrite"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    { await library.RetryAsync(await ApiVideoAsync(id, ct), ct); return Accepted(); }

    [HttpPost("{id:guid}/reanalyze"), Authorize(Policy = "ExternalApiWrite"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Reanalyze(Guid id, ReanalysisForm form, CancellationToken ct)
    {
        if (form.SpeakerDiarization && !await intelligence.SpeakerDiarizationConfiguredAsync(ct))
            throw new AppException(409, "diarization_unsupported", "The active speech provider does not support speaker diarization.");
        await library.ReanalyzeAsync(await ApiVideoAsync(id, ct), new(form.SpeakerDiarization, form.SpeakerCount), ct);
        return Accepted();
    }

    [HttpPut("{id:guid}/speakers"), Authorize(Policy = "ExternalApiWrite"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Speakers(Guid id, SpeakerNamesForm form, CancellationToken ct)
    { await library.SaveSpeakerNamesAsync(await ApiVideoAsync(id, ct), form.Names, ct); return NoContent(); }

    [HttpPost("{id:guid}/subtitles"), Authorize(Policy = "ExternalApiWrite"), RequestSizeLimit(2_100_000), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> ImportSubtitles(Guid id, SubtitleForm form, CancellationToken ct)
    { await library.ImportAsync(await ApiVideoAsync(id, ct), form.Content, ct); return NoContent(); }

    [HttpDelete("{id:guid}"), Authorize(Policy = "ExternalApiDelete"), TypeFilter(typeof(MutationFilter))]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    { await library.DeleteAsync(await ApiVideoAsync(id, ct), ct); return NoContent(); }

    private async Task<Video> ApiVideoAsync(Guid id, CancellationToken ct)
    {
        var v = await access.OwnedAsync(id, Owner, ct);
        return v.CreatedVia == "api" ? v : throw VideoAccess.Missing();
    }
    private static ExternalAnalysisSummary Summary(Video v) => new(v.Id, v.Title, v.Status, v.Stage, v.Progress,
        v.CreatedAt, v.Duration, v.MediaKind, v.ErrorCode);
}

[ApiController, Route("api/v1"), IgnoreAntiforgeryToken]
public sealed class ExternalApiDiscoveryController : ControllerBase
{
    [AllowAnonymous, HttpGet]
    public IActionResult Index() => Ok(new
    {
        name = "VideoAtlas Analysis API", version = "v1", authentication = "Authorization: Bearer vat_<prefix>.<secret>",
        documentation = "/api/v1/openapi.json", endpoints = new[] { "POST /api/v1/analyses", "GET /api/v1/analyses", "GET /api/v1/analyses/{id}",
            "GET /api/v1/analyses/{id}/result", "GET /api/v1/analyses/{id}/transcript", "GET /api/v1/analyses/{id}/subtitles.srt",
            "POST /api/v1/analyses/{id}/cancel", "POST /api/v1/analyses/{id}/retry", "POST /api/v1/analyses/{id}/reanalyze",
            "PUT /api/v1/analyses/{id}/speakers", "POST /api/v1/analyses/{id}/subtitles", "DELETE /api/v1/analyses/{id}" }
    });

    [AllowAnonymous, HttpGet("openapi.json")]
    public IActionResult OpenApi() => Ok(new
    {
        openapi = "3.0.3",
        info = new { title = "VideoAtlas Analysis API", version = "1.0.0", description = "Owner-scoped video and audio analysis API." },
        components = new
        {
            securitySchemes = new Dictionary<string, object>
            {
                ["bearerAuth"] = new { type = "http", scheme = "bearer", bearerFormat = "VideoAtlas API credential" }
            }
        },
        security = new[] { new Dictionary<string, string[]> { ["bearerAuth"] = [] } },
        paths = new Dictionary<string, object>
        {
            ["/api/v1/analyses"] = new
            {
                get = new { summary = "List analyses", description = "Returns only API-created analyses owned by the credential user." },
                post = new { summary = "Create analysis", description = "multipart/form-data: file, title, consent, language, fullReview, speakerDiarization, speakerCount, terms, instructions." }
            },
            ["/api/v1/analyses/{id}"] = new { get = new { summary = "Get status" }, delete = new { summary = "Delete analysis" } },
            ["/api/v1/analyses/{id}/result"] = new { get = new { summary = "Get complete result" } },
            ["/api/v1/analyses/{id}/transcript"] = new { get = new { summary = "Get structured transcript" } },
            ["/api/v1/analyses/{id}/subtitles.srt"] = new { get = new { summary = "Download SRT" } },
            ["/api/v1/analyses/{id}/cancel"] = new { post = new { summary = "Cancel processing" } },
            ["/api/v1/analyses/{id}/retry"] = new { post = new { summary = "Retry processing" } },
            ["/api/v1/analyses/{id}/reanalyze"] = new { post = new { summary = "Run a full reanalysis" } },
            ["/api/v1/analyses/{id}/speakers"] = new { put = new { summary = "Assign speaker names" } },
            ["/api/v1/analyses/{id}/subtitles"] = new { post = new { summary = "Import an SRT transcript" } }
        }
    });
}
