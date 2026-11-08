namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Bandeja de salida: registra un mensaje en la misma transacción del cambio que lo origina
/// (se guarda con el próximo SaveChanges). Un proceso en segundo plano lo entrega a su handler,
/// con reintentos; así las llamadas externas nunca ocurren dentro de una transacción SQL.
/// </summary>
public interface IOutbox
{
    void Enqueue(string type, object payload);
}

/// <summary>Procesa un tipo de mensaje de la bandeja de salida. Debe ser idempotente.</summary>
public interface IOutboxHandler
{
    string Type { get; }

    Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken);
}

/// <param name="Payload">JSON tal como se guardó.</param>
public sealed record OutboxEnvelope(Guid Id, Guid? CompanyId, string Type, string Payload, int Attempt);
