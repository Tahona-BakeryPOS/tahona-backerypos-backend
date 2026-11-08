using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Companies;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Companies;

/// <summary>Lo que otros módulos pueden consultar de Companies.</summary>
public sealed class CompanyDirectory : ICompanyDirectory, IBranchDirectory
{
    private readonly IAppDbContext _db;

    public CompanyDirectory(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CompanySummary>> GetActiveAsync(IReadOnlyCollection<Guid> companyIds, CancellationToken ct)
    {
        if (companyIds.Count == 0) return [];
        var rows = await _db.Companies.AsNoTracking()
            .Where(c => companyIds.Contains(c.Id) && c.Status == CompanyStatus.Active)
            .Select(c => new { c.Id, c.TradeName, c.LegalName })
            .ToListAsync(ct);
        return rows.Select(c => new CompanySummary(c.Id, c.TradeName ?? c.LegalName)).OrderBy(c => c.Name).ToList();
    }

    public Task<BranchSummary?> FindAsync(Guid branchId, CancellationToken ct) =>
        _db.Branches.AsNoTracking()
            .Where(b => b.Id == branchId)
            .Select(b => new BranchSummary(b.Id, b.Code, b.Name, b.IsActive))
            .FirstOrDefaultAsync(ct);
}
