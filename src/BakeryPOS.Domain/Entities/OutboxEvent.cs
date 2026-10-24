using BakeryPOS.Domain.Common;
using BakeryPOS.Domain.Enums;

namespace BakeryPOS.Domain.Entities;

/// <summary>
/// Tabla: outbox_event / bandeja_evento
/// Outbox pattern for reliable event publishing.
/// </summary>
public class OutboxEvent : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public OutboxEventStatus Status { get; set; } = OutboxEventStatus.Pending;
    public int RetryCount { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
