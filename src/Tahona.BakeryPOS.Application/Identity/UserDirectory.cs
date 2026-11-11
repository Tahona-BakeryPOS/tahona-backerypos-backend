using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity;

/// <summary>
/// User names to display in histories. It only returns users with a membership in the company
/// in session: an id of another company does not reveal its name.
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
