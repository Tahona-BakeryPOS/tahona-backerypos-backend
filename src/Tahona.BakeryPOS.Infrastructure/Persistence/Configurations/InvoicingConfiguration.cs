using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Domain.Invoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>Módulo Invoicing — esquema <c>fis</c>.</summary>
internal sealed class FiscalSettingsConfiguration : IEntityTypeConfiguration<FiscalSettings>
{
    public void Configure(EntityTypeBuilder<FiscalSettings> builder)
    {
        builder.ToTable("fiscal_settings", "fis", t => t.HasCheckConstraint("ck_fiscal_settings_retention", "retention_years >= 1"));
        builder.ConfigureAuditable();
        builder.Property(e => e.Provider).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.ConsumerFinalIdentification).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Ignore(e => e.IsComplete);
        builder.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.CompanyId).IsUnique().HasFilter("\"deleted_at\" IS NULL");
    }
}

internal sealed class FiscalMappingConfiguration : IEntityTypeConfiguration<FiscalMapping>
{
    public void Configure(EntityTypeBuilder<FiscalMapping> builder)
    {
        builder.ToTable("fiscal_mappings", "fis", t => t.HasCheckConstraint("ck_fiscal_mappings_kind", "kind IN ('tax','payment_method','product')"));
        builder.ConfigureAuditable();
        builder.Property(e => e.Kind).HasMaxLength(20).IsUnicode(false)
            .HasConversion(v => StockMovementConfiguration.SnakeCase(v.ToString()), v => Enum.Parse<FiscalMappingKind>(v.Replace("_", ""), true));
        builder.Property(e => e.ExternalId).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.HasIndex(e => new { e.CompanyId, e.Kind, e.LocalId }).IsUnique().HasFilter("\"deleted_at\" IS NULL");
    }
}

internal sealed class FiscalDocumentConfiguration : IEntityTypeConfiguration<FiscalDocument>
{
    public void Configure(EntityTypeBuilder<FiscalDocument> builder)
    {
        builder.ToTable("fiscal_documents", "fis", t =>
        {
            t.HasCheckConstraint("ck_fiscal_documents_status",
                "status IN ('pending','submitting','accepted','rejected','temporary_error','unknown','cancelled')");
            t.HasCheckConstraint("ck_fiscal_documents_kind", "kind IN ('invoice','credit_note')");
            t.HasCheckConstraint("ck_fiscal_documents_total", "total > 0");
        });
        builder.ConfigureAuditable();
        builder.Property(e => e.Kind).HasMaxLength(20).IsUnicode(false)
            .HasConversion(v => StockMovementConfiguration.SnakeCase(v.ToString()), v => Enum.Parse<FiscalDocumentKind>(v.Replace("_", ""), true));
        builder.Property(e => e.Status).HasMaxLength(20).IsUnicode(false)
            .HasConversion(v => StockMovementConfiguration.SnakeCase(v.ToString()), v => Enum.Parse<FiscalDocumentStatus>(v.Replace("_", ""), true));
        builder.Property(e => e.Provider).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.IdempotencyKey).HasMaxLength(30).IsUnicode(false).IsRequired();
        builder.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.CustomerIdentification).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.ExternalId).HasMaxLength(100).IsUnicode(false);
        builder.Property(e => e.Prefix).HasMaxLength(10).IsUnicode(false);
        builder.Property(e => e.Cufe).HasMaxLength(200).IsUnicode(false);
        builder.Property(e => e.Errors).HasMaxLength(2000);
        builder.Ignore(e => e.IsFinal);
        builder.HasIndex(e => new { e.Provider, e.IdempotencyKey }).IsUnique();
        builder.HasIndex(e => new { e.Provider, e.ExternalId }).IsUnique().HasFilter("\"external_id\" IS NOT NULL");
        // Una factura por venta (las notas crédito, fase 9, pueden ser varias).
        builder.HasIndex(e => e.SaleId).IsUnique().HasFilter("\"kind\" = 'invoice'").HasDatabaseName("ux_fiscal_documents_invoice_per_sale");
        builder.HasIndex(e => new { e.CompanyId, e.Status });
        // Fase 9: una nota crédito por anulación o devolución.
        builder.HasIndex(e => e.SourceId).IsUnique().HasFilter("\"kind\" = 'credit_note'").HasDatabaseName("ux_fiscal_documents_credit_note_per_source");
        builder.HasOne<FiscalDocument>().WithMany().HasForeignKey(e => e.InvoiceDocumentId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(e => new { e.CompanyId, e.Id });
    }
}

internal sealed class FiscalSubmissionAttemptConfiguration : IEntityTypeConfiguration<FiscalSubmissionAttempt>
{
    public void Configure(EntityTypeBuilder<FiscalSubmissionAttempt> builder)
    {
        builder.ToTable("fiscal_submission_attempts", "fis");
        builder.ConfigureAuditable();
        builder.Property(e => e.Operation).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Outcome).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.Detail).HasMaxLength(1000);
        builder.HasOne<FiscalDocument>().WithMany().HasForeignKey(e => new { e.CompanyId, e.FiscalDocumentId })
            .HasPrincipalKey(d => new { d.CompanyId, d.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.FiscalDocumentId, e.Number });
    }
}
