using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using VideoAtlas.Application;

namespace VideoAtlas.Infrastructure;

public sealed class ShareAudience(AppDbContext db) : IShareAudience
{
    public async Task<IReadOnlyList<string>> ResolveAsync(IReadOnlyList<string> emails, CancellationToken ct)
    {
        if (emails.Any(e => e is null || e.Length > 254 || !MailAddress.TryCreate(e, out var address) || address.Address != e.Trim()))
            throw new AppException(400, "invalid_audience", "Geçerli hesap e-postaları girin.");
        var normalized = emails.Select(e => e.Trim().ToUpperInvariant()).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(u => normalized.Contains(u.NormalizedEmail)).Select(u => u.Id).ToArrayAsync(ct);
        if (users.Length != normalized.Length) throw new AppException(400, "invalid_audience", "Tüm alıcıların uygulamada mevcut hesabı olmalıdır.");
        return users;
    }
}
