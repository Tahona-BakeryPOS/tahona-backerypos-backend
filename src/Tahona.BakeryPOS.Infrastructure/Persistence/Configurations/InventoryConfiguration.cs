using Tahona.BakeryPOS.Domain.Catalog;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>Módulo Inventory — esquema <c>inv</c>.</summary>
internal sealed class StockLocationConfiguration : IEntityTypeConfiguration<StockLocation>
{
    public void Configure(EntityTypeBuilder<StockLocation> builder)
    {
        builder.ToTable("stock_locations", "inv");
        builder.ConfigureAuditable();
        builder.Property(e => e.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(80).IsRequired();
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => new { e.CompanyId, e.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.BranchId, e.Code }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        // Una sola ubicación por defecto por sucursal.
        builder.HasIndex(e => e.BranchId).IsUnique().HasFilter("\"is_default\" AND \"deleted_at\" IS NULL")
            .HasDatabaseName("ux_stock_locations_default_per_branch");
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class AdjustmentReasonConfiguration : IEntityTypeConfiguration<AdjustmentReason>
{
    public void Configure(EntityTypeBuilder<AdjustmentReason> builder)
    {
        builder.ToTable("adjustment_reasons", "inv", t =>
            t.HasCheckConstraint("ck_adjustment_reasons_direction", "direction IN ('in','out','both')"));
        builder.ConfigureAuditable();
        builder.Property(e => e.Code).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Direction).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<AdjustmentDirection>(v, true));
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    private static readonly string Kinds = string.Join(",", Enum.GetNames<StockMovementKind>().Select(k => $"'{SnakeCase(k)}'"));

    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", "inv", t =>
        {
            t.HasCheckConstraint("ck_stock_movements_quantity", "quantity <> 0");
            t.HasCheckConstraint("ck_stock_movements_kind", $"kind IN ({Kinds})");
        });
        builder.ConfigureAuditable();
        builder.Property(e => e.Quantity).HasPrecision(18, 4);
        builder.Property(e => e.Kind).HasMaxLength(20).IsUnicode(false)
            .HasConversion(v => SnakeCase(v.ToString()), v => Enum.Parse<StockMovementKind>(v.Replace("_", ""), true));
        builder.Property(e => e.SourceType).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Note).HasMaxLength(200);
        builder.HasOne<StockLocation>().WithMany().HasForeignKey(e => new { e.CompanyId, e.LocationId })
            .HasPrincipalKey(l => new { l.CompanyId, l.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => new { e.CompanyId, e.ProductId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AdjustmentReason>().WithMany().HasForeignKey(e => e.ReasonId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        // Idempotencia (docs/inventory.md §3): el mismo hecho no mueve dos veces el saldo.
        builder.HasIndex(e => new { e.CompanyId, e.Kind, e.SourceType, e.SourceId, e.SourceLineId }).IsUnique()
            .HasDatabaseName("ux_stock_movements_source");
        builder.HasIndex(e => new { e.CompanyId, e.LocationId, e.ProductId, e.OccurredAt });
    }

    internal static string SnakeCase(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        builder.ToTable("stock_balances", "inv");
        builder.ConfigureAuditable();
        builder.Property(e => e.OnHand).HasPrecision(18, 4);
        builder.HasOne<StockLocation>().WithMany().HasForeignKey(e => new { e.CompanyId, e.LocationId })
            .HasPrincipalKey(l => new { l.CompanyId, l.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(e => new { e.CompanyId, e.ProductId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.LocationId, e.ProductId }).IsUnique();
    }
}
