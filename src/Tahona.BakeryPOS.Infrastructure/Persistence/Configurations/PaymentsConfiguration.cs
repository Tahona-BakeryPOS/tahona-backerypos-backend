using Tahona.BakeryPOS.Domain.Cash;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>Payments module — schema <c>pay</c>.</summary>
internal sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("payment_methods", "pay", t =>
            t.HasCheckConstraint("ck_payment_methods_kind", "kind IN ('cash','gateway','external_card','transfer_manual')"));
        builder.ConfigureAuditable();
        builder.Property(e => e.Code).HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Kind).HasMaxLength(20).IsUnicode(false)
            .HasConversion(v => StockMovementConfiguration.SnakeCase(v.ToString()), v => Enum.Parse<PaymentMethodKind>(v.Replace("_", ""), true));
        builder.Property(e => e.GatewayMethod).HasMaxLength(30).IsUnicode(false);
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.Code }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", "pay", t =>
        {
            t.HasCheckConstraint("ck_payments_status", "status IN ('pending','approved','declined','cancelled','refunded','partially_refunded')");
            t.HasCheckConstraint("ck_payments_purpose", "purpose IN ('sale','test')");
            t.HasCheckConstraint("ck_payments_amount", "amount > 0");
            t.HasCheckConstraint("ck_payments_change", "change_amount IS NULL OR change_amount >= 0");
            t.HasCheckConstraint("ck_payments_refunded", "refunded_amount >= 0 AND refunded_amount <= amount");
        });
        builder.ConfigureAuditable();
        builder.Property(e => e.Status).HasMaxLength(20).IsUnicode(false)
            .HasConversion(v => StockMovementConfiguration.SnakeCase(v.ToString()), v => Enum.Parse<PaymentStatus>(v.Replace("_", ""), true));
        builder.Property(e => e.Purpose).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<PaymentPurpose>(v, true));
        builder.Property(e => e.MethodKind).HasMaxLength(20).IsUnicode(false)
            .HasConversion(v => StockMovementConfiguration.SnakeCase(v.ToString()), v => Enum.Parse<PaymentMethodKind>(v.Replace("_", ""), true));
        builder.Property(e => e.Reference).HasMaxLength(100);
        builder.Property(e => e.ResolutionNote).HasMaxLength(300);
        builder.Ignore(e => e.IsPending);
        builder.HasOne<PaymentMethod>().WithMany().HasForeignKey(e => new { e.CompanyId, e.PaymentMethodId })
            .HasPrincipalKey(m => new { m.CompanyId, m.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CashSession>().WithMany().HasForeignKey(e => new { e.CompanyId, e.CashSessionId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CashSessionId, e.Status });
        builder.HasIndex(e => new { e.CompanyId, e.SaleId });
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts", "pay", t =>
            t.HasCheckConstraint("ck_payment_attempts_status", "status IN ('created','pending','approved','declined','voided','error','unknown','expired')"));
        builder.ConfigureAuditable();
        builder.Property(e => e.Provider).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Environment).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<GatewayEnvironment>(v, true));
        builder.Property(e => e.Reference).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(e => e.ExternalId).HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.IntegrationId).HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.Status).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<AttemptStatus>(v, true));
        builder.Property(e => e.RawStatus).HasMaxLength(40).IsUnicode(false);
        builder.Property(e => e.GatewayMethod).HasMaxLength(30).IsUnicode(false);
        builder.Property(e => e.TerminalSerial).HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.ErrorCode).HasMaxLength(20).IsUnicode(false);
        builder.Property(e => e.ErrorMessage).HasMaxLength(300);
        builder.Ignore(e => e.IsOpen);
        builder.HasOne<Payment>().WithMany().HasForeignKey(e => new { e.CompanyId, e.PaymentId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.Provider, e.Reference }).IsUnique();
        builder.HasIndex(e => new { e.Provider, e.ExternalId }).IsUnique().HasFilter("\"external_id\" IS NOT NULL");
        // A single open attempt per payment (payment-integration.md §5).
        builder.HasIndex(e => e.PaymentId).IsUnique().HasFilter("\"status\" IN ('created','pending','unknown')")
            .HasDatabaseName("ux_payment_attempts_open_per_payment");
    }
}

internal sealed class GatewayAccountConfiguration : IEntityTypeConfiguration<GatewayAccount>
{
    public void Configure(EntityTypeBuilder<GatewayAccount> builder)
    {
        builder.ToTable("gateway_accounts", "pay", t =>
            t.HasCheckConstraint("ck_gateway_accounts_environment", "environment IN ('sandbox','production')"));
        builder.ConfigureAuditable();
        builder.Property(e => e.Provider).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Environment).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<GatewayEnvironment>(v, true));
        builder.Property(e => e.EncryptedIdentityKey).IsRequired();
        builder.Property(e => e.SellerEmail).HasMaxLength(200).IsRequired();
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CompanyId, e.Provider, e.Environment }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
        builder.HasIndex(e => new { e.CompanyId, e.Provider }).IsUnique().HasFilter("\"is_active\" AND \"deleted_at\" IS NULL")
            .HasDatabaseName("ux_gateway_accounts_one_active");
    }
}

internal sealed class RegisterTerminalConfiguration : IEntityTypeConfiguration<RegisterTerminal>
{
    public void Configure(EntityTypeBuilder<RegisterTerminal> builder)
    {
        builder.ToTable("register_terminals", "pay");
        builder.ConfigureAuditable();
        builder.Property(e => e.Provider).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.TerminalModel).HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(e => e.TerminalSerial).HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.HasOne<CashRegister>().WithMany().HasForeignKey(e => new { e.CompanyId, e.CashRegisterId })
            .HasPrincipalKey(r => new { r.CompanyId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.CashRegisterId, e.Provider }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds", "pay", t =>
        {
            t.HasCheckConstraint("ck_refunds_amount", "amount > 0");
            t.HasCheckConstraint("ck_refunds_method", "method IN ('cash','gateway','manual')");
            t.HasCheckConstraint("ck_refunds_status", "status IN ('pending','completed','failed')");
            t.HasCheckConstraint("ck_refunds_source", "source_type IN ('void','return')");
        });
        builder.ConfigureAuditable();
        builder.Property(e => e.Method).HasMaxLength(10).IsUnicode(false)
            .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<RefundMethod>(v, true));
        builder.Property(e => e.SourceType).HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasMaxLength(10).IsUnicode(false).IsRequired();
        builder.HasOne<Payment>().WithMany().HasForeignKey(e => new { e.CompanyId, e.PaymentId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.PaymentId, e.SourceType, e.SourceId }).IsUnique();
        builder.HasIndex(e => new { e.CompanyId, e.SaleId });
    }
}
