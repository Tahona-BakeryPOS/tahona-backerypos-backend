namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Registro de eventos entrantes de terceros (webhooks de pasarela o proveedor fiscal). Guarda el
/// cuerpo crudo y deduplica por (proveedor, id del evento): un evento repetido no se procesa dos veces.
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
