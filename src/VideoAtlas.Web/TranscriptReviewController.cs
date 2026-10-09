using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VideoAtlas.Application;

namespace VideoAtlas.Web;

[Authorize, ApiController, Route("api/videos/{id:guid}/review"), RequestSizeLimit(65536)]
public sealed class TranscriptReviewController(VideoAccess access, TranscriptReviewService review) : ControllerBase
{
    private string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct) => Ok(TranscriptReviewService.State(await access.OwnedAsync(id, Owner, ct)));
    [HttpPost("context")]
    public async Task<IActionResult> Context(Guid id, ContextCommand command, CancellationToken ct)
    { await review.SaveContextAsync(id, Owner, command, ct); return NoContent(); }
    [HttpPost("check")]
    public async Task<IActionResult> Check(Guid id, ReviewCommand command, CancellationToken ct)
    { await review.CheckAsync(id, Owner, command.Version, ct); return NoContent(); }
    [HttpPost("segments/{segmentId:int}/ai"), EnableRateLimiting("review")]
    public async Task<IActionResult> Inspect(Guid id, int segmentId, ReviewCommand command, CancellationToken ct)
    { await review.ProposeAsync(id, Owner, segmentId, command.Version, false, ct); return NoContent(); }
    [HttpPost("segments/{segmentId:int}/retranscribe"), EnableRateLimiting("review")]
    public async Task<IActionResult> Retranscribe(Guid id, int segmentId, ReviewCommand command, CancellationToken ct)
    { await review.ProposeAsync(id, Owner, segmentId, command.Version, true, ct); return NoContent(); }
    [HttpPost("edit")]
    public async Task<IActionResult> Edit(Guid id, EditTranscriptCommand command, CancellationToken ct)
    { await review.EditAsync(id, Owner, command, ct); return NoContent(); }
    [HttpPost("chapters/edit")]
    public async Task<IActionResult> EditChapter(Guid id, EditChapterCommand command, CancellationToken ct)
    { await review.EditChapterAsync(id, Owner, command, ct); return NoContent(); }
    [HttpPost("segments/{segmentId:int}/compare"), EnableRateLimiting("review")]
    public async Task<IActionResult> Compare(Guid id, int segmentId, ReviewCommand command, CancellationToken ct)
    { await review.ProposeAsync(id, Owner, segmentId, command.Version, true, ct, secondary: true); return NoContent(); }
    [HttpPost("segments/{segmentId:int}/align"), EnableRateLimiting("review")]
    public async Task<IActionResult> Align(Guid id, int segmentId, ReviewCommand command, CancellationToken ct)
    { await review.ProposeAsync(id, Owner, segmentId, command.Version, false, ct, align: true); return NoContent(); }
    [HttpPost("issues/{issueId:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, Guid issueId, ReviewCommand command, CancellationToken ct)
    { await review.DismissAsync(id, Owner, issueId, command.Version, ct); return NoContent(); }
    [HttpPost("restore/{number:int}")]
    public async Task<IActionResult> Restore(Guid id, int number, ReviewCommand command, CancellationToken ct)
    { await review.RestoreAsync(id, Owner, number, command.Version, ct); return NoContent(); }
}
