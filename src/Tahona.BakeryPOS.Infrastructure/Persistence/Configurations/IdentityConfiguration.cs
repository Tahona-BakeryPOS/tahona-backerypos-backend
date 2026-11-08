using System.Security.Cryptography;
using System.Text;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Cash;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Módulo Identity — esquema <c>sec</c>. Las relaciones dentro de una empresa usan FK compuestas
/// (company_id, x_id): la base misma impide, por ejemplo, asignar un rol de otra empresa.
/// </summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "sec");
        builder.ConfigureAuditable();
        builder.Property(e => e.Email).HasMaxLength(200);
        builder.Property(e => e.FullName).HasMaxLength(120).IsRequired();
        builder.Property(e => e.PasswordHash).HasMaxLength(100).IsUnicode(false);
        builder.Ignore(e => e.CanUseBackoffice);
        builder.HasIndex(e => e.Email).IsUnique().HasFilter("\"email\" IS NOT NULL");
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships", "sec");
        builder.ConfigureAuditable();
        builder.Property(e => e.PinHash).HasMaxLength(100).IsUnicode(false);
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.UserId }).IsUnique();
        builder.HasIndex(e => e.UserId);
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles", "sec");
        builder.ConfigureAuditable();
        builder.Property(e => e.Code).HasMaxLength(40).IsUnicode(false).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(80).IsRequired();
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique();
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
        builder.HasMany(e => e.Permissions).WithOne().HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions", "sec");
        builder.HasKey(e => new { e.RoleId, e.PermissionId });
        builder.HasOne<Permission>().WithMany().HasForeignKey(e => e.PermissionId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Catálogo global sembrado por migración desde <see cref="Permissions.All"/>.</summary>
internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions", "sec");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(60).IsUnicode(false).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Module).HasMaxLength(40).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasData(Permissions.All.Select(p => new Permission { Id = StableId(p.Code), Code = p.Code, Name = p.Name, Module = p.Module }));
    }

    /// <summary>Id fijo derivado del código: la semilla no cambia entre migraciones.</summary>
    internal static Guid StableId(string code) => new(SHA256.HashData(Encoding.UTF8.GetBytes("permission:" + code)).AsSpan(0, 16));
}

internal sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable("role_assignments", "sec", t =>
            t.HasCheckConstraint("ck_role_assignments_validity", "valid_to IS NULL OR valid_to > valid_from"));
        builder.ConfigureAuditable();
        builder.HasOne<Membership>().WithMany().HasForeignKey(e => new { e.CompanyId, e.MembershipId })
            .HasPrincipalKey(m => new { m.CompanyId, m.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Role>().WithMany().HasForeignKey(e => new { e.CompanyId, e.RoleId })
            .HasPrincipalKey(r => new { r.CompanyId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => new { e.CompanyId, e.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id }).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
        builder.HasIndex(e => e.MembershipId);
    }
}

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices", "sec");
        builder.ConfigureAuditable();
        builder.Property(e => e.Name).HasMaxLength(80).IsRequired();
        builder.Property(e => e.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasOne<CashRegister>().WithMany().HasForeignKey(e => new { e.CompanyId, e.CashRegisterId })
            .HasPrincipalKey(r => new { r.CompanyId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.TokenHash).IsUnique();
    }
}

internal sealed class DevicePairingCodeConfiguration : IEntityTypeConfiguration<DevicePairingCode>
{
    public void Configure(EntityTypeBuilder<DevicePairingCode> builder)
    {
        builder.ToTable("device_pairing_codes", "sec");
        builder.ConfigureAuditable();
        builder.Property(e => e.CodeHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasOne<CashRegister>().WithMany().HasForeignKey(e => new { e.CompanyId, e.CashRegisterId })
            .HasPrincipalKey(r => new { r.CompanyId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.CodeHash).IsUnique().HasFilter("\"consumed_at\" IS NULL");
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", "sec");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.TokenHash).IsUnique();
        builder.HasIndex(e => new { e.UserId, e.RevokedAt });
    }
}

/// <summary>Módulo Cash — esquema <c>cash</c>. En la fase 2 solo el registro de cajas.</summary>
internal sealed class CashRegisterConfiguration : IEntityTypeConfiguration<CashRegister>
{
    public void Configure(EntityTypeBuilder<CashRegister> builder)
    {
        builder.ToTable("cash_registers", "cash");
        builder.ConfigureAuditable();
        builder.Property(e => e.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(120).IsRequired();
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => new { e.CompanyId, e.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.BranchId, e.Code }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}
