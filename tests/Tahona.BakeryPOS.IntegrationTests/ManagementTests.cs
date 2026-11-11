using System.Net;
using System.Net.Http.Json;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Companies.Branches;
using Tahona.BakeryPOS.Application.Companies.Profile;
using Tahona.BakeryPOS.Application.Identity.Users;
using Tahona.BakeryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.IntegrationTests;

/// <summary>Company onboarding, company data, users and assignments, with ETag concurrency.</summary>
[Collection(ApiCollection.Name)]
public sealed class ManagementTests
{
    private readonly ApiFactory _api;

    public ManagementTests(ApiFactory api)
    {
        _api = api;
    }

    private ApiFactory.TenantSeed A => _api.Data.A;
    private HttpClient Owner => _api.ClientWithToken(A.OwnerToken);

    [Fact]
    public async Task Onboarding_created_system_roles_and_an_owner_with_all_company_permissions()
    {
        var roles = await Owner.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");

        Assert.Equal([SystemRoles.BranchManager, SystemRoles.Cashier, SystemRoles.Owner], roles!.Select(r => r.Code).Order());
        var owner = roles!.Single(r => r.Code == SystemRoles.Owner);
        Assert.Equal(Permissions.All.Count - 1, owner.Permissions.Count);
    }

    [Fact]
    public async Task Duplicate_nit_and_invalid_check_digit_are_rejected()
    {
        var platform = _api.ClientFor(null, Permissions.PlatformAdmin);
        var duplicate = await platform.PostAsJsonAsync("/api/v1/platform/companies", CompanyBody("800197268", 4));
        var badDv = await platform.PostAsJsonAsync("/api/v1/platform/companies", CompanyBody("800197268", 5));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("DUPLICATE_NIT", await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badDv.StatusCode);
        Assert.Contains("INVALID_NIT", await badDv.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Company_update_requires_the_current_version()
    {
        var get = await Owner.GetAsync("/api/v1/company");
        var etag = get.Headers.ETag!.Tag;
        var company = await get.Content.ReadFromJsonAsync<CompanyDto>();
        var body = new { tradeName = "La Espiga", email = company!.Email, timeZone = "America/Bogota" };

        var missing = await Owner.PutAsJsonAsync("/api/v1/company", body);
        var updated = await Put(Owner, "/api/v1/company", body, etag);
        var stale = await Put(Owner, "/api/v1/company", body with { tradeName = "Otra" }, etag);
        var badZone = await Put(Owner, "/api/v1/company", body with { timeZone = "Marte/Olympus" }, updated.Headers.ETag!.Tag);

        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("La Espiga", (await updated.Content.ReadFromJsonAsync<CompanyDto>())!.TradeName);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Contains("VERSION_CONFLICT", await stale.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, badZone.StatusCode);
    }

    [Fact]
    public async Task Branch_is_edited_with_if_match_and_deactivated_not_deleted()
    {
        var branch = await ApiFactory.ReadAsync<BranchDto>(await ApiFactory.PostIdempotent(Owner, "/api/v1/branches",
            new { code = "EDIT-1", name = "Para editar" }));

        var edited = await Put(Owner, $"/api/v1/branches/{branch.Id}", new { name = "Editada", cityCode = "05001" }, branch.Version);
        var deactivated = await ApiFactory.ReadAsync<BranchDto>(await Owner.PostAsync($"/api/v1/branches/{branch.Id}/deactivate", null));
        var cashRegister = await ApiFactory.PostIdempotent(Owner, "/api/v1/cash-registers", new { branchId = branch.Id, code = "C1", name = "Caja" });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("EDIT-1", deactivated.Code);
        Assert.False(deactivated.IsActive);
        Assert.Equal(HttpStatusCode.BadRequest, cashRegister.StatusCode);
        Assert.Contains(await Owner.GetFromJsonAsync<List<BranchDto>>("/api/v1/branches") ?? [], b => b.Id == branch.Id);
    }

    [Fact]
    public async Task Temporary_password_and_pin_never_reach_the_database_in_clear()
    {
        var created = await ApiFactory.ReadAsync<CreatedUserDto>(await Owner.PostAsJsonAsync("/api/v1/users", new
        {
            fullName = "Usuaria secreta", email = $"secreta-{Guid.NewGuid():N}@prueba.test", pin = "9753",
            assignments = new[] { new { roleId = A.CashierRoleId, branchId = A.BranchId } },
        }));

        Assert.False(string.IsNullOrEmpty(created.TemporaryPassword));
        Assert.True(created.User.HasPin);
        await using var db = _api.NewContext();
        var audit = await db.AuditLogs.Where(l => l.EntityId == created.User.Id || l.NewValues!.Contains(created.User.Id.ToString()))
            .Select(l => l.NewValues).ToListAsync();
        Assert.NotEmpty(audit);
        Assert.DoesNotContain(audit, v => v!.Contains(created.TemporaryPassword!) || v.Contains("9753") || v.Contains("$2"));
        Assert.False(await db.IdempotencyRecords.AnyAsync(r => r.ResponseBody!.Contains(created.TemporaryPassword!)));
    }

    [Fact]
    public async Task User_update_needs_if_match_and_shared_users_keep_their_name()
    {
        var get = await Owner.GetAsync($"/api/v1/users/{A.CashierUserId}");
        var etag = get.Headers.ETag!.Tag;

        var missing = await Owner.PutAsJsonAsync($"/api/v1/users/{A.CashierUserId}", new { fullName = "Cajero A", isActive = true });
        var withPin = await Put(Owner, $"/api/v1/users/{A.CashierUserId}", new { fullName = "Cajero A", isActive = true, pin = ApiFactory.CashierPin }, etag);
        var stale = await Put(Owner, $"/api/v1/users/{A.CashierUserId}", new { fullName = "Cajero A", isActive = true }, etag);

        Assert.Equal((HttpStatusCode)428, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withPin.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [Fact]
    public async Task The_last_owner_cannot_be_removed_or_deactivated()
    {
        var self = await Owner.GetAsync($"/api/v1/users/{A.OwnerUserId}");
        var deactivateSelf = await Put(Owner, $"/api/v1/users/{A.OwnerUserId}",
            new { fullName = "Propietario A", isActive = false }, self.Headers.ETag!.Tag);
        var endOwnerRole = await Owner.DeleteAsync($"/api/v1/users/{A.OwnerUserId}/assignments/{A.OwnerAssignmentId}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, deactivateSelf.StatusCode);
        Assert.Contains("SELF_DEACTIVATION", await deactivateSelf.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, endOwnerRole.StatusCode);
        Assert.Contains("LAST_OWNER", await endOwnerRole.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Assignments_are_added_once_and_ended_without_deleting()
    {
        var created = await ApiFactory.ReadAsync<CreatedUserDto>(await Owner.PostAsJsonAsync("/api/v1/users", new
        {
            fullName = "Rotativo", pin = "8642", assignments = new[] { new { roleId = A.CashierRoleId, branchId = A.BranchId } },
        }));
        var userId = created.User.Id;

        var added = await ApiFactory.PostIdempotent(Owner, $"/api/v1/users/{userId}/assignments", new { roleId = A.CashierRoleId });
        var duplicate = await ApiFactory.PostIdempotent(Owner, $"/api/v1/users/{userId}/assignments", new { roleId = A.CashierRoleId });
        var assignment = await ApiFactory.ReadAsync<AssignmentDto>(added);
        var ended = await Owner.DeleteAsync($"/api/v1/users/{userId}/assignments/{assignment.Id}");
        var after = await Owner.GetFromJsonAsync<UserDto>($"/api/v1/users/{userId}");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        var kept = after!.Assignments.Single(a => a.Id == assignment.Id);
        Assert.False(kept.IsCurrent);
        Assert.NotNull(kept.ValidTo);
    }

    private static object CompanyBody(string nit, int dv) => new
    {
        legalName = "Repetida S.A.S.", nit, nitCheckDigit = dv, taxRegime = "48", taxResponsibilities = new[] { "R-99-PN" },
        email = "repetida@prueba.test", owner = new { fullName = "Dueño", email = $"dueno-{Guid.NewGuid():N}@prueba.test" },
    };

    private static Task<HttpResponseMessage> Put<T>(HttpClient client, string url, T body, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", etag.StartsWith('"') ? etag : $"\"{etag}\"");
        return client.SendAsync(request);
    }
}
