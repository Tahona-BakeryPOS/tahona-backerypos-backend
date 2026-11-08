using System.Net;
using System.Net.Http.Json;
using Tahona.BakeryPOS.Application.Companies.Branches;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.IntegrationTests;

/// <summary>Idempotency-Key: reintentar no duplica; misma clave con otro contenido es un conflicto.</summary>
[Collection(ApiCollection.Name)]
public sealed class IdempotencyTests
{
    private readonly ApiFactory _api;

    public IdempotencyTests(ApiFactory api)
    {
        _api = api;
    }

    [Fact]
    public async Task Retrying_with_the_same_key_returns_the_same_result_without_creating_twice()
    {
        var key = Guid.NewGuid().ToString();
        var body = new { code = "IDEM-1", name = "Sede reintento" };

        var first = await Post(key, body);
        var second = await Post(key, body);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.True(second.Headers.Contains("Idempotent-Replayed"));
        var a = await first.Content.ReadFromJsonAsync<BranchDto>();
        var b = await second.Content.ReadFromJsonAsync<BranchDto>();
        Assert.Equal(a!.Id, b!.Id);

        await using var db = _api.NewContext();
        Assert.Equal(1, await db.Branches.IgnoreQueryFilters().CountAsync(x => x.Code == "IDEM-1"));
    }

    [Fact]
    public async Task Same_key_with_different_content_is_a_conflict()
    {
        var key = Guid.NewGuid().ToString();
        await Post(key, new { code = "IDEM-2", name = "Uno" });
        var second = await Post(key, new { code = "IDEM-3", name = "Otro" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("IDEMPOTENCIA_CONFLICTO", await second.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Business_errors_are_also_replayed_with_the_same_answer()
    {
        var first = await Post(Guid.NewGuid().ToString(), new { code = "IDEM-4", name = "Primera" });
        var key = Guid.NewGuid().ToString();
        var duplicate = await Post(key, new { code = "IDEM-4", name = "Duplicada" });
        var replay = await Post(key, new { code = "IDEM-4", name = "Duplicada" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.True(replay.Headers.Contains("Idempotent-Replayed"));
    }

    [Fact]
    public async Task Missing_key_is_rejected()
    {
        var response = await _api.ClientFor(_api.Data.CompanyA).PostAsJsonAsync("/api/v1/sucursales", new { code = "SIN-CLAVE", name = "X" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("IDEMPOTENCIA_REQUERIDA", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_same_key_in_another_company_is_independent()
    {
        var key = Guid.NewGuid().ToString();
        var inA = await Post(key, new { code = "IDEM-5", name = "En A" });
        var clientB = _api.ClientFor(_api.Data.CompanyB);
        clientB.DefaultRequestHeaders.Add("Idempotency-Key", key);
        var inB = await clientB.PostAsJsonAsync("/api/v1/sucursales", new { code = "IDEM-5", name = "En B" });

        Assert.Equal(HttpStatusCode.Created, inA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, inB.StatusCode);
        Assert.False(inB.Headers.Contains("Idempotent-Replayed"));
    }

    private Task<HttpResponseMessage> Post(string key, object body)
    {
        var client = _api.ClientFor(_api.Data.CompanyA);
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        return client.PostAsJsonAsync("/api/v1/sucursales", body);
    }
}
