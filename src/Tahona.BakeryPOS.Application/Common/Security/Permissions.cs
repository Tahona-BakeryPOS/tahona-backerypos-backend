namespace Tahona.BakeryPOS.Application.Common.Security;

/// <summary>
/// Global permission catalog (claim "permission"). Seeded by migration from <see cref="All"/>:
/// a new permission is added here and requires a migration, otherwise nobody can hold it.
/// Codes are English and stable; the display name and module are Spanish because users see them.
/// There is no <c>sales.sync</c> permission: the system has no offline mode.
/// </summary>
public static class Permissions
{
    // Platform (never assigned to a company role)
    public const string PlatformAdmin = "platform.admin";

    // Company
    public const string OrganizationManage = "organization.manage";
    public const string ReportsView = "reports.view";

    // Catalog
    public const string ProductsView = "products.view";
    public const string CatalogManage = "catalog.manage";
    public const string BranchPrices = "prices.branch";

    // Inventory
    public const string InventoryView = "inventory.view";
    public const string InventoryAdjust = "inventory.adjust";

    // Cash
    public const string ShiftsOpen = "shifts.open";
    public const string ShiftsClose = "shifts.close";
    public const string CashMovements = "cash.movements";
    public const string CashWithdrawal = "cash.withdrawal";

    // Sales and returns
    public const string SalesCreate = "sales.create";
    public const string SalesView = "sales.view";
    public const string SalesVoid = "sales.void";
    public const string SalesDiscount = "sales.discount";
    public const string ReturnsCreate = "returns.create";
    public const string ReturnsApprove = "returns.approve";

    // Payments
    public const string PaymentsResolve = "payments.resolve";

    // Electronic invoicing
    public const string FiscalManage = "fiscal.manage";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(PlatformAdmin, "Administrar la plataforma", "Plataforma"),
        new(OrganizationManage, "Gestionar empresa, sucursales, cajas y usuarios", "Empresa"),
        new(ReportsView, "Ver reportes", "Empresa"),
        new(ProductsView, "Ver productos", "Catálogo"),
        new(CatalogManage, "Gestionar catálogo y precios base", "Catálogo"),
        new(BranchPrices, "Gestionar precios y disponibilidad de la sucursal", "Catálogo"),
        new(InventoryView, "Ver existencias y movimientos", "Inventario"),
        new(InventoryAdjust, "Registrar ajustes de inventario", "Inventario"),
        new(ShiftsOpen, "Abrir caja", "Caja"),
        new(ShiftsClose, "Cerrar caja", "Caja"),
        new(CashMovements, "Registrar ingresos de efectivo", "Caja"),
        new(CashWithdrawal, "Registrar retiros de efectivo", "Caja"),
        new(SalesCreate, "Registrar ventas", "Ventas"),
        new(SalesView, "Ver ventas", "Ventas"),
        new(SalesVoid, "Anular ventas", "Ventas"),
        new(SalesDiscount, "Aplicar descuentos en la venta", "Ventas"),
        new(ReturnsCreate, "Registrar devoluciones", "Ventas"),
        new(ReturnsApprove, "Aprobar devoluciones y reembolsos", "Ventas"),
        new(PaymentsResolve, "Resolver a mano pagos sin respuesta de la pasarela", "Pagos"),
        new(FiscalManage, "Gestionar facturación electrónica", "Facturación"),
    ];
}

public sealed record PermissionDefinition(string Code, string Name, string Module);
