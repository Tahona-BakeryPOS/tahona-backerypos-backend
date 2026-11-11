namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Record of inbound third-party events (gateway or tax-provider webhooks). It stores the
/// raw body and deduplicates by (provider, event id): a repeated event is not processed twice.
/// </summary>
public interface IInboundEventStore
{
    Task<InboundEventRecord> RecordAsync(
        string provider, string externalEventId, string payload, bool signatureValid, CancellationToken cancellationToken);

    Task MarkAsync(Guid id, InboundEventStatus status, string? error, CancellationToken cancellationToken);
}

public sealed record InboundEventRecord(Guid Id, bool IsDuplicate);

public enum InboundEventStatus
{
    Received,
    Processed,
    Ignored,
    Failed,
}
