using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VideoAtlas.Application;
using VideoAtlas.Domain;

namespace VideoAtlas.Infrastructure;

public sealed class VideoStore(AppDbContext db, TimeProvider clock) : IVideoStore, ILibraryStore
{
    public Task<Video?> FindAsync(Guid id, CancellationToken ct) => db.Videos.FirstOrDefaultAsync(v => v.Id == id, ct);
    public Task<Video?> ReloadAsync(Guid id, CancellationToken ct)
    { db.ChangeTracker.Clear(); return FindAsync(id, ct); }
    public Task<Video?> FindSharedAsync(string hash, CancellationToken ct) => db.Videos.FirstOrDefaultAsync(v => v.ShareHash == hash, ct);
    public async Task<IReadOnlyList<VideoSummary>> ListAsync(string ownerId, int page, CancellationToken ct) =>
        await db.Videos.AsNoTracking().Where(v => v.OwnerId == ownerId).OrderByDescending(v => v.CreatedAt)
            .Skip(Math.Clamp(page, 0, 10000) * 24).Take(24)
            .Select(v => new VideoSummary(v.Id, v.Title, v.CreatedAt, v.Status, v.Stage, v.Progress, v.Duration, v.MediaReady, v.ErrorCode))
            .ToListAsync(ct);
    public async Task AddAsync(Video video, CancellationToken ct) { db.Videos.Add(video); await db.SaveChangesAsync(ct); }
    public async Task SaveAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
    public async Task<long> UsedBytesAsync(string ownerId, CancellationToken ct) =>
        await db.Videos.Where(v => v.OwnerId == ownerId).SumAsync(v => v.SourceBytes, ct);
    public async Task DeleteAsync(Video video, CancellationToken ct) { db.Videos.Remove(video); await db.SaveChangesAsync(ct); }
    public async Task<bool> ReserveChatAsync(Guid id, int limit, CancellationToken ct)
    {
        var day = clock.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // One conditional SQL statement, atomic across concurrent owner and public chat requests.
        return await db.Videos.Where(v => v.Id == id && (v.ChatDay != day || v.DailyChatCount < limit))
            .ExecuteUpdateAsync(set => set.SetProperty(v => v.DailyChatCount, v => v.ChatDay == day ? v.DailyChatCount + 1 : 1)
                .SetProperty(v => v.ChatDay, day), ct) == 1;
    }
}
