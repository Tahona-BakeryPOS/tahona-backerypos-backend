using BakeryPOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<Branch> Branches { get; }
    DbSet<CashRegister> CashRegisters { get; }
    DbSet<Device> Devices { get; }
    DbSet<AppUser> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserAssignment> UserAssignments { get; }
    DbSet<IdentityDocumentType> IdentityDocumentTypes { get; }
    DbSet<Party> Parties { get; }
    DbSet<UnitOfMeasure> UnitsOfMeasure { get; }
    DbSet<Tax> Taxes { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductBarcode> ProductBarcodes { get; }
    DbSet<PriceList> PriceLists { get; }
    DbSet<Price> Prices { get; }
    DbSet<PaymentMethod> PaymentMethods { get; }
    DbSet<OperationReason> OperationReasons { get; }
    DbSet<DocumentSequence> DocumentSequences { get; }
    DbSet<CashShift> CashShifts { get; }
    DbSet<CashMovement> CashMovements { get; }
    DbSet<Sale> Sales { get; }
    DbSet<SaleLine> SaleLines { get; }
    DbSet<Payment> Payments { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<OutboxEvent> OutboxEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
