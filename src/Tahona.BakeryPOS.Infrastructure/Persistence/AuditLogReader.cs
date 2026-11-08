using Tahona.BakeryPOS.Application.Auditing;
using Tahona.BakeryPOS.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>
/// Consulta <c>aud.audit_logs</c>. La tabla no tiene filtro global por empresa (también guarda cambios
/// de plataforma), así que la empresa se filtra aquí, siempre.
/// </summary>
internal sealed class AuditLogReader : IAuditLogReader
{
    private readonly AppDbContext _db;

    public AuditLogReader(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<AuditLogDto>> ReadAsync(AuditLogFilter f, CancellationToken ct)
    {
        var query = _db.AuditLogs.AsNoTracking().Where(a => a.CompanyId == f.CompanyId);
        if (!string.IsNullOrWhiteSpace(f.Entity)) query = query.Where(a => a.Entity == f.Entity);
        if (f.EntityId is { } id) query = query.Where(a => a.EntityId == id);
        if (f.From is { } from) query = query.Where(a => a.OccurredAt >= from);
        if (f.To is { } to) query = query.Where(a => a.OccurredAt <= to);

        var total = await query.CountAsync(ct);
        // Usuarios globales (sin filtro por empresa): solo se lee el nombre de quien hizo el cambio.
        var items = await query
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Skip((f.Page - 1) * f.PageSize).Take(f.PageSize)
            .Select(a => new AuditLogDto(a.Id, a.OccurredAt, a.UserId,
                _db.Users.IgnoreQueryFilters().Where(u => u.Id == a.UserId).Select(u => u.FullName).FirstOrDefault(),
                a.Entity, a.EntityId, a.Action, a.OldValues, a.NewValues))
            .ToListAsync(ct);
        return new PagedResult<AuditLogDto>(items, f.Page, f.PageSize, total);
    }

    public async Task<IReadOnlyList<string>> EntitiesAsync(Guid companyId, CancellationToken ct) =>
        await _db.AuditLogs.AsNoTracking().Where(a => a.CompanyId == companyId)
            .Select(a => a.Entity).Distinct().OrderBy(e => e).ToListAsync(ct);
}
