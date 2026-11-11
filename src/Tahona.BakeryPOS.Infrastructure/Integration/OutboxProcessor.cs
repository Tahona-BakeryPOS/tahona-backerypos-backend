using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using Tahona.BakeryPOS.Infrastructure.Persistence.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tahona.BakeryPOS.Infrastructure.Integration;

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 20;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(2);
    public int MaxAttempts { get; set; } = 10;
}

/// <summary>
/// Delivers outbox messages to their handler. Several API instances can
/// run at once: each batch is claimed with an UPDATE that skips locked rows (FOR UPDATE SKIP
/// LOCKED), so a message is taken by a single instance. A message locked by a dead process is released when the lock expires.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopes, IOptions<OutboxOptions> options, ILogger<OutboxProcessor> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                if (processed == 0) await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox processing cycle failed");
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
    }

    /// <summary>Claims and processes a batch. Public for the integration tests.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        await using var _ = db;

        var batch = await ClaimAsync(db, ct);
        foreach (var message in batch)
        {
            var handler = scope.ServiceProvider.GetServices<IOutboxHandler>().FirstOrDefault(h => h.Type == message.Type);
            try
            {
                if (handler is null)
                    throw new InvalidOperationException($"No handler is registered for message '{message.Type}'.");
                await handler.HandleAsync(new OutboxEnvelope(message.Id, message.CompanyId, message.Type, message.Payload, message.Attempts), ct);
                await db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, OutboxStatus.Done)
                    .SetProperty(m => m.ProcessedAt, DateTime.UtcNow)
                    .SetProperty(m => m.LockedUntil, (DateTime?)null)
                    .SetProperty(m => m.LastError, (string?)null), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var failed = message.Attempts >= _options.MaxAttempts;
                var next = DateTime.UtcNow.Add(Backoff(message.Attempts));
                var error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                _logger.LogWarning(ex, "Message {Id} ({Type}) failed on attempt {Attempt}", message.Id, message.Type, message.Attempts);
                await db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, failed ? OutboxStatus.Failed : OutboxStatus.Pending)
                    .SetProperty(m => m.NextAttemptAt, next)
                    .SetProperty(m => m.LockedUntil, (DateTime?)null)
                    .SetProperty(m => m.LastError, error), ct);
            }
        }
        return batch.Count;
    }

    private Task<List<OutboxMessage>> ClaimAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var lockUntil = now.Add(_options.LockDuration);
        return db.OutboxMessages.FromSql($"""
            UPDATE "int".outbox_messages AS o
            SET status = 'processing', locked_until = {lockUntil}, attempts = o.attempts + 1
            WHERE o.id IN (
                SELECT id FROM "int".outbox_messages
                WHERE (status = 'pending' AND next_attempt_at <= {now})
                   OR (status = 'processing' AND locked_until < {now})
                ORDER BY next_attempt_at
                LIMIT {_options.BatchSize}
                FOR UPDATE SKIP LOCKED)
            RETURNING o.*
            """).AsNoTracking().ToListAsync(ct);
    }

    /// <summary>Exponential retries: 5 s, 10 s, 20 s… up to 1 hour.</summary>
    public static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(3600, 5 * Math.Pow(2, Math.Max(0, attempt - 1))));
}
