using BakeryPOS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BakeryPOS.Infrastructure.Persistence.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.OrganizationId).HasColumnName("organization_id").IsRequired();
        builder.Property(e => e.CashShiftId).HasColumnName("cash_shift_id").IsRequired();
        builder.Property(e => e.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(e => e.CashRegisterId).HasColumnName("cash_register_id").IsRequired();
        builder.Property(e => e.SoldByUserId).HasColumnName("sold_by_user_id").IsRequired();
        builder.Property(e => e.CustomerId).HasColumnName("customer_id");
        builder.Property(e => e.DocumentNumber).HasColumnName("document_number").HasMaxLength(50);
        builder.Property(e => e.SaleDate).HasColumnName("sale_date").HasColumnType("timestamptz");
        builder.Property(e => e.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(18,2)");
        builder.Property(e => e.TaxTotal).HasColumnName("tax_total").HasColumnType("numeric(18,2)");
        builder.Property(e => e.DiscountTotal).HasColumnName("discount_total").HasColumnType("numeric(18,2)");
        builder.Property(e => e.Total).HasColumnName("total").HasColumnType("numeric(18,2)");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(e => e.Origin).HasColumnName("origin").HasConversion<string>();
        builder.Property(e => e.Notes).HasColumnName("notes").HasMaxLength(500);

        // Audit columns
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(100);
        builder.Property(e => e.ModifiedAt).HasColumnName("modified_at").HasColumnType("timestamptz");
        builder.Property(e => e.ModifiedBy).HasColumnName("modified_by").HasMaxLength(100);
        builder.Property(e => e.RowVersion).HasColumnName("row_version");
        builder.Property(e => e.DeletedAt).HasColumnName("deleted_at").HasColumnType("timestamptz");
        builder.Property(e => e.DeletedBy).HasColumnName("deleted_by").HasMaxLength(100);

        // Relationships
        builder.HasOne(e => e.CashShift).WithMany(s => s.Sales).HasForeignKey(e => e.CashShiftId);
        builder.HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId);
        builder.HasOne(e => e.SoldByUser).WithMany().HasForeignKey(e => e.SoldByUserId);
        builder.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId);
        builder.HasMany(e => e.Lines).WithOne(l => l.Sale).HasForeignKey(l => l.SaleId);
        builder.HasMany(e => e.Payments).WithOne(p => p.Sale).HasForeignKey(p => p.SaleId);

        // Indexes
        builder.HasIndex(e => e.OrganizationId);
        builder.HasIndex(e => new { e.OrganizationId, e.DocumentNumber }).IsUnique()
            .HasFilter("deleted_at IS NULL AND document_number IS NOT NULL");
        builder.HasIndex(e => new { e.OrganizationId, e.SaleDate });
        builder.HasIndex(e => e.CashShiftId);

        // Ignore domain events
        builder.Ignore(e => e.DomainEvents);
    }
}
