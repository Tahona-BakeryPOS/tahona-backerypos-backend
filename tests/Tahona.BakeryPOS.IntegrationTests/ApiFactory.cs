using System.Net.Http.Headers;
using System.Net.Http.Json;
using Tahona.BakeryPOS.Application.Cash.Registers;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Companies.Branches;
using Tahona.BakeryPOS.Application.Companies.Platform;
using Tahona.BakeryPOS.Application.Identity.Auth;
using Tahona.BakeryPOS.Application.Identity.Devices;
using Tahona.BakeryPOS.Application.Identity.Users;
using Tahona.BakeryPOS.Domain.Identity;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Tahona.BakeryPOS.IntegrationTests;

/// <summary>
/// Real API against real PostgreSQL (Testcontainers container). It is created once per collection:
/// it applies the migrations and onboards two demo companies through the same endpoints the product uses
/// (plataforma → propietario → sucursal → caja → equipo vinculado → cajero).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Test-only secrets (ephemeral process, ephemeral database); they are useless outside this suite.
    private const string TestJwtSecret = "pruebas-integracion-bakerypos-0123456789abcdef";
    public const string OwnerPassword = "ClavePrueba2026";
    public const string CashierPin = "4321";

    private readonly PostgreSqlContainer _sql = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Seed Data { get; private set; } = null!;

    public string ConnectionString =>
        new Npgsql.NpgsqlConnectionStringBuilder(_sql.GetConnectionString()) { Database = "bakerypos_tests" }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Jwt:Secret", TestJwtSecret);
        builder.UseSetting("Outbox:Enabled", "false"); // the tests process the outbox by hand
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "100000");
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IOutboxHandler, OkOutboxHandler>();
            services.AddScoped<IOutboxHandler, FailingOutboxHandler>();
        });
    }

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        await using (var scope = Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        var a = await OnboardAsync("Panadería A S.A.S.", "800197268", 4, "a");
        var b = await OnboardAsync("Panadería B S.A.S.", "890903938", 8, "b");
        Data = new Seed(a, b);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    /// <summary>Client with a hand-issued token (invented user) to test permissions and scope.</summary>
    public HttpClient ClientFor(Guid? companyId, params string[] permissions)
    {
        var jwt = Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.CreateAccessToken(new AccessTokenRequest(
            Guid.CreateVersion7(), "Usuario de prueba", companyId,
            permissions.Length == 0 ? [Permissions.OrganizationManage] : permissions));
        return ClientWithToken(token.Token);
    }

    public HttpClient ClientWithToken(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public HttpClient DeviceClient(string deviceToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Device-Token", deviceToken);
        return client;
    }

    public AppDbContext NewContext(ITenantContext? tenant = null) =>
        new(AppDatabase.Options(ConnectionString), tenant);

    /// <summary>Full onboarding of a demo company through the API.</summary>
    private async Task<TenantSeed> OnboardAsync(string legalName, string nit, int dv, string suffix)
    {
        var platform = ClientFor(null, Permissions.PlatformAdmin);
        var ownerEmail = $"propietario@{suffix}.test";
        var created = await ReadAsync<CreatedCompanyDto>(await platform.PostAsJsonAsync("/api/v1/plataforma/empresas", new
        {
            legalName, nit, nitCheckDigit = dv, taxRegime = "48", taxResponsibilities = new[] { "R-99-PN" },
            email = $"contacto@{suffix}.test", owner = new { fullName = $"Propietario {suffix.ToUpperInvariant()}", email = ownerEmail },
        }));

        // First sign-in with the one-time password → mandatory change → session in its only company.
        var first = await ReadAsync<AuthSessionDto>(await CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { email = ownerEmail, password = created.Owner.TemporaryPassword }));
        var session = await ReadAsync<AuthSessionDto>(await ClientWithToken(first.AccessToken).PostAsJsonAsync(
            "/api/v1/auth/cambiar-contrasena", new { currentPassword = created.Owner.TemporaryPassword, newPassword = OwnerPassword }));
        var owner = ClientWithToken(session.AccessToken);

        var branch = await ReadAsync<BranchDto>(await PostIdempotent(owner, "/api/v1/sucursales",
            new { code = $"{suffix.ToUpperInvariant()}1", name = $"Sede {suffix.ToUpperInvariant()}1" }));
        var register = await ReadAsync<CashRegisterDto>(await PostIdempotent(owner, "/api/v1/cajas",
            new { branchId = branch.Id, code = "CAJA1", name = "Caja 1" }));
        var code = await ReadAsync<PairingCodeDto>(await owner.PostAsync($"/api/v1/cajas/{register.Id}/codigos-vinculacion", null));
        var device = await ReadAsync<PairedDeviceDto>(await CreateClient().PostAsJsonAsync("/api/v1/dispositivo/vincular",
            new { code = code.Code, deviceName = "Tablet mostrador" }));

        var roles = await owner.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var cashierRole = roles!.Single(r => r.Code == SystemRoles.Cashier);
        var ownerUser = (await owner.GetFromJsonAsync<List<UserDto>>("/api/v1/usuarios"))!.Single();
        var cashier = await ReadAsync<CreatedUserDto>(await owner.PostAsJsonAsync("/api/v1/usuarios", new
        {
            fullName = $"Cajero {suffix.ToUpperInvariant()}", pin = CashierPin,
            assignments = new[] { new { roleId = cashierRole.Id, branchId = branch.Id } },
        }));

        return new TenantSeed(created.Company.Id, ownerUser.Id, ownerEmail, session.AccessToken, ownerUser.Assignments.Single().Id,
            branch.Id, register.Id, device.DeviceId, device.DeviceToken, cashierRole.Id, cashier.User.Id, cashier.User.Assignments.Single().Id);
    }

    public static Task<HttpResponseMessage> PostIdempotent(HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} en {response.RequestMessage?.RequestUri}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public sealed record Seed(TenantSeed A, TenantSeed B)
    {
        public Guid CompanyA => A.CompanyId;
        public Guid CompanyB => B.CompanyId;
        public Guid BranchA => A.BranchId;
        public Guid BranchB => B.BranchId;
    }

    public sealed record TenantSeed(
        Guid CompanyId,
        Guid OwnerUserId,
        string OwnerEmail,
        string OwnerToken,
        Guid OwnerAssignmentId,
        Guid BranchId,
        Guid CashRegisterId,
        Guid DeviceId,
        string DeviceToken,
        Guid CashierRoleId,
        Guid CashierUserId,
        Guid CashierAssignmentId);
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

public sealed class OkOutboxHandler : IOutboxHandler
{
    public static readonly List<OutboxEnvelope> Handled = [];
    public string Type => "test.ok";

    public Task HandleAsync(OutboxEnvelope message, CancellationToken ct)
    {
        lock (Handled) Handled.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class FailingOutboxHandler : IOutboxHandler
{
    public string Type => "test.fail";

    public Task HandleAsync(OutboxEnvelope message, CancellationToken ct) =>
        throw new InvalidOperationException("Proveedor no disponible (simulado en pruebas)");
}
