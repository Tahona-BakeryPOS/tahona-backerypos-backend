namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Outbox: records a message in the same transaction as the change that originates it
/// (saved with the next SaveChanges). A background process delivers it to its handler,
/// with retries; this way external calls never happen inside a SQL transaction.
/// </summary>
public interface IOutbox
{
    void Enqueue(string type, object payload);
}

/// <summary>Processes one outbox message type. It must be idempotent.</summary>
public interface IOutboxHandler
{
    string Type { get; }

    Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken);
}

/// <param name="Payload">JSON as it was stored.</param>
public sealed record OutboxEnvelope(Guid Id, Guid? CompanyId, string Type, string Payload, int Attempt);
