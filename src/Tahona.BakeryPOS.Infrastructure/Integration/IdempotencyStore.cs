using Tahona.BakeryPOS.Infrastructure.Persistence;
using Tahona.BakeryPOS.Infrastructure.Persistence.Integration;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Infrastructure.Integration;

public abstract record IdempotencyDecision
{
    /// <summary>First time: run the operation and then call Complete or Release.</summary>
    public sealed record Proceed : IdempotencyDecision;
    /// <summary>Already completed with the same content: return the stored response.</summary>
    public sealed record Replay(int StatusCode, string? Body) : IdempotencyDecision;
    /// <summary>Same key, different content.</summary>
    public sealed record Mismatch : IdempotencyDecision;
    /// <summary>Another request with the same key is still in progress.</summary>
    public sealed record InProgress : IdempotencyDecision;
}

/// <summary>
/// Store of idempotent responses. It works with its own context and direct SQL so as not to mix
/// with the (possibly failed) changes of the operation it protects. Reserving the key is an
/// INSERT: if two requests arrive at the same time, the database lets only one through.
/// </summary>
public sealed class IdempotencyStore
{
    /// <summary>An uncompleted reservation older than this is considered abandoned (process down).</summary>
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

        // Abandoned reservation: it is taken again only if nobody took it in the meantime (condition in the UPDATE).
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

    /// <summary>The operation failed unexpectedly: the key is released so the client can retry.</summary>
    public async Task ReleaseAsync(Guid companyId, string key, string endpoint, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.IdempotencyRecords
            .Where(r => r.CompanyId == companyId && r.Key == key && r.Endpoint == endpoint && r.Status == "in_progress")
            .ExecuteDeleteAsync(ct);
    }
}
