using BakeryPOS.Domain.Common;
using BakeryPOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Base configuration for snake_case table/column naming and common audit columns.
/// Individual entity configurations inherit from this.
/// </summary>
public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        ConfigureBase(builder);
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Nit).HasColumnName("nit").HasMaxLength(20);
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(200);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(300);
        builder.Property(e => e.LogoUrl).HasColumnName("logo_url").HasMaxLength(500);
        builder.Property(e => e.IsActive).HasColumnName("is_active");
    }

    protected static void ConfigureBase<T>(EntityTypeBuilder<T> builder) where T : AuditableEntity
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
        builder.Property(e => e.ModifiedAt).HasColumnName("modified_at").HasColumnType("timestamptz");
        builder.Property(e => e.ModifiedBy).HasColumnName("modified_by").HasMaxLength(100);
        builder.Property(e => e.RowVersion).HasColumnName("row_version");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
        builder.Property(e => e.DeletedBy).HasColumnName("deleted_by").HasMaxLength(100);
        builder.Ignore(e => e.DomainEvents);
    }
}

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(300);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasOne(e => e.Organization).WithMany(o => o.Branches).HasForeignKey(e => e.OrganizationId);
        builder.HasIndex(e => e.OrganizationId);
    }
}

public class CashRegisterConfiguration : IEntityTypeConfiguration<CashRegister>
{
    public void Configure(EntityTypeBuilder<CashRegister> builder)
    {
        builder.ToTable("cash_registers");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasOne(e => e.Branch).WithMany(b => b.CashRegisters).HasForeignKey(e => e.BranchId);
        builder.HasIndex(e => new { e.OrganizationId, e.BranchId });
    }
}

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.CashRegisterId).HasColumnName("cash_register_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.DeviceIdentifier).HasColumnName("device_identifier").HasMaxLength(200);
        builder.Property(e => e.LastSyncAt).HasColumnName("last_sync_at").HasColumnType("timestamptz");
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasOne(e => e.CashRegister).WithMany(c => c.Devices).HasForeignKey(e => e.CashRegisterId);
    }
}

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("app_users");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(200);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(e => e.PinHash).HasColumnName("pin_hash").HasMaxLength(200).IsRequired();
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasOne(e => e.Organization).WithMany(o => o.Users).HasForeignKey(e => e.OrganizationId);
        builder.HasIndex(e => e.OrganizationId);
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(300);
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasOne(e => e.Organization).WithMany(o => o.Roles).HasForeignKey(e => e.OrganizationId);
        builder.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Module).HasColumnName("module").HasMaxLength(50);

        builder.HasIndex(e => e.Code).IsUnique();
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(e => e.PermissionId).HasColumnName("permission_id").IsRequired();

        builder.HasOne(e => e.Role).WithMany(r => r.RolePermissions).HasForeignKey(e => e.RoleId);
        builder.HasOne(e => e.Permission).WithMany(p => p.RolePermissions).HasForeignKey(e => e.PermissionId);
        builder.HasIndex(e => new { e.RoleId, e.PermissionId }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class UserAssignmentConfiguration : IEntityTypeConfiguration<UserAssignment>
{
    public void Configure(EntityTypeBuilder<UserAssignment> builder)
    {
        builder.ToTable("user_assignments");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(e => e.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasOne(e => e.User).WithMany(u => u.UserAssignments).HasForeignKey(e => e.UserId);
        builder.HasOne(e => e.Role).WithMany(r => r.UserAssignments).HasForeignKey(e => e.RoleId);
        builder.HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId);
        builder.HasIndex(e => new { e.UserId, e.BranchId });
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(e => e.TaxId).HasColumnName("tax_id");
        builder.Property(e => e.UnitOfMeasureId).HasColumnName("unit_of_measure_id");
        builder.Property(e => e.Sku).HasColumnName("sku").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(e => e.ImageUrl).HasColumnName("image_url").HasMaxLength(500);
        builder.Property(e => e.IsActive).HasColumnName("is_active");
        builder.Property(e => e.TrackInventory).HasColumnName("track_inventory");

        builder.HasOne(e => e.Category).WithMany(c => c.Products).HasForeignKey(e => e.CategoryId);
        builder.HasOne(e => e.Tax).WithMany().HasForeignKey(e => e.TaxId);
        builder.HasOne(e => e.UnitOfMeasure).WithMany().HasForeignKey(e => e.UnitOfMeasureId);
        builder.HasIndex(e => new { e.OrganizationId, e.Sku }).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(e => new { e.OrganizationId, e.CategoryId });
    }
}

public class CashShiftConfiguration : IEntityTypeConfiguration<CashShift>
{
    public void Configure(EntityTypeBuilder<CashShift> builder)
    {
        builder.ToTable("cash_shifts");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.CashRegisterId).HasColumnName("cash_register_id").IsRequired();
        builder.Property(e => e.OpenedByUserId).HasColumnName("opened_by_user_id").IsRequired();
        builder.Property(e => e.ClosedByUserId).HasColumnName("closed_by_user_id");
        builder.Property(e => e.OpenedAt).HasColumnName("opened_at").HasColumnType("timestamptz");
        builder.Property(e => e.ClosedAt).HasColumnName("closed_at").HasColumnType("timestamptz");
        builder.Property(e => e.OpeningAmount).HasColumnName("opening_amount").HasColumnType("numeric(18,2)");
        builder.Property(e => e.ClosingAmount).HasColumnName("closing_amount").HasColumnType("numeric(18,2)");
        builder.Property(e => e.ExpectedAmount).HasColumnName("expected_amount").HasColumnType("numeric(18,2)");
        builder.Property(e => e.Difference).HasColumnName("difference").HasColumnType("numeric(18,2)");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(500);

        builder.HasOne(e => e.CashRegister).WithMany(c => c.CashShifts).HasForeignKey(e => e.CashRegisterId);
        builder.HasOne(e => e.OpenedByUser).WithMany().HasForeignKey(e => e.OpenedByUserId);
        builder.HasOne(e => e.ClosedByUser).WithMany().HasForeignKey(e => e.ClosedByUserId);
        builder.HasIndex(e => new { e.OrganizationId, e.CashRegisterId, e.Status });
    }
}
