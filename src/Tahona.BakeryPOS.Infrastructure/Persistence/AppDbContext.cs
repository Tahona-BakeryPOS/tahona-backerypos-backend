using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.Json;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Cash;
using Tahona.BakeryPOS.Domain.Catalog;
using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Domain.Identity;
using Tahona.BakeryPOS.Domain.Inventory;
using Tahona.BakeryPOS.Domain.Invoicing;
using Tahona.BakeryPOS.Domain.Payments;
using Tahona.BakeryPOS.Domain.Sales;
using Tahona.BakeryPOS.Infrastructure.Persistence.Integration;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>
/// Contexto único sobre PostgreSQL. Aplica, para toda entidad <see cref="ITenantEntity"/>, el filtro
/// global por la empresa de la sesión, y al guardar rechaza filas de otra empresa. Llena las columnas
/// de auditoría, escribe el registro de auditoría y los mensajes de outbox en la misma transacción.
/// </summary>
public sealed class AppDbContext : DbContext, IAppDbContext, IOutbox
{
    public const string TenantFilter = QueryFilters.Tenant;
    public const string SoftDeleteFilter = QueryFilters.SoftDelete;

    private readonly ITenantContext? _tenant;
    private readonly ICorrelationIdAccessor? _correlation;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenant = null, ICorrelationIdAccessor? correlation = null)
        : base(options)
    {
        _tenant = tenant;
        _correlation = correlation;
    }

    /// <summary>Empresa usada por el filtro global. EF la evalúa en cada consulta.</summary>
    public Guid? CurrentCompanyId => _tenant?.CompanyId;

    // Companies
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();

    // Identity
    public DbSet<User> Users => Set<User>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DevicePairingCode> DevicePairingCodes => Set<DevicePairingCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Catalog
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Tax> Taxes => Set<Tax>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<BranchProduct> BranchProducts => Set<BranchProduct>();

    // Inventory
    public DbSet<StockLocation> StockLocations => Set<StockLocation>();
    public DbSet<AdjustmentReason> AdjustmentReasons => Set<AdjustmentReason>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    // Cash
    public DbSet<CashRegister> CashRegisters => Set<CashRegister>();
    public DbSet<CashSession> CashSessions => Set<CashSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<CashReason> CashReasons => Set<CashReason>();

    // Payments
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<GatewayAccount> GatewayAccounts => Set<GatewayAccount>();
    public DbSet<RegisterTerminal> RegisterTerminals => Set<RegisterTerminal>();
    public DbSet<Refund> Refunds => Set<Refund>();

    // Sales
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SaleCounter> SaleCounters => Set<SaleCounter>();
    public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
    public DbSet<SaleReturnLine> SaleReturnLines => Set<SaleReturnLine>();

    // Invoicing
    public DbSet<FiscalSettings> FiscalSettings => Set<FiscalSettings>();
    public DbSet<FiscalMapping> FiscalMappings => Set<FiscalMapping>();
    public DbSet<FiscalDocument> FiscalDocuments => Set<FiscalDocument>();
    public DbSet<FiscalSubmissionAttempt> FiscalSubmissionAttempts => Set<FiscalSubmissionAttempt>();

    public void ExpectVersion<T>(T entity, byte[] version) where T : class =>
        Entry(entity).Property(nameof(AuditableEntity.RowVersion)).OriginalValue = version;

    // Transversales
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboundEvent> InboundEvents => Set<InboundEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(18, 2);
        builder.Properties<string>().AreUnicode();
        // Todas las fechas son UTC: timestamptz exige DateTime con Kind=Utc al escribir.
        builder.Properties<DateTime>().HaveColumnType("timestamp(3) with time zone").HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Select(e => e.ClrType).ToList())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType))
                ApplyFilter(modelBuilder, entityType, TenantFilter, nameof(ITenantEntity.CompanyId), isTenant: true);
            if (typeof(AuditableEntity).IsAssignableFrom(entityType))
                ApplyFilter(modelBuilder, entityType, SoftDeleteFilter, nameof(AuditableEntity.DeletedAt), isTenant: false);
        }

        SnakeCaseNaming.Apply(modelBuilder);
    }

    /// <summary>Filtros con nombre (EF Core 10): se pueden desactivar por separado con IgnoreQueryFilters([...]).</summary>
    private void ApplyFilter(ModelBuilder modelBuilder, Type entityType, string name, string property, bool isTenant)
    {
        var e = Expression.Parameter(entityType, "e");
        var member = Expression.Property(e, property);
        Expression body = isTenant
            ? Expression.Equal(Expression.Convert(member, typeof(Guid?)),
                Expression.Property(Expression.Constant(this), nameof(CurrentCompanyId)))
            : Expression.Equal(member, Expression.Constant(null, typeof(DateTime?)));
        modelBuilder.Entity(entityType).HasQueryFilter(name, Expression.Lambda(body, e));
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var actor = _tenant?.UserId?.ToString() ?? "system";

        GuardTenantIsolation();

        var auditable = ChangeTracker.Entries<AuditableEntity>().ToList();
        foreach (var entry in auditable)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = actor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.ModifiedAt = now;
                entry.Entity.ModifiedBy = actor;
            }
            // PostgreSQL no tiene rowversion: la versión la pone la aplicación en cada cambio (el valor
            // original sigue siendo el de la condición WHERE, así la concurrencia optimista se mantiene).
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.RowVersion = RandomNumberGenerator.GetBytes(8);
        }

        AuditLogs.AddRange(AuditTrail.Collect(auditable, _tenant?.UserId, _tenant?.CompanyId, _correlation?.CorrelationId, now));
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Con una sesión de empresa, toda fila de empresa que se agregue, modifique o borre debe ser de esa
    /// empresa, y nunca puede cambiar de empresa. Los procesos internos (sin empresa en sesión, p. ej.
    /// workers o webhooks) escriben con la empresa que ya trae cada entidad.
    /// </summary>
    private void GuardTenantIsolation()
    {
        var current = _tenant?.CompanyId;
        foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached) continue;

            var companyProp = entry.Property(e => e.CompanyId);
            if (entry.State == EntityState.Modified && companyProp.OriginalValue != companyProp.CurrentValue)
                throw new TenantIsolationException(entry.Metadata.ClrType.Name, companyProp.OriginalValue, current);

            if (current is not null && entry.Entity.CompanyId != current)
                throw new TenantIsolationException(entry.Metadata.ClrType.Name, entry.Entity.CompanyId, current);
        }
    }

    // ---- IOutbox: el mensaje se guarda con el próximo SaveChanges, en la misma transacción ----
    void IOutbox.Enqueue(string type, object payload)
    {
        var now = DateTime.UtcNow;
        OutboxMessages.Add(new OutboxMessage
        {
            CompanyId = _tenant?.CompanyId,
            Type = type,
            Payload = JsonSerializer.Serialize(payload, payload.GetType(), JsonSerializerOptions.Web),
            CreatedAt = now,
            NextAttemptAt = now,
        });
    }
}

/// <summary>Id de correlación de la petición actual (para auditoría).</summary>
public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}
