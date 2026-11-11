using Tahona.BakeryPOS.Domain.Cash;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>Sales module — schema <c>sales</c>.</summary>
internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales", "sales", t =>
        {
            t.HasCheckConstraint("ck_sales_status", "status IN ('draft','pending_payment','confirmed','cancelled','voided')");
            t.HasCheckConstraint("ck_sales_payment_status", "payment_status IN ('unpaid','partially_paid','paid','refunded','partially_refunded')");
            t.HasCheckConstraint("ck_sales_inventory_status", "inventory_status IN ('not_applied','applied','reversed')");
            t.HasCheckConstraint("ck_sales_fiscal_status", "fiscal_status IN ('not_required','pending','accepted','rejected','in_review')");
        });
        builder.ConfigureAuditable();
        Snake(builder.Property(e => e.Status));
        Snake(builder.Property(e => e.PaymentStatus));
        Snake(builder.Property(e => e.InventoryStatus));
        Snake(builder.Property(e => e.FiscalStatus));
        builder.Property(e => e.CustomerIdentification).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.VoidReason).HasMaxLength(300);
        builder.Ignore(e => e.IsOpen);
        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => new { l.CompanyId, l.SaleId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(e => e.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<CashSession>().WithMany().HasForeignKey(e => new { e.CompanyId, e.CashSessionId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => new { e.CompanyId, e.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.Number }).IsUnique().HasFilter("\"number\" IS NOT NULL");
        builder.HasIndex(e => new { e.CompanyId, e.BranchId, e.CreatedAt });
        builder.HasIndex(e => new { e.CashSessionId, e.Status });
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }

    private static void Snake<TEnum>(PropertyBuilder<TEnum> property) where TEnum : struct, Enum =>
        property.HasMaxLength(20).IsUnicode(false)
            .HasConversion(v => StockMovementConfiguration.SnakeCase(v.ToString()), v => Enum.Parse<TEnum>(v.Replace("_", ""), true));
}

internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        builder.ToTable("sale_lines", "sales", t =>
        {
            t.HasCheckConstraint("ck_sale_lines_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_sale_lines_discount", "discount_amount >= 0");
        });
        builder.ConfigureAuditable();
        builder.Property(e => e.Sku).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(200).IsRequired();
        builder.Property(e => e.UnitCode).HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(e => e.Quantity).HasPrecision(18, 3);
        builder.Property(e => e.TaxRate).HasPrecision(7, 4);
        builder.Property(e => e.DiscountReason).HasMaxLength(200);
        builder.Ignore(e => e.UnitPriceWithTax);
        builder.HasIndex(e => new { e.SaleId, e.LineNo }).IsUnique();
    }
}

internal sealed class SaleCounterConfiguration : IEntityTypeConfiguration<SaleCounter>
{
    public void Configure(EntityTypeBuilder<SaleCounter> builder)
    {
        builder.ToTable("sale_counters", "sales");
        builder.ConfigureAuditable();
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.CompanyId).IsUnique();
    }
}

internal sealed class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> builder)
    {
        builder.ToTable("returns", "sales", t => t.HasCheckConstraint("ck_returns_total", "total > 0"));
        builder.ConfigureAuditable();
        builder.Property(e => e.Reason).HasMaxLength(300).IsRequired();
        builder.HasMany(e => e.Lines).WithOne().HasForeignKey(l => new { l.CompanyId, l.SaleReturnId })
            .HasPrincipalKey(r => new { r.CompanyId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(e => e.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<Sale>().WithMany().HasForeignKey(e => new { e.CompanyId, e.SaleId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CashSession>().WithMany().HasForeignKey(e => new { e.CompanyId, e.CashSessionId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.BranchId, e.OccurredAt });
        builder.HasIndex(e => e.SaleId);
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class SaleReturnLineConfiguration : IEntityTypeConfiguration<SaleReturnLine>
{
    public void Configure(EntityTypeBuilder<SaleReturnLine> builder)
    {
        builder.ToTable("return_lines", "sales", t =>
        {
            t.HasCheckConstraint("ck_return_lines_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_return_lines_amount", "amount >= 0");
        });
        builder.ConfigureAuditable();
        builder.Property(e => e.Quantity).HasPrecision(18, 3);
        builder.HasOne<SaleLine>().WithMany().HasForeignKey(e => e.SaleLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.SaleReturnId, e.SaleLineId }).IsUnique();
    }
}
