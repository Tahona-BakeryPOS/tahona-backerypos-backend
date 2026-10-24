using System.Reflection;
using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Common;
using BakeryPOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentUserService? _currentUser;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUserService? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<CashRegister> CashRegisters => Set<CashRegister>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserAssignment> UserAssignments => Set<UserAssignment>();
    public DbSet<IdentityDocumentType> IdentityDocumentTypes => Set<IdentityDocumentType>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<Tax> Taxes => Set<Tax>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<PriceList> PriceLists => Set<PriceList>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<OperationReason> OperationReasons => Set<OperationReason>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Global query filter for soft delete
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(AuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(
                        ExpressionHelper.BuildSoftDeleteFilter(entityType.ClrType));
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.CreatedBy = _currentUser?.UserId.ToString() ?? "system";
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAt = DateTime.UtcNow;
                    entry.Entity.ModifiedBy = _currentUser?.UserId.ToString() ?? "system";
                    entry.Entity.RowVersion++;
                    break;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}

internal static class ExpressionHelper
{
    public static System.Linq.Expressions.LambdaExpression BuildSoftDeleteFilter(Type entityType)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var property = System.Linq.Expressions.Expression.Property(parameter, nameof(AuditableEntity.DeletedAt));
        var nullConstant = System.Linq.Expressions.Expression.Constant(null, typeof(DateTime?));
        var condition = System.Linq.Expressions.Expression.Equal(property, nullConstant);
        return System.Linq.Expressions.Expression.Lambda(condition, parameter);
    }
}
