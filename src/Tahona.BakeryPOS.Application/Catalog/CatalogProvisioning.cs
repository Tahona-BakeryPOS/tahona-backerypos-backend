using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Catalog;

namespace Tahona.BakeryPOS.Application.Catalog;

/// <summary>
/// Catalog a company is born with (US-1.1): the taxes of <see cref="DefaultTaxes"/> and the
/// "General" category, so products can be created from day one. Assumptions to confirm (I-20).
/// </summary>
public sealed class CatalogProvisioning : ICatalogProvisioning
{
    public const string DefaultCategory = "General";

    private readonly IAppDbContext _db;

    public CatalogProvisioning(IAppDbContext db)
    {
        _db = db;
    }

    public void ProvisionCompany(Guid companyId)
    {
        foreach (var (code, name, kind, rate) in DefaultTaxes.All)
            _db.Taxes.Add(Tax.Create(companyId, code, name, kind, rate));
        _db.Categories.Add(Category.Create(companyId, DefaultCategory, 0));
    }
}
