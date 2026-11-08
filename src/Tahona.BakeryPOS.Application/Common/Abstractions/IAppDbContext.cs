using Tahona.BakeryPOS.Domain.Cash;
using Tahona.BakeryPOS.Domain.Catalog;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Domain.Identity;
using Tahona.BakeryPOS.Domain.Inventory;
using Tahona.BakeryPOS.Domain.Invoicing;
using Tahona.BakeryPOS.Domain.Payments;
using Tahona.BakeryPOS.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Unidad de trabajo de la aplicación. Una sola base y un solo contexto para que la confirmación de
/// una venta (ventas, pagos, inventario, caja, documento fiscal, outbox) sea una transacción.
/// Cada módulo usa solo sus propios DbSet; las pruebas de arquitectura lo verifican. Lo que un módulo
/// necesita de otro lo pide por los contratos de <c>Common/Contracts</c>.
/// </summary>
public interface IAppDbContext
{
    // Companies
    DbSet<Company> Companies { get; }
    DbSet<Branch> Branches { get; }

    // Identity
    DbSet<User> Users { get; }
    DbSet<Membership> Memberships { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RoleAssignment> RoleAssignments { get; }
    DbSet<Device> Devices { get; }
    DbSet<DevicePairingCode> DevicePairingCodes { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    // Catalog
    DbSet<Category> Categories { get; }
    DbSet<Unit> Units { get; }
    DbSet<Tax> Taxes { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductPrice> ProductPrices { get; }
    DbSet<BranchProduct> BranchProducts { get; }

    // Inventory
    DbSet<StockLocation> StockLocations { get; }
    DbSet<AdjustmentReason> AdjustmentReasons { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<StockBalance> StockBalances { get; }

    // Cash
    DbSet<CashRegister> CashRegisters { get; }
    DbSet<CashSession> CashSessions { get; }
    DbSet<CashMovement> CashMovements { get; }
    DbSet<CashReason> CashReasons { get; }

    // Payments
    DbSet<PaymentMethod> PaymentMethods { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentAttempt> PaymentAttempts { get; }
    DbSet<GatewayAccount> GatewayAccounts { get; }
    DbSet<RegisterTerminal> RegisterTerminals { get; }
    DbSet<Refund> Refunds { get; }

    // Sales
    DbSet<Sale> Sales { get; }
    DbSet<SaleLine> SaleLines { get; }
    DbSet<SaleCounter> SaleCounters { get; }
    DbSet<SaleReturn> SaleReturns { get; }
    DbSet<SaleReturnLine> SaleReturnLines { get; }

    // Invoicing
    DbSet<FiscalSettings> FiscalSettings { get; }
    DbSet<FiscalMapping> FiscalMappings { get; }
    DbSet<FiscalDocument> FiscalDocuments { get; }
    DbSet<FiscalSubmissionAttempt> FiscalSubmissionAttempts { get; }

    /// <summary>
    /// Fija la versión (rowversion) que el cliente leyó: si la fila cambió desde entonces, guardar falla
    /// con CONFLICTO_VERSION (412). Así se implementa If-Match.
    /// </summary>
    void ExpectVersion<T>(T entity, byte[] version) where T : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Nombres de los filtros globales del DbContext. Solo se apaga <see cref="Tenant"/> en flujos sin
/// empresa en sesión que buscan por un secreto (login, refresh, vinculación, PIN de dispositivo).
/// </summary>
public static class QueryFilters
{
    public const string Tenant = "Tenant";
    public const string SoftDelete = "SoftDelete";
}
