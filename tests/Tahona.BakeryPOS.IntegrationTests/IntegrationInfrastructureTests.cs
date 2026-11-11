using System.Net;
using System.Net.Http.Json;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Infrastructure.Integration;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using Tahona.BakeryPOS.Infrastructure.Persistence.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tahona.BakeryPOS.IntegrationTests;

/// <summary>Outbox, inbound events, audit and health against real PostgreSQL.</summary>
[Collection(ApiCollection.Name)]
public sealed class IntegrationInfrastructureTests
{
    private readonly ApiFactory _api;

    public IntegrationInfrastructureTests(ApiFactory api)
    {
        _api = api;
    }

    private AppDbContext Db(ITenantContext? tenant = null) => _api.NewContext(tenant);

    [Fact]
    public async Task Health_check_reaches_the_database()
    {
        var response = await _api.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Runs_against_postgresql_with_every_migration_applied()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ProviderName;

        Assert.Contains("Npgsql", provider);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task OpenApi_document_is_published()
    {
        var doc = await _api.CreateClient().GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/v1/sucursales", doc);
    }

    [Fact]
    public async Task Outbox_message_is_saved_in_the_same_transaction_as_the_change()
    {
        var tenant = new FixedTenant(_api.Data.CompanyA);
        await using (var db = Db(tenant))
        {
            db.Branches.Add(Branch.Create(_api.Data.CompanyA, "OUT-1", "Con mensaje", null));
            ((IOutbox)db).Enqueue("test.ok", new { marker = "out-1" });
            await db.SaveChangesAsync();
        }

        // If the change fails (repeated code), the message is not stored either.
        await using (var db = Db(tenant))
        {
            db.Branches.Add(Branch.Create(_api.Data.CompanyA, "OUT-1", "Repetida", null));
            ((IOutbox)db).Enqueue("test.ok", new { marker = "out-1-rollback" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var check = Db();
        var payloads = await check.OutboxMessages.Where(m => m.Payload.Contains("out-1")).Select(m => m.Payload).ToListAsync();
        Assert.Single(payloads);
        Assert.DoesNotContain(payloads, p => p.Contains("rollback"));
    }

    [Fact]
    public async Task Outbox_processor_delivers_messages_and_retries_failures()
    {
        Guid ok, fail;
        await using (var db = Db(new FixedTenant(_api.Data.CompanyA)))
        {
            ((IOutbox)db).Enqueue("test.ok", new { marker = "deliver" });
            ((IOutbox)db).Enqueue("test.fail", new { marker = "retry" });
            await db.SaveChangesAsync();
            ok = db.OutboxMessages.Local.Single(m => m.Type == "test.ok").Id;
            fail = db.OutboxMessages.Local.Single(m => m.Type == "test.fail").Id;
        }

        var processor = _api.Services.GetRequiredService<OutboxProcessor>();
        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0) { }

        await using var check = Db();
        var delivered = await check.OutboxMessages.SingleAsync(m => m.Id == ok);
        var failed = await check.OutboxMessages.SingleAsync(m => m.Id == fail);

        Assert.Equal(OutboxStatus.Done, delivered.Status);
        Assert.Contains(OkOutboxHandler.Handled, m => m.Id == ok && m.CompanyId == _api.Data.CompanyA);
        Assert.Equal(OutboxStatus.Pending, failed.Status);
        Assert.Equal(1, failed.Attempts);
        Assert.True(failed.NextAttemptAt > DateTime.UtcNow);
        Assert.Contains("Proveedor no disponible", failed.LastError);
    }

    [Fact]
    public async Task Inbound_event_is_recorded_once_even_if_delivered_twice()
    {
        await using var scope = _api.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboundEventStore>();
        var id = $"evt-{Guid.NewGuid():N}";

        var first = await store.RecordAsync("wompi", id, "{\"a\":1}", signatureValid: true, CancellationToken.None);
        var second = await store.RecordAsync("wompi", id, "{\"a\":1}", signatureValid: true, CancellationToken.None);
        var otherProvider = await store.RecordAsync("bold", id, "{}", signatureValid: true, CancellationToken.None);

        Assert.False(first.IsDuplicate);
        Assert.True(second.IsDuplicate);
        Assert.Equal(first.Id, second.Id);
        Assert.False(otherProvider.IsDuplicate);
    }

    [Fact]
    public async Task Concurrent_deliveries_of_the_same_event_leave_a_single_row()
    {
        var id = $"evt-{Guid.NewGuid():N}";
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = _api.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IInboundEventStore>()
                .RecordAsync("wompi", id, "{}", true, CancellationToken.None);
        });
        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => !r.IsDuplicate);
        await using var check = Db();
        Assert.Equal(1, await check.InboundEvents.CountAsync(e => e.ExternalEventId == id));
    }

    [Fact]
    public async Task Changes_are_audited_with_user_company_and_correlation_id()
    {
        var client = _api.ClientFor(_api.Data.CompanyA);
        var correlation = Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlation);
        var response = await client.PostAsJsonAsync("/api/v1/sucursales", new { code = "AUD-1", name = "Auditada" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(correlation, response.Headers.GetValues("X-Correlation-Id").Single());

        await using var check = Db();
        // A request can audit several entities (the branch and what is provisioned with it):
        // they all share the correlation id, so the branch's row is looked up.
        var logs = await check.AuditLogs.Where(a => a.CorrelationId == correlation).ToListAsync();
        Assert.All(logs, l => Assert.Equal(_api.Data.CompanyA, l.CompanyId));
        var log = Assert.Single(logs, l => l.Entity == "org.branches");
        Assert.Equal("org.branches", log.Entity);
        Assert.Equal("create", log.Action);
        Assert.Equal(_api.Data.CompanyA, log.CompanyId);
        Assert.NotNull(log.UserId);
        Assert.Contains("AUD-1", log.NewValues);
    }
}
