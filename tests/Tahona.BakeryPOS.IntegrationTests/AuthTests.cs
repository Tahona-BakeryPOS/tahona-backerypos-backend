using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Identity.Auth;
using Tahona.BakeryPOS.Application.Identity.Devices;
using Tahona.BakeryPOS.Application.Identity.Users;
using Tahona.BakeryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.IntegrationTests;

/// <summary>Backoffice login, several companies (D-08), rotating refresh and cash-register PIN sign-in.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuthTests
{
    private readonly ApiFactory _api;

    public AuthTests(ApiFactory api)
    {
        _api = api;
    }

    private ApiFactory.TenantSeed A => _api.Data.A;
    private ApiFactory.TenantSeed B => _api.Data.B;

    [Fact]
    public async Task Owner_with_one_company_enters_directly_with_its_permissions()
    {
        var session = await LoginAsync(A.OwnerEmail, ApiFactory.OwnerPassword);

        Assert.Equal(A.CompanyId, session.CompanyId);
        Assert.Contains("organization.manage", session.Permissions);
        Assert.DoesNotContain("platform.admin", session.Permissions);
        Assert.False(string.IsNullOrEmpty(session.RefreshToken));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
        Assert.Equal(A.CompanyId.ToString(), jwt.Claims.Single(c => c.Type == "company_id").Value);
        Assert.Equal("*", jwt.Claims.Single(c => c.Type == "branch").Value);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer()
    {
        var wrong = await PostLogin(A.OwnerEmail + ".x", "nada");
        var unknown = await PostLogin("nadie@ninguna.test", "nada");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task One_time_password_session_can_only_change_the_password()
    {
        var (email, temporary) = await CreateBackofficeUserAsync(A, "temporal");
        var session = await LoginAsync(email, temporary);

        Assert.True(session.MustChangePassword);
        Assert.Null(session.CompanyId);
        Assert.Empty(session.Permissions);
        var client = _api.ClientWithToken(session.AccessToken);
        var select = await client.PostAsJsonAsync("/api/v1/auth/select-company", new { companyId = A.CompanyId });
        Assert.Equal(HttpStatusCode.Forbidden, select.StatusCode);
        Assert.Contains("PASSWORD_CHANGE_REQUIRED", await select.Content.ReadAsStringAsync());

        var weak = await client.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = temporary, newPassword = "corta" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
    }

    [Fact]
    public async Task User_in_two_companies_chooses_one_and_the_token_carries_only_that_one()
    {
        var (email, temporary) = await CreateBackofficeUserAsync(A, "contador");
        // B's owner adds the same person by their email: they do not receive a new password.
        var addedInB = await ApiFactory.ReadAsync<CreatedUserDto>(await _api.ClientWithToken(B.OwnerToken).PostAsJsonAsync("/api/v1/users",
            new { fullName = "Otro nombre", email, assignments = new[] { new { roleId = B.CashierRoleId } } }));
        Assert.Null(addedInB.TemporaryPassword);

        var first = await LoginAsync(email, temporary);
        var changed = await ApiFactory.ReadAsync<AuthSessionDto>(await _api.ClientWithToken(first.AccessToken)
            .PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = temporary, newPassword = "OtraClave2026" }));

        Assert.Null(changed.CompanyId);
        Assert.Equal(new[] { A.CompanyId, B.CompanyId }.Order(), changed.Companies.Select(c => c.Id).Order());
        var inB = await ApiFactory.ReadAsync<AuthSessionDto>(await _api.ClientWithToken(changed.AccessToken)
            .PostAsJsonAsync("/api/v1/auth/select-company", new { companyId = B.CompanyId, refreshToken = changed.RefreshToken }));
        Assert.Equal(B.CompanyId, inB.CompanyId);
        Assert.Contains("sales.create", inB.Permissions);

        // The previous session's refresh token was revoked on switching company.
        var oldRefresh = await _api.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = changed.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
    }

    [Fact]
    public async Task Selecting_a_company_without_membership_answers_not_found()
    {
        var session = await LoginAsync(A.OwnerEmail, ApiFactory.OwnerPassword);
        var response = await _api.ClientWithToken(session.AccessToken)
            .PostAsJsonAsync("/api/v1/auth/select-company", new { companyId = B.CompanyId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_and_reusing_an_old_token_closes_every_session()
    {
        var session = await LoginAsync(A.OwnerEmail, ApiFactory.OwnerPassword);
        var other = await LoginAsync(A.OwnerEmail, ApiFactory.OwnerPassword);

        var rotated = await ApiFactory.ReadAsync<AuthSessionDto>(await Refresh(session.RefreshToken!));
        Assert.NotEqual(session.RefreshToken, rotated.RefreshToken);
        Assert.Equal(A.CompanyId, rotated.CompanyId);

        // Someone reuses the already-rotated token: all of the user's sessions are revoked.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(session.RefreshToken!)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(rotated.RefreshToken!)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Refresh(other.RefreshToken!)).StatusCode);

        await using var db = _api.NewContext();
        var tokens = await db.RefreshTokens.Where(t => t.UserId == A.OwnerUserId).ToListAsync();
        Assert.All(tokens, t => Assert.NotNull(t.RevokedAt));
        Assert.DoesNotContain(tokens, t => t.TokenHash == session.RefreshToken); // only the hash is stored
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_user()
    {
        var (email, temporary) = await CreateBackofficeUserAsync(A, "bloqueo");
        for (var i = 1; i < LockoutPolicy.MaxFailedAttempts; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await PostLogin(email, "equivocada")).StatusCode);

        Assert.Equal(HttpStatusCode.Locked, (await PostLogin(email, "equivocada")).StatusCode);
        Assert.Equal(HttpStatusCode.Locked, (await PostLogin(email, temporary)).StatusCode);
    }

    [Fact]
    public async Task Cashier_enters_with_pin_on_a_paired_device_with_branch_scope()
    {
        var device = _api.DeviceClient(A.DeviceToken);
        var users = await device.GetFromJsonAsync<List<DeviceUserDto>>("/api/v1/device/users");
        Assert.Contains(users!, u => u.UserId == A.CashierUserId);
        Assert.DoesNotContain(users!, u => u.UserId == A.OwnerUserId); // no PIN

        var session = await ApiFactory.ReadAsync<AuthSessionDto>(await device.PostAsJsonAsync("/api/v1/auth/login-pin",
            new { userId = A.CashierUserId, pin = ApiFactory.CashierPin }));

        Assert.Equal(A.CompanyId, session.CompanyId);
        Assert.Null(session.RefreshToken);
        Assert.Contains("sales.create", session.Permissions);
        Assert.DoesNotContain("organization.manage", session.Permissions);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken).Claims.ToList();
        Assert.Equal(A.BranchId.ToString(), claims.Single(c => c.Type == "branch").Value);
        Assert.Equal(A.DeviceId.ToString(), claims.Single(c => c.Type == "device_id").Value);
        Assert.Equal(A.CashRegisterId.ToString(), claims.Single(c => c.Type == "cash_register_id").Value);
    }

    [Fact]
    public async Task Five_wrong_pins_lock_the_cashier_for_five_minutes_and_it_is_audited()
    {
        var userId = await CreateCashierAsync(A, "Cajero bloqueo", "2468");
        var device = _api.DeviceClient(A.DeviceToken);
        for (var i = 1; i < LockoutPolicy.MaxFailedAttempts; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await PinLogin(device, userId, "0000")).StatusCode);

        var locked = await PinLogin(device, userId, "0000");
        var evenRight = await PinLogin(device, userId, "2468");

        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        Assert.Equal(HttpStatusCode.Locked, evenRight.StatusCode);
        Assert.Contains("USER_LOCKED", await evenRight.Content.ReadAsStringAsync());

        await using var db = _api.NewContext();
        var membership = await db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant]).SingleAsync(m => m.UserId == userId);
        Assert.True(membership.PinLockedUntil > DateTime.UtcNow.AddMinutes(4));
        Assert.True(await db.AuditLogs.AnyAsync(l => l.EntityId == membership.Id && l.NewValues!.Contains("PinLockedUntil")));
        Assert.False(await db.AuditLogs.AnyAsync(l => l.EntityId == membership.Id && l.NewValues!.Contains("$2")),
            "La auditoría no debe contener hashes de PIN.");
    }

    [Fact]
    public async Task Cashier_without_assignment_in_the_device_branch_cannot_enter()
    {
        var owner = _api.ClientWithToken(A.OwnerToken);
        var otherBranch = await ApiFactory.ReadAsync<Tahona.BakeryPOS.Application.Companies.Branches.BranchDto>(
            await ApiFactory.PostIdempotent(owner, "/api/v1/branches", new { code = "A-OTRA", name = "Otra sede" }));
        var userId = await CreateCashierAsync(A, "Cajero de otra sede", "1357", otherBranch.Id);

        var response = await PinLogin(_api.DeviceClient(A.DeviceToken), userId, "1357");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deactivated_device_and_unknown_token_are_rejected()
    {
        var owner = _api.ClientWithToken(A.OwnerToken);
        var code = await ApiFactory.ReadAsync<PairingCodeDto>(await owner.PostAsync($"/api/v1/cash-registers/{A.CashRegisterId}/pairing-codes", null));
        var paired = await ApiFactory.ReadAsync<PairedDeviceDto>(await _api.CreateClient().PostAsJsonAsync("/api/v1/device/pair",
            new { code = code.Code.ToLowerInvariant(), deviceName = "Equipo temporal" }));
        var reused = await _api.CreateClient().PostAsJsonAsync("/api/v1/device/pair", new { code = code.Code, deviceName = "Otro" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);

        await owner.PostAsync($"/api/v1/devices/{paired.DeviceId}/deactivate", null);
        var afterDeactivation = await PinLogin(_api.DeviceClient(paired.DeviceToken), A.CashierUserId, ApiFactory.CashierPin);
        var unknown = await PinLogin(_api.DeviceClient("token-inventado"), A.CashierUserId, ApiFactory.CashierPin);

        Assert.Equal(HttpStatusCode.Forbidden, afterDeactivation.StatusCode);
        Assert.Contains("DEVICE_NOT_PAIRED", await afterDeactivation.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
    }

    private Task<HttpResponseMessage> PostLogin(string email, string password) =>
        _api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private async Task<AuthSessionDto> LoginAsync(string email, string password) =>
        await ApiFactory.ReadAsync<AuthSessionDto>(await PostLogin(email, password));

    private Task<HttpResponseMessage> Refresh(string refreshToken) =>
        _api.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });

    private static Task<HttpResponseMessage> PinLogin(HttpClient device, Guid userId, string pin) =>
        device.PostAsJsonAsync("/api/v1/auth/login-pin", new { userId, pin });

    private async Task<(string Email, string TemporaryPassword)> CreateBackofficeUserAsync(ApiFactory.TenantSeed tenant, string name)
    {
        var email = $"{name}-{Guid.NewGuid():N}@prueba.test";
        var created = await ApiFactory.ReadAsync<CreatedUserDto>(await _api.ClientWithToken(tenant.OwnerToken).PostAsJsonAsync("/api/v1/users",
            new { fullName = name, email, assignments = new[] { new { roleId = tenant.CashierRoleId } } }));
        return (email, created.TemporaryPassword!);
    }

    private async Task<Guid> CreateCashierAsync(ApiFactory.TenantSeed tenant, string name, string pin, Guid? branchId = null)
    {
        var created = await ApiFactory.ReadAsync<CreatedUserDto>(await _api.ClientWithToken(tenant.OwnerToken).PostAsJsonAsync("/api/v1/users",
            new { fullName = name, pin, assignments = new[] { new { roleId = tenant.CashierRoleId, branchId = branchId ?? tenant.BranchId } } }));
        return created.User.Id;
    }
}
