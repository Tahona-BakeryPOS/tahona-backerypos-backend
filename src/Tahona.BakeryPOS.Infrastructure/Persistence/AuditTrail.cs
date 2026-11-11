using System.Text.Json;
using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Infrastructure.Persistence.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>
/// Writes to <c>aud.audit_logs</c> the before and after of every change to an auditable entity,
/// in the same transaction as the change. Sensitive values (PIN or password hashes, credentials)
/// are never copied.
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

    /// <summary>Audit actions: create, update, delete (soft delete).</summary>
    private static string ActionOf(EntityEntry<AuditableEntity> entry)
    {
        if (entry.State == EntityState.Added) return "create";
        var deletedAt = entry.Property(nameof(AuditableEntity.DeletedAt));
        return deletedAt.IsModified && deletedAt.CurrentValue is not null ? "delete" : "update";
    }

    private static string Serialize(IEnumerable<PropertyEntry> props, Func<PropertyEntry, object?> value) =>
        JsonSerializer.Serialize(props.ToDictionary(
            p => p.Metadata.Name,
            p => Sensitive.Contains(p.Metadata.Name) ? "***" : value(p)?.ToString()));
}
