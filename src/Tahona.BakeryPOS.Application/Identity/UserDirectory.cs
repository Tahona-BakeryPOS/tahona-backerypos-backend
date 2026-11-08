using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity;

/// <summary>
/// Nombres de usuarios para mostrar en historiales. Solo devuelve usuarios con pertenencia en la empresa
/// en sesión: un id de otra empresa no revela su nombre.
/// </summary>
public sealed class UserDirectory : IUserDirectory
{
    private readonly IAppDbContext _db;

    public UserDirectory(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();
        return await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && _db.Memberships.Any(m => m.UserId == u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
    }
}
