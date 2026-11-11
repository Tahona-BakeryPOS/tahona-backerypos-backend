using Tahona.BakeryPOS.Domain.Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>Common columns of <see cref="AuditableEntity"/>: key, audit and row version.</summary>
internal static class AuditableConfiguration
{
    public static void ConfigureAuditable<T>(this EntityTypeBuilder<T> builder) where T : AuditableEntity
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CreatedBy).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ModifiedBy).HasMaxLength(64);
        builder.Property(e => e.DeletedBy).HasMaxLength(64);
        // Concurrency token (bytea) that AppDbContext renews on every change.
        builder.Property(e => e.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        builder.Ignore(e => e.DomainEvents);
        builder.Ignore(e => e.IsDeleted);
    }
}
