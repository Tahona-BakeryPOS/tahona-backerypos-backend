using Tahona.BakeryPOS.Infrastructure.Persistence;
using Tahona.BakeryPOS.Infrastructure.Persistence.Integration;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Infrastructure.Integration;

public abstract record IdempotencyDecision
{
    /// <summary>Primera vez: ejecutar la operación y luego llamar a Complete o Release.</summary>
    public sealed record Proceed : IdempotencyDecision;
    /// <summary>Ya se completó con el mismo contenido: devolver la respuesta guardada.</summary>
    public sealed record Replay(int StatusCode, string? Body) : IdempotencyDecision;
    /// <summary>Misma clave, contenido distinto.</summary>
    public sealed record Mismatch : IdempotencyDecision;
    /// <summary>Otra petición con la misma clave sigue en curso.</summary>
    public sealed record InProgress : IdempotencyDecision;
}

/// <summary>
/// Almacén de respuestas idempotentes. Trabaja con su propio contexto y SQL directo para no mezclarse
/// con los cambios (posiblemente fallidos) de la operación que protege. La reserva de la clave es un
/// INSERT: si dos peticiones llegan a la vez, la base deja pasar solo una.
/// </summary>
public sealed class IdempotencyStore
{
    /// <summary>Una reserva sin completar más vieja que esto se considera abandonada (proceso caído).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private readonly IDbContextFactory<AppDbContext> _factory;

    public IdempotencyStore(IDbContextFactory<AppDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IdempotencyDecision> BeginAsync(Guid companyId, string key, string endpoint, string requestHash, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        try
        {
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                CompanyId = companyId, Key = key, Endpoint = endpoint, RequestHash = requestHash, CreatedAt = now,
            });
            await db.SaveChangesAsync(ct);
            return new IdempotencyDecision.Proceed();
        }
        catch (DbUpdateException ex) when (AppDatabase.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
        }

        var existing = await db.IdempotencyRecords.AsNoTracking()
            .FirstAsync(r => r.CompanyId == companyId && r.Key == key && r.Endpoint == endpoint, ct);

        if (existing.RequestHash != requestHash)
            return new IdempotencyDecision.Mismatch();
        if (existing.Status == "completed")
            return new IdempotencyDecision.Replay(existing.ResponseStatus ?? 200, existing.ResponseBody);
        if (now - existing.CreatedAt < StaleAfter)
            return new IdempotencyDecision.InProgress();

        // Reserva abandonada: se toma de nuevo solo si nadie la tomó entre tanto (condición en el UPDATE).
        var taken = await db.IdempotencyRecords
            .Where(r => r.CompanyId == companyId && r.Key == key && r.Endpoint == endpoint
                && r.Status == "in_progress" && r.CreatedAt == existing.CreatedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CreatedAt, now), ct);
        return taken == 1 ? new IdempotencyDecision.Proceed() : new IdempotencyDecision.InProgress();
    }

    public async Task CompleteAsync(Guid companyId, string key, string endpoint, int statusCode, string? body, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        await db.IdempotencyRecords
            .Where(r => r.CompanyId == companyId && r.Key == key && r.Endpoint == endpoint)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, "completed")
                .SetProperty(r => r.ResponseStatus, statusCode)
                .SetProperty(r => r.ResponseBody, body)
                .SetProperty(r => r.CompletedAt, now), ct);
    }

    /// <summary>La operación falló de forma inesperada: se libera la clave para que el cliente reintente.</summary>
    public async Task ReleaseAsync(Guid companyId, string key, string endpoint, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.IdempotencyRecords
            .Where(r => r.CompanyId == companyId && r.Key == key && r.Endpoint == endpoint && r.Status == "in_progress")
            .ExecuteDeleteAsync(ct);
    }
}
