using System.Collections.Concurrent;

namespace VideoAtlas.Infrastructure;

public sealed class VideoJobRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> jobs = new();
    public void Register(Guid id, CancellationTokenSource source)
    { if (!jobs.TryAdd(id, source)) throw new InvalidOperationException("Video already processing."); }
    public void Remove(Guid id) => jobs.TryRemove(id, out _);
    public void Cancel(Guid id)
    {
        if (jobs.TryGetValue(id, out var source))
            try { source.Cancel(); } catch (ObjectDisposedException) { /* Job completed concurrently. */ }
    }
}
