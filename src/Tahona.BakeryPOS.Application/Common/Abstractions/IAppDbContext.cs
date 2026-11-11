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
/// The application's unit of work. A single database and a single context so that confirming
/// a sale (sales, payments, inventory, cash, fiscal document, outbox) is one transaction.
/// Each module uses only its own DbSets; the architecture tests verify it. What a module
/// needs from another it asks for through the contracts in <c>Common/Contracts</c>.
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
    /// Sets the version (rowversion) the client read: if the row changed since then, saving fails
    /// with CONFLICTO_VERSION (412). This is how If-Match is implemented.
    /// </summary>
    void ExpectVersion<T>(T entity, byte[] version) where T : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Names of the DbContext's global filters. Only <see cref="Tenant"/> is turned off in flows with no
/// company in session that look up by a secret (login, refresh, pairing, device PIN).
/// </summary>
public static class QueryFilters
{
    public const string Tenant = "Tenant";
    public const string SoftDelete = "SoftDelete";
}
