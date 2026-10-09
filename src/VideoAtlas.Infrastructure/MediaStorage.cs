using Microsoft.Extensions.Options;
using VideoAtlas.Application;

namespace VideoAtlas.Infrastructure;

public sealed class MediaStorage(IOptions<StorageOptions> settings) : IMediaStorage
{
    private readonly string root = Path.GetFullPath(settings.Value.Root);
    public string PathFor(Guid id, string name)
    {
        if (name != Path.GetFileName(name) || name.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage name", nameof(name));
        return Path.Combine(root, "videos", id.ToString("N"), name);
    }
    public async Task SaveUploadAsync(Guid id, Stream content, CancellationToken ct)
    {
        var path = PathFor(id, "source.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await content.CopyToAsync(output, ct);
    }
    public void Delete(Guid id)
    {
        var path = Path.GetDirectoryName(PathFor(id, "source.bin"))!;
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
