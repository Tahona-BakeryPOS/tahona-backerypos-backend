using System.Text.Json;
using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Infrastructure.Persistence.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>
/// Escribe en <c>aud.audit_logs</c> el antes y el después de cada cambio de una entidad auditable,
/// en la misma transacción del cambio. Los valores sensibles (hashes de PIN o contraseña, credenciales)
/// nunca se copian.
/// </summary>
internal static class AuditTrail
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "PinHash", "PasswordHash", "EncryptedPrivateKey", "EncryptedEventsSecret", "EncryptedCredentials", "TokenHash",
    };

    private static readonly HashSet<string> Ignored =
    [
        nameof(AuditableEntity.CreatedAt), nameof(AuditableEntity.CreatedBy), nameof(AuditableEntity.ModifiedAt),
        nameof(AuditableEntity.ModifiedBy), nameof(AuditableEntity.RowVersion), nameof(AuditableEntity.DeletedBy),
    ];

    public static List<AuditLog> Collect(IEnumerable<EntityEntry<AuditableEntity>> entries, Guid? userId, Guid? companyId, string? correlationId, DateTime now)
    {
        var logs = new List<AuditLog>();
        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            var changed = entry.Properties
                .Where(p => !Ignored.Contains(p.Metadata.Name) && (entry.State == EntityState.Added || p.IsModified))
                .ToList();
            if (entry.State == EntityState.Modified && changed.Count == 0) continue;

            logs.Add(new AuditLog
            {
                CompanyId = entry.Entity is ITenantEntity t ? t.CompanyId : companyId,
                UserId = userId,
                Entity = entry.Metadata.GetSchemaQualifiedTableName() ?? entry.Metadata.ClrType.Name,
                EntityId = entry.Entity.Id,
                Action = ActionOf(entry),
                OldValues = entry.State == EntityState.Modified ? Serialize(changed, p => p.OriginalValue) : null,
                NewValues = Serialize(changed, p => p.CurrentValue),
                CorrelationId = correlationId,
                OccurredAt = now,
            });
        }
        return logs;
    }

    /// <summary>Acciones del diseño: creacion, modificacion, eliminacion (borrado lógico).</summary>
    private static string ActionOf(EntityEntry<AuditableEntity> entry)
    {
        if (entry.State == EntityState.Added) return "creacion";
        var deletedAt = entry.Property(nameof(AuditableEntity.DeletedAt));
        return deletedAt.IsModified && deletedAt.CurrentValue is not null ? "eliminacion" : "modificacion";
    }

    private static string Serialize(IEnumerable<PropertyEntry> props, Func<PropertyEntry, object?> value) =>
        JsonSerializer.Serialize(props.ToDictionary(
            p => p.Metadata.Name,
            p => Sensitive.Contains(p.Metadata.Name) ? "***" : value(p)?.ToString()));
}
