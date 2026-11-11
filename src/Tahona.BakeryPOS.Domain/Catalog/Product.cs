using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Catalog;

/// <summary>What happens if a sale or an adjustment would leave the balance below zero (D-06).</summary>
public enum NegativeStockPolicy
{
    /// <summary>A negative balance is allowed (the day's bread that is sold before its entry is recorded).</summary>
    Allow,
    /// <summary>It is blocked, unless a supervisor authorizes with a PIN (it is audited).</summary>
    Block,
}

/// <summary>
/// Sellable product (US-3.1). SKU unique per company and immutable; barcode unique per company
/// if present. It is never deleted: it is deactivated (sales reference it).
/// </summary>
public sealed class Product : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public Guid CategoryId { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid TaxId { get; private set; }
    public string? Barcode { get; private set; }
    /// <summary>Whether the sale deducts stock (phase 4). False for products made to order.</summary>
    public bool TrackStock { get; private set; }
    /// <summary>Only applies with <see cref="TrackStock"/>.</summary>
    public NegativeStockPolicy NegativeStockPolicy { get; private set; } = NegativeStockPolicy.Allow;
    public bool IsActive { get; private set; } = true;

    private Product() { }

    public static Product Create(Guid companyId, string sku, string name, Guid categoryId, Guid unitId, Guid taxId,
        string? barcode, bool trackStock, NegativeStockPolicy negativeStockPolicy = NegativeStockPolicy.Allow)
    {
        if (companyId == Guid.Empty)
            throw new DomainException("VALIDATION", "El producto debe pertenecer a una empresa.");
        if (string.IsNullOrWhiteSpace(sku))
            throw new DomainException("VALIDATION", "El código del producto es obligatorio.");
        var product = new Product { CompanyId = companyId, Sku = NormalizeSku(sku) };
        product.Update(name, categoryId, unitId, taxId, barcode, trackStock, negativeStockPolicy);
        return product;
    }

    public void Update(string name, Guid categoryId, Guid unitId, Guid taxId, string? barcode, bool trackStock,
        NegativeStockPolicy negativeStockPolicy = NegativeStockPolicy.Allow)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDATION", "El nombre del producto es obligatorio.");
        if (categoryId == Guid.Empty || unitId == Guid.Empty || taxId == Guid.Empty)
            throw new DomainException("VALIDATION", "Categoría, unidad e impuesto son obligatorios.");
        Name = name.Trim();
        CategoryId = categoryId;
        UnitId = unitId;
        TaxId = taxId;
        Barcode = NormalizeBarcode(barcode);
        TrackStock = trackStock;
        NegativeStockPolicy = negativeStockPolicy;
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    public static string? NormalizeBarcode(string? barcode) => string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
}

/// <summary>
/// Price with validity (US-3.2). <see cref="BranchId"/> null = the company's base price. A change opens
/// a new validity period and closes the previous one; none is deleted or overlaps. With D-07 the list price
/// includes the tax (<see cref="IncludesTax"/>).
/// </summary>
public sealed class ProductPrice : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? BranchId { get; private set; }
    public decimal Amount { get; private set; }
    public bool IncludesTax { get; private set; }
    public DateTime ValidFrom { get; private set; }
    /// <summary>Exclusive end; null = in force with no end.</summary>
    public DateTime? ValidTo { get; private set; }

    private ProductPrice() { }

    public bool IsValidAt(DateTime at) => ValidFrom <= at && (ValidTo is null || ValidTo > at);

    /// <summary>
    /// Opens a new validity period over the price history of the same product and branch. It must start
    /// after the last existing validity; the one still open is closed where the new one starts.
    /// </summary>
    public static ProductPrice Open(Guid companyId, Guid productId, Guid? branchId, decimal amount, bool includesTax,
        DateTime validFrom, IReadOnlyCollection<ProductPrice> history)
    {
        if (amount < 0)
            throw new DomainException("VALIDATION", "El precio no puede ser negativo.");
        if (decimal.Round(amount, 2) != amount)
            throw new DomainException("VALIDATION", "El precio admite máximo 2 decimales.");
        if (history.Any(p => p.ProductId != productId || p.BranchId != branchId))
            throw new DomainException("VALIDATION", "El historial no corresponde al producto y sucursal.");
        var latest = history.MaxBy(p => p.ValidFrom);
        if (latest is not null && validFrom <= latest.ValidFrom)
            throw new DomainException("VALIDATION",
                $"Ya hay un precio que rige desde {latest.ValidFrom:yyyy-MM-dd HH:mm} UTC; la nueva vigencia debe empezar después.");

        foreach (var open in history.Where(p => p.ValidTo is null || p.ValidTo > validFrom))
            open.ValidTo = validFrom;

        return new ProductPrice
        {
            CompanyId = companyId, ProductId = productId, BranchId = branchId,
            Amount = amount, IncludesTax = includesTax, ValidFrom = validFrom,
        };
    }

    /// <summary>The price in force at a branch: the branch's if there is a current one; otherwise the base one.</summary>
    public static ProductPrice? EffectiveAt(IEnumerable<ProductPrice> prices, Guid? branchId, DateTime at)
    {
        var valid = prices.Where(p => p.IsValidAt(at)).ToList();
        return (branchId is null ? null : valid.FirstOrDefault(p => p.BranchId == branchId))
            ?? valid.FirstOrDefault(p => p.BranchId is null);
    }
}

/// <summary>Availability of a product at a branch (US-3.3). No row = available.</summary>
public sealed class BranchProduct : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public bool IsAvailable { get; private set; }

    private BranchProduct() { }

    public static BranchProduct Create(Guid companyId, Guid branchId, Guid productId, bool isAvailable) =>
        new() { CompanyId = companyId, BranchId = branchId, ProductId = productId, IsAvailable = isAvailable };

    public void SetAvailable(bool isAvailable) => IsAvailable = isAvailable;
}
