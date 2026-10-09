using System.Security.Cryptography;
using System.Text;
using VideoAtlas.Domain;

namespace VideoAtlas.Application;

public sealed class VideoAccess(IVideoStore store, TimeProvider clock)
{
    public async Task<Video> OwnedAsync(Guid id, string owner, CancellationToken ct)
    {
        var video = await store.FindAsync(id, ct);
        return video is not null && video.OwnerId == owner ? video : throw Missing();
    }
    public async Task<Video> SharedAsync(string token, CancellationToken ct, string? viewerId = null)
    {
        if (token.Length != 64 || token.Any(c => !char.IsAsciiHexDigit(c))) throw Missing();
        var video = await store.FindSharedAsync(Hash(token), ct);
        if (video is null || !video.CanShare(clock.GetUtcNow())) throw Missing();
        if (video.ShareRequiresLogin)
        {
            if (viewerId is null) throw new AppException(401, "share_login_required", "Bu paylaşım için izin verilen hesabınızla giriş yapın.");
            if (video.OwnerId != viewerId && !video.ShareViewers.Contains(viewerId, StringComparer.Ordinal)) throw Missing();
        }
        return video;
    }
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static AppException Missing() => new(404, "not_found", "Video bulunamadı veya erişim bağlantısı geçersiz.");
    public static void RequireIdle(Video v)
    { if (v.IsBusy) throw new AppException(409, "video_busy", "İşleme devam ediyor. İşlem bittikten sonra tekrar deneyin."); }
    public static void RequireReady(Video v)
    { if (v.Status != VideoStatus.Ready) throw new AppException(409, "not_ready", "Önce video dökümünün hazırlanması gerekiyor."); }
}
