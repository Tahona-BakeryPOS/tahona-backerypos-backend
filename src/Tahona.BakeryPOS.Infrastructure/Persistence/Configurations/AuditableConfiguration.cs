using Tahona.BakeryPOS.Domain.Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>Columnas comunes de <see cref="AuditableEntity"/>: clave, auditoría y versión de fila.</summary>
internal static class AuditableConfiguration
{
    public static void ConfigureAuditable<T>(this EntityTypeBuilder<T> builder) where T : AuditableEntity
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CreatedBy).HasMaxLength(64).IsRequired();
        builder.Property(e => e.ModifiedBy).HasMaxLength(64);
        builder.Property(e => e.DeletedBy).HasMaxLength(64);
        // Token de concurrencia (bytea) que AppDbContext renueva en cada cambio.
        builder.Property(e => e.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        builder.Ignore(e => e.DomainEvents);
        builder.Ignore(e => e.IsDeleted);
    }
}
