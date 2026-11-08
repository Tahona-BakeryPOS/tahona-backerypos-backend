using System.Net;
using System.Net.Http.Json;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Companies.Branches;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.IntegrationTests;

/// <summary>
/// Aislamiento multiempresa (HU-2.3): con el token de la empresa A, nada de la empresa B se puede
/// leer ni escribir, aunque se envíe un identificador válido de B.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TenantIsolationTests
{
    private readonly ApiFactory _api;

    public TenantIsolationTests(ApiFactory api)
    {
        _api = api;
    }

    [Fact]
    public async Task List_returns_only_the_branches_of_the_token_company()
    {
        var branches = await _api.ClientFor(_api.Data.CompanyA).GetFromJsonAsync<List<BranchDto>>("/api/v1/sucursales");

        Assert.NotNull(branches);
        Assert.Contains(branches, b => b.Id == _api.Data.BranchA);
        Assert.DoesNotContain(branches, b => b.Id == _api.Data.BranchB);
    }

    [Fact]
    public async Task Reading_another_company_branch_by_id_answers_not_found()
    {
        var response = await _api.ClientFor(_api.Data.CompanyA).GetAsync($"/api/v1/sucursales/{_api.Data.BranchB}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("NO_ENCONTRADO", body!.Codigo);
        Assert.False(string.IsNullOrEmpty(body.CorrelationId));
    }

    [Fact]
    public async Task Created_branch_belongs_to_the_token_company_whatever_the_client_sends()
    {
        var client = _api.ClientFor(_api.Data.CompanyB);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        // El cuerpo no tiene empresa; un campo extra "companyId" se ignora.
        var response = await client.PostAsJsonAsync("/api/v1/sucursales",
            new { code = "B-NORTE", name = "Sede Norte", companyId = _api.Data.CompanyA });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<BranchDto>();

        await using var db = NewContext(tenant: null);
        var saved = await db.Branches.IgnoreQueryFilters().SingleAsync(b => b.Id == created!.Id);
        Assert.Equal(_api.Data.CompanyB, saved.CompanyId);
    }

    [Fact]
    public async Task Same_code_in_two_companies_is_allowed()
    {
        var client = _api.ClientFor(_api.Data.CompanyB);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.PostAsJsonAsync("/api/v1/sucursales", new { code = "A1", name = "Otra A1" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Requests_without_token_or_permission_are_rejected()
    {
        var anonymous = await _api.CreateClient().GetAsync("/api/v1/sucursales");
        var noPermission = await _api.ClientFor(_api.Data.CompanyA, "otro.permiso").GetAsync("/api/v1/sucursales");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noPermission.StatusCode);
    }

    [Fact]
    public async Task Token_without_company_sees_nothing_and_cannot_create()
    {
        var client = _api.ClientFor(companyId: null);
        var list = await client.GetFromJsonAsync<List<BranchDto>>("/api/v1/sucursales");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var create = await client.PostAsJsonAsync("/api/v1/sucursales", new { code = "X1", name = "X" });

        Assert.Empty(list!);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task DbContext_refuses_to_write_rows_of_another_company()
    {
        await using var db = NewContext(new FixedTenant(_api.Data.CompanyA));
        db.Branches.Add(Branch.Create(_api.Data.CompanyB, "INTRUSA", "Intrusa", null));

        await Assert.ThrowsAsync<TenantIsolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DbContext_does_not_even_load_rows_of_another_company_for_update()
    {
        await using var db = NewContext(new FixedTenant(_api.Data.CompanyA));
        var foreign = await db.Branches.FirstOrDefaultAsync(b => b.Id == _api.Data.BranchB);

        Assert.Null(foreign);
    }

    private AppDbContext NewContext(ITenantContext? tenant) =>
        _api.NewContext(tenant);

    private sealed record ErrorResponse(string Codigo, string Mensaje, string CorrelationId);
}

/// <summary>Contexto de empresa fijo para probar el DbContext sin HTTP.</summary>
internal sealed class FixedTenant(Guid companyId) : ITenantContext
{
    public Guid? CompanyId => companyId;
    public Guid? UserId => Guid.Empty;
    public IReadOnlyCollection<string> Permissions => [];
    public bool HasPermission(string permission) => false;
    public bool CanAccessBranch(Guid branchId) => true;
    public Guid RequiredCompanyId => companyId;
}
