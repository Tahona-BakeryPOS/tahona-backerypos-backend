namespace Tahona.BakeryPOS.Infrastructure.Persistence.Integration;

/// <summary>Mensaje de la bandeja de salida (<c>int.outbox_messages</c>).</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid? CompanyId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public string Status { get; set; } = OutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}

public static class OutboxStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Done = "done";
    public const string Failed = "failed";
}

/// <summary>Evento recibido de un tercero (<c>int.inbound_events</c>), deduplicado por proveedor e id.</summary>
public sealed class InboundEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string Provider { get; init; } = string.Empty;
    public string ExternalEventId { get; init; } = string.Empty;
    public DateTime ReceivedAt { get; init; }
    public bool SignatureValid { get; init; }
    public string Payload { get; init; } = string.Empty;
    public string Status { get; set; } = "received";
    public DateTime? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>Respuesta guardada de una petición idempotente (<c>int.idempotency_requests</c>).</summary>
public sealed class IdempotencyRecord
{
    public Guid CompanyId { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public string Status { get; set; } = "in_progress";
    public int? ResponseStatus { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Registro de auditoría (<c>aud.audit_logs</c>): solo inserción.</summary>
public sealed class AuditLog
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid? CompanyId { get; init; }
    public Guid? UserId { get; init; }
    public string Entity { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public string? CorrelationId { get; init; }
    public DateTime OccurredAt { get; init; }
}
