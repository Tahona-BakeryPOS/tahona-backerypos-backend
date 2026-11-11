using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Catalog;

/// <summary>Product category: the tabs of the sale screen, in the indicated order.</summary>
public sealed class Category : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Category() { }

    public static Category Create(Guid companyId, string name, int displayOrder)
    {
        if (companyId == Guid.Empty)
            throw new DomainException("VALIDATION", "La categoría debe pertenecer a una empresa.");
        var category = new Category { CompanyId = companyId };
        category.Update(name, displayOrder, isActive: true);
        return category;
    }

    public void Update(string name, int displayOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDATION", "El nombre de la categoría es obligatorio.");
        if (displayOrder < 0)
            throw new DomainException("VALIDATION", "El orden no puede ser negativo.");
        Name = name.Trim();
        DisplayOrder = displayOrder;
        IsActive = isActive;
    }
}
