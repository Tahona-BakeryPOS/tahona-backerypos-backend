using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Catalog;

/// <summary>Qué pasa si una venta o un ajuste dejaría el saldo por debajo de cero (D-06).</summary>
public enum NegativeStockPolicy
{
    /// <summary>Se permite el saldo negativo (pan del día que se vende antes de registrar su entrada).</summary>
    Allow,
    /// <summary>Se bloquea, salvo autorización con PIN de un superior (queda auditada).</summary>
    Block,
}

/// <summary>
/// Producto vendible (HU-3.1). SKU único por empresa e inmutable; código de barras único por empresa
/// si existe. Nunca se borra: se desactiva (las ventas lo referencian).
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
    /// <summary>Si la venta descuenta existencias (fase 4). Falso para productos de elaboración al momento.</summary>
    public bool TrackStock { get; private set; }
    /// <summary>Solo aplica con <see cref="TrackStock"/>.</summary>
    public NegativeStockPolicy NegativeStockPolicy { get; private set; } = NegativeStockPolicy.Allow;
    public bool IsActive { get; private set; } = true;

    private Product() { }

    public static Product Create(Guid companyId, string sku, string name, Guid categoryId, Guid unitId, Guid taxId,
        string? barcode, bool trackStock, NegativeStockPolicy negativeStockPolicy = NegativeStockPolicy.Allow)
    {
        if (companyId == Guid.Empty)
            throw new DomainException("VALIDACION", "El producto debe pertenecer a una empresa.");
        if (string.IsNullOrWhiteSpace(sku))
            throw new DomainException("VALIDACION", "El código del producto es obligatorio.");
        var product = new Product { CompanyId = companyId, Sku = NormalizeSku(sku) };
        product.Update(name, categoryId, unitId, taxId, barcode, trackStock, negativeStockPolicy);
        return product;
    }

    public void Update(string name, Guid categoryId, Guid unitId, Guid taxId, string? barcode, bool trackStock,
        NegativeStockPolicy negativeStockPolicy = NegativeStockPolicy.Allow)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VALIDACION", "El nombre del producto es obligatorio.");
        if (categoryId == Guid.Empty || unitId == Guid.Empty || taxId == Guid.Empty)
            throw new DomainException("VALIDACION", "Categoría, unidad e impuesto son obligatorios.");
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
/// Precio con vigencia (HU-3.2). <see cref="BranchId"/> null = precio base de la empresa. Un cambio abre
/// una vigencia nueva y cierra la anterior; ninguna se borra ni se solapa. Con D-07 el precio de lista
/// incluye el impuesto (<see cref="IncludesTax"/>).
/// </summary>
public sealed class ProductPrice : AuditableEntity, ITenantEntity
{
    public Guid CompanyId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? BranchId { get; private set; }
    public decimal Amount { get; private set; }
    public bool IncludesTax { get; private set; }
    public DateTime ValidFrom { get; private set; }
    /// <summary>Fin exclusivo; null = vigente sin fin.</summary>
    public DateTime? ValidTo { get; private set; }

    private ProductPrice() { }

    public bool IsValidAt(DateTime at) => ValidFrom <= at && (ValidTo is null || ValidTo > at);

    /// <summary>
    /// Abre una vigencia nueva sobre el historial de precios del mismo producto y sucursal. Debe empezar
    /// después de la última vigencia existente; la que siga abierta se cierra donde empieza la nueva.
    /// </summary>
    public static ProductPrice Open(Guid companyId, Guid productId, Guid? branchId, decimal amount, bool includesTax,
        DateTime validFrom, IReadOnlyCollection<ProductPrice> history)
    {
        if (amount < 0)
            throw new DomainException("VALIDACION", "El precio no puede ser negativo.");
        if (decimal.Round(amount, 2) != amount)
            throw new DomainException("VALIDACION", "El precio admite máximo 2 decimales.");
        if (history.Any(p => p.ProductId != productId || p.BranchId != branchId))
            throw new DomainException("VALIDACION", "El historial no corresponde al producto y sucursal.");
        var latest = history.MaxBy(p => p.ValidFrom);
        if (latest is not null && validFrom <= latest.ValidFrom)
            throw new DomainException("VALIDACION",
                $"Ya hay un precio que rige desde {latest.ValidFrom:yyyy-MM-dd HH:mm} UTC; la nueva vigencia debe empezar después.");

        foreach (var open in history.Where(p => p.ValidTo is null || p.ValidTo > validFrom))
            open.ValidTo = validFrom;

        return new ProductPrice
        {
            CompanyId = companyId, ProductId = productId, BranchId = branchId,
            Amount = amount, IncludesTax = includesTax, ValidFrom = validFrom,
        };
    }

    /// <summary>Precio que rige en una sucursal: el de la sucursal si hay uno vigente; si no, el base.</summary>
    public static ProductPrice? EffectiveAt(IEnumerable<ProductPrice> prices, Guid? branchId, DateTime at)
    {
        var valid = prices.Where(p => p.IsValidAt(at)).ToList();
        return (branchId is null ? null : valid.FirstOrDefault(p => p.BranchId == branchId))
            ?? valid.FirstOrDefault(p => p.BranchId is null);
    }
}

/// <summary>Disponibilidad de un producto en una sucursal (HU-3.3). Sin fila = disponible.</summary>
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
