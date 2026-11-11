using Tahona.BakeryPOS.Domain.Cash;
using Tahona.BakeryPOS.Domain.Companies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>Cash module — schema <c>cash</c> (sessions, movements and reasons; the registers are with Identity).</summary>
internal sealed class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public void Configure(EntityTypeBuilder<CashSession> builder)
    {
        builder.ToTable("cash_sessions", "cash", t =>
        {
            t.HasCheckConstraint("ck_cash_sessions_status", "status IN ('open','closing','closed')");
            t.HasCheckConstraint("ck_cash_sessions_opening", "opening_amount >= 0");
        });
        builder.ConfigureAuditable();
        builder.Property(e => e.Status).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<CashSessionStatus>(v, true));
        builder.Property(e => e.Notes).HasMaxLength(300);
        builder.Ignore(e => e.IsOpen);
        builder.HasOne<CashRegister>().WithMany().HasForeignKey(e => new { e.CompanyId, e.CashRegisterId })
            .HasPrincipalKey(r => new { r.CompanyId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Branch>().WithMany().HasForeignKey(e => new { e.CompanyId, e.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id }).OnDelete(DeleteBehavior.Restrict);
        // A single non-closed session per register (US-5.2).
        builder.HasIndex(e => e.CashRegisterId).IsUnique().HasFilter("\"status\" <> 'closed'")
            .HasDatabaseName("ux_cash_sessions_open_per_register");
        builder.HasIndex(e => new { e.CompanyId, e.BranchId, e.OpenedAt });
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("cash_movements", "cash", t =>
        {
            t.HasCheckConstraint("ck_cash_movements_amount", "amount > 0");
            t.HasCheckConstraint("ck_cash_movements_kind",
                "kind IN ('opening','sale_cash','change_given','refund_cash','income','expense')");
        });
        builder.ConfigureAuditable();
        builder.Property(e => e.Kind).HasMaxLength(20).IsUnicode(false)
            .HasConversion(v => StockMovementConfiguration.SnakeCase(v.ToString()), v => Enum.Parse<CashMovementKind>(v.Replace("_", ""), true));
        builder.Property(e => e.SourceType).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.Note).HasMaxLength(200);
        builder.Ignore(e => e.SignedAmount);
        builder.HasOne<CashSession>().WithMany().HasForeignKey(e => new { e.CompanyId, e.CashSessionId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CashReason>().WithMany().HasForeignKey(e => e.ReasonId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        // Derived from sales and returns (phase 8): a single movement per (kind, source).
        builder.HasIndex(e => new { e.CompanyId, e.Kind, e.SourceType, e.SourceId }).IsUnique()
            .HasFilter("\"source_id\" IS NOT NULL").HasDatabaseName("ux_cash_movements_source");
        builder.HasIndex(e => new { e.CashSessionId, e.OccurredAt });
    }
}

internal sealed class CashReasonConfiguration : IEntityTypeConfiguration<CashReason>
{
    public void Configure(EntityTypeBuilder<CashReason> builder)
    {
        builder.ToTable("cash_reasons", "cash", t => t.HasCheckConstraint("ck_cash_reasons_kind", "kind IN ('income','expense')"));
        builder.ConfigureAuditable();
        builder.Property(e => e.Kind).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<CashReasonKind>(v, true));
        builder.Property(e => e.Name).HasMaxLength(80).IsRequired();
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.Kind, e.Name }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
    }
}
