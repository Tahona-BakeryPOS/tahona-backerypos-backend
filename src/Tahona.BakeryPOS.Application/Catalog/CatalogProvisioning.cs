using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Catalog;

namespace Tahona.BakeryPOS.Application.Catalog;

/// <summary>
/// Catálogo con que nace una empresa (HU-1.1): los impuestos de <see cref="DefaultTaxes"/> y la
/// categoría "General", para poder crear productos desde el primer día. Supuestos por confirmar (I-20).
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
