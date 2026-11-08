using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using Tahona.BakeryPOS.Infrastructure.Persistence.Integration;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Infrastructure.Integration;

/// <summary>
/// Guarda cada evento entrante antes de procesarlo. La deduplicación la garantiza el índice único
/// (proveedor, id externo) en la base, no una consulta previa: dos entregas simultáneas del mismo
/// evento no pueden pasar ambas.
/// </summary>
public sealed class InboundEventStore : IInboundEventStore
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public InboundEventStore(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<InboundEventRecord> RecordAsync(
        string provider, string externalEventId, string payload, bool signatureValid, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var evt = new InboundEvent
        {
            Provider = provider,
            ExternalEventId = externalEventId,
            Payload = payload,
            SignatureValid = signatureValid,
            ReceivedAt = DateTime.UtcNow,
            Status = signatureValid ? "received" : "ignored",
        };
        db.InboundEvents.Add(evt);
        try
        {
            await db.SaveChangesAsync(ct);
            return new InboundEventRecord(evt.Id, IsDuplicate: false);
        }
        catch (DbUpdateException ex) when (AppDatabase.IsUniqueViolation(ex))
        {
            var existing = await db.InboundEvents.AsNoTracking()
                .Where(e => e.Provider == provider && e.ExternalEventId == externalEventId)
                .Select(e => e.Id)
                .FirstAsync(ct);
            return new InboundEventRecord(existing, IsDuplicate: true);
        }
    }

    public async Task MarkAsync(Guid id, InboundEventStatus status, string? error, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var value = status.ToString().ToLowerInvariant();
        var now = DateTime.UtcNow;
        await db.InboundEvents.Where(e => e.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, value)
            .SetProperty(e => e.ProcessedAt, now)
            .SetProperty(e => e.LastError, error), ct);
    }
}
