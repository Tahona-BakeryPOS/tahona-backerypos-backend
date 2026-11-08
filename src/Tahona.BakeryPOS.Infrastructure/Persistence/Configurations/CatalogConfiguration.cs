using System.Security.Cryptography;
using System.Text;
using Tahona.BakeryPOS.Domain.Catalog;
using Tahona.BakeryPOS.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>Módulo Catalog — esquema <c>cat</c>.</summary>
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", "cat");
        builder.ConfigureAuditable();
        builder.Property(e => e.Name).HasMaxLength(80).IsRequired();
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.Name }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("units", "cat", t => t.HasCheckConstraint("ck_units_kind", "kind IN ('count','mass','volume')"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Kind).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<UnitKind>(v, true));
        builder.Property(e => e.DianCode).HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Ignore(e => e.AllowsDecimals);
        builder.HasIndex(e => e.Code).IsUnique();
        builder.HasData(Units.All.Select(u => new Unit { Id = StableId(u.Code), Code = u.Code, Name = u.Name, Kind = u.Kind, DianCode = u.DianCode }));
    }

    /// <summary>Id fijo derivado del código: la semilla no cambia entre migraciones.</summary>
    internal static Guid StableId(string code) => new(SHA256.HashData(Encoding.UTF8.GetBytes("unit:" + code)).AsSpan(0, 16));
}

internal sealed class TaxConfiguration : IEntityTypeConfiguration<Tax>
{
    public void Configure(EntityTypeBuilder<Tax> builder)
    {
        builder.ToTable("taxes", "cat", t =>
        {
            t.HasCheckConstraint("ck_taxes_kind", "kind IN ('iva','inc','none')");
            t.HasCheckConstraint("ck_taxes_rate", "rate >= 0 AND rate <= 100");
        });
        builder.ConfigureAuditable();
        builder.Property(e => e.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Kind).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<TaxKind>(v, true));
        builder.Property(e => e.Rate).HasPrecision(7, 4);
        builder.Ignore(e => e.DianCode);
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", "cat", t =>
            t.HasCheckConstraint("ck_products_negative_stock_policy", "negative_stock_policy IN ('allow','block')"));
        builder.ConfigureAuditable();
        builder.Property(e => e.NegativeStockPolicy).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<NegativeStockPolicy>(v, true));
        builder.Property(e => e.Sku).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Barcode).HasMaxLength(50).IsUnicode(false);
        builder.HasOne<Category>().WithMany().HasForeignKey(e => new { e.CompanyId, e.CategoryId })
            .HasPrincipalKey(c => new { c.CompanyId, c.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tax>().WithMany().HasForeignKey(e => new { e.CompanyId, e.TaxId })
            .HasPrincipalKey(t => new { t.CompanyId, t.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Unit>().WithMany().HasForeignKey(e => e.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.Sku }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        builder.HasIndex(e => new { e.CompanyId, e.Barcode }).IsUnique().HasFilter("\"barcode\" IS NOT NULL AND \"deleted_at\" IS NULL");
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class ProductPriceConfiguration : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> builder)
    {
        builder.ToTable("product_prices", "cat", t =>
        {
            t.HasCheckConstraint("ck_product_prices_amount", "amount >= 0");
            t.HasCheckConstraint("ck_product_prices_validity", "valid_to IS NULL OR valid_to > valid_from");
        });
        builder.ConfigureAuditable();
        builder.HasOne<Product>().WithMany().HasForeignKey(e => new { e.CompanyId, e.ProductId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        // branch_id NULL = precio base: FK simple (una compuesta con una parte nula no se valida).
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => e.BranchId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        // Una sola vigencia por inicio; el no-solapamiento lo garantiza ProductPrice.Open.
        builder.HasIndex(e => new { e.CompanyId, e.ProductId, e.BranchId, e.ValidFrom }).IsUnique().AreNullsDistinct(false);
    }
}

internal sealed class BranchProductConfiguration : IEntityTypeConfiguration<BranchProduct>
{
    public void Configure(EntityTypeBuilder<BranchProduct> builder)
    {
        builder.ToTable("branch_products", "cat");
        builder.ConfigureAuditable();
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => new { e.CompanyId, e.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => new { e.CompanyId, e.ProductId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.BranchId, e.ProductId }).IsUnique();
    }
}
