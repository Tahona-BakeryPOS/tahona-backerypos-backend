using Tahona.BakeryPOS.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>Módulo Companies — esquema <c>org</c>.</summary>
internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies", "org", t =>
        {
            t.HasCheckConstraint("ck_companies_status", "status IN ('active','suspended')");
            t.HasCheckConstraint("ck_companies_tax_regime", "tax_regime IN ('48','49')");
        });
        builder.ConfigureAuditable();
        builder.Property(e => e.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.TradeName).HasMaxLength(200);
        builder.Property(e => e.NitNumber).HasMaxLength(15).IsUnicode(false).IsRequired();
        builder.Property(e => e.TaxRegime).HasMaxLength(5).IsUnicode(false).IsRequired();
        // Lista de códigos DIAN guardada como JSON (colección primitiva de EF Core).
        builder.PrimitiveCollection(e => e.TaxResponsibilities).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Phone).HasMaxLength(30);
        builder.Property(e => e.Address).HasMaxLength(200);
        builder.Property(e => e.CityCode).HasMaxLength(5).IsUnicode(false).IsFixedLength();
        builder.Property(e => e.TimeZone).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(e => e.Currency).HasMaxLength(3).IsUnicode(false).IsFixedLength().IsRequired();
        builder.Property(e => e.Status).HasMaxLength(20).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<CompanyStatus>(v, true));
        builder.Ignore(e => e.DisplayName);
        builder.HasIndex(e => e.NitNumber).IsUnique().HasFilter("\"deleted_at\" IS NULL");
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches", "org");
        builder.ConfigureAuditable();
        builder.Property(e => e.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Address).HasMaxLength(200);
        builder.Property(e => e.CityCode).HasMaxLength(5).IsUnicode(false).IsFixedLength();
        builder.Property(e => e.Phone).HasMaxLength(30);
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        // Destino de FK compuestas (company_id, id) desde otros módulos.
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}
