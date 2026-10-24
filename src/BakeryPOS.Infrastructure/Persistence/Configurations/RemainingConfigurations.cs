using BakeryPOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BakeryPOS.Infrastructure.Persistence.Configurations;

public class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        builder.ToTable("sale_lines");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.SaleId).HasColumnName("sale_id").IsRequired();
        builder.Property(e => e.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(e => e.ProductName).HasColumnName("product_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Quantity).HasColumnName("quantity").HasColumnType("numeric(18,4)");
        builder.Property(e => e.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(18,2)");
        builder.Property(e => e.DiscountPercent).HasColumnName("discount_percent").HasColumnType("numeric(5,2)");
        builder.Property(e => e.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(18,2)");
        builder.Property(e => e.TaxPercent).HasColumnName("tax_percent").HasColumnType("numeric(5,2)");
        builder.Property(e => e.TaxAmount).HasColumnName("tax_amount").HasColumnType("numeric(18,2)");
        builder.Property(e => e.LineTotal).HasColumnName("line_total").HasColumnType("numeric(18,2)");
        builder.Property(e => e.LineNumber).HasColumnName("line_number");

        builder.HasOne(e => e.Sale).WithMany(s => s.Lines).HasForeignKey(e => e.SaleId);
        builder.HasOne(e => e.Product).WithMany().HasForeignKey(e => e.ProductId);
        builder.HasIndex(e => e.SaleId);
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.SaleId).HasColumnName("sale_id").IsRequired();
        builder.Property(e => e.PaymentMethodId).HasColumnName("payment_method_id").IsRequired();
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
        builder.Property(e => e.Reference).HasColumnName("reference").HasMaxLength(200);

        builder.HasOne(e => e.Sale).WithMany(s => s.Payments).HasForeignKey(e => e.SaleId);
        builder.HasOne(e => e.PaymentMethod).WithMany().HasForeignKey(e => e.PaymentMethodId);
        builder.HasIndex(e => e.SaleId);
    }
}

public class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        builder.ToTable("product_barcodes");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(e => e.Barcode).HasColumnName("barcode").HasMaxLength(100).IsRequired();

        builder.HasOne(e => e.Product).WithMany(p => p.Barcodes).HasForeignKey(e => e.ProductId);
        builder.HasIndex(e => new { e.OrganizationId, e.Barcode }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.ToTable("price_lists");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.IsDefault).HasColumnName("is_default");
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class PriceConfiguration : IEntityTypeConfiguration<Price>
{
    public void Configure(EntityTypeBuilder<Price> builder)
    {
        builder.ToTable("prices");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.PriceListId).HasColumnName("price_list_id").IsRequired();
        builder.Property(e => e.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(e => e.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(18,2)");

        builder.HasOne(e => e.PriceList).WithMany(pl => pl.Prices).HasForeignKey(e => e.PriceListId);
        builder.HasOne(e => e.Product).WithMany(p => p.Prices).HasForeignKey(e => e.ProductId);
        builder.HasIndex(e => new { e.PriceListId, e.ProductId }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.ToTable("payment_methods");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.Property(e => e.RequiresReference).HasColumnName("requires_reference");
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasIndex(e => new { e.OrganizationId, e.Code }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class OperationReasonConfiguration : IEntityTypeConfiguration<OperationReason>
{
    public void Configure(EntityTypeBuilder<OperationReason> builder)
    {
        builder.ToTable("operation_reasons");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Type).HasColumnName("type").HasConversion<string>();
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasIndex(e => new { e.OrganizationId, e.Type });
    }
}

public class DocumentSequenceConfiguration : IEntityTypeConfiguration<DocumentSequence>
{
    public void Configure(EntityTypeBuilder<DocumentSequence> builder)
    {
        builder.ToTable("document_sequences");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(e => e.Prefix).HasColumnName("prefix").HasMaxLength(10).IsRequired();
        builder.Property(e => e.CurrentNumber).HasColumnName("current_number");

        builder.HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId);
        builder.HasIndex(e => new { e.OrganizationId, e.BranchId, e.Prefix }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("cash_movements");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.CashShiftId).HasColumnName("cash_shift_id").IsRequired();
        builder.Property(e => e.ReasonId).HasColumnName("reason_id");
        builder.Property(e => e.Type).HasColumnName("type").HasConversion<string>();
        builder.Property(e => e.Amount).HasColumnName("amount").HasColumnType("numeric(18,2)");
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(300);

        builder.HasOne(e => e.CashShift).WithMany(cs => cs.CashMovements).HasForeignKey(e => e.CashShiftId);
        builder.HasOne(e => e.Reason).WithMany().HasForeignKey(e => e.ReasonId);
        builder.HasIndex(e => e.CashShiftId);
    }
}

public class IdentityDocumentTypeConfiguration : IEntityTypeConfiguration<IdentityDocumentType>
{
    public void Configure(EntityTypeBuilder<IdentityDocumentType> builder)
    {
        builder.ToTable("identity_document_types");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(10).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();

        builder.HasIndex(e => e.Code).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("parties");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.IdentityDocumentTypeId).HasColumnName("identity_document_type_id");
        builder.Property(e => e.DocumentNumber).HasColumnName("document_number").HasMaxLength(30);
        builder.Property(e => e.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(200);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(e => e.Address).HasColumnName("address").HasMaxLength(300);
        builder.Property(e => e.IsCustomer).HasColumnName("is_customer");
        builder.Property(e => e.IsSupplier).HasColumnName("is_supplier");

        builder.HasOne(e => e.IdentityDocumentType).WithMany().HasForeignKey(e => e.IdentityDocumentTypeId);
        builder.HasIndex(e => new { e.OrganizationId, e.DocumentNumber }).HasFilter("deleted_at IS NULL AND document_number IS NOT NULL");
    }
}

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("units_of_measure");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(e => e.Abbreviation).HasColumnName("abbreviation").HasMaxLength(10).IsRequired();

        builder.HasIndex(e => new { e.OrganizationId, e.Abbreviation }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class TaxConfiguration : IEntityTypeConfiguration<Tax>
{
    public void Configure(EntityTypeBuilder<Tax> builder)
    {
        builder.ToTable("taxes");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Rate).HasColumnName("rate").HasColumnType("numeric(5,2)");
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.ToTable("product_categories");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Description).HasColumnName("description").HasMaxLength(300);
        builder.Property(e => e.SortOrder).HasColumnName("sort_order");
        builder.Property(e => e.IsActive).HasColumnName("is_active");

        builder.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.Action).HasColumnName("action").HasMaxLength(50).IsRequired();
        builder.Property(e => e.EntityName).HasColumnName("entity_name").HasMaxLength(100).IsRequired();
        builder.Property(e => e.EntityId).HasColumnName("entity_id");
        builder.Property(e => e.OldValues).HasColumnName("old_values").HasColumnType("jsonb");
        builder.Property(e => e.NewValues).HasColumnName("new_values").HasColumnType("jsonb");
        builder.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(45);

        builder.HasIndex(e => new { e.OrganizationId, e.EntityName, e.EntityId });
        builder.HasIndex(e => new { e.OrganizationId, e.CreatedAt });
    }
}

public class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events");
        OrganizationConfiguration.ConfigureBase(builder);
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at").HasColumnType("timestamptz");
        builder.Property(e => e.Error).HasColumnName("error").HasMaxLength(2000);
        builder.Property(e => e.RetryCount).HasColumnName("retry_count");

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => new { e.Status, e.CreatedAt });
    }
}
