using System.Net;
using System.Net.Http.Json;
using Tahona.BakeryPOS.Application.Cash.Registers;
using Tahona.BakeryPOS.Application.Companies.Profile;
using Tahona.BakeryPOS.Application.Identity.Devices;
using Tahona.BakeryPOS.Application.Identity.Users;

namespace Tahona.BakeryPOS.IntegrationTests;

/// <summary>
/// Criterio de aceptación de la fase 2: dos empresas demo sin acceso cruzado, endpoint por endpoint.
/// El propietario de A (token real, obtenido por login) usa identificadores válidos de B: todo responde
/// 404 como si no existieran, y las listas solo traen lo de A.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrossTenantEndpointTests
{
    private readonly ApiFactory _api;

    public CrossTenantEndpointTests(ApiFactory api)
    {
        _api = api;
    }

    private ApiFactory.TenantSeed A => _api.Data.A;
    private ApiFactory.TenantSeed B => _api.Data.B;

    public static TheoryData<string, string, string> ForeignIdRequests => new()
    {
        // método, ruta con marcadores de B, cuerpo JSON
        { "GET", "/api/v1/sucursales/{branchB}", "" },
        { "PUT", "/api/v1/sucursales/{branchB}", """{"name":"Intrusa"}""" },
        { "POST", "/api/v1/sucursales/{branchB}/desactivar", "" },
        { "POST", "/api/v1/cajas", """{"branchId":"{branchB}","code":"X","name":"X"}""" },
        { "POST", "/api/v1/cajas/{registerB}/codigos-vinculacion", "" },
        { "POST", "/api/v1/dispositivos/{deviceB}/desactivar", "" },
        { "GET", "/api/v1/usuarios/{cashierB}", "" },
        { "PUT", "/api/v1/usuarios/{cashierB}", """{"fullName":"X","isActive":false}""" },
        { "POST", "/api/v1/usuarios/{cashierB}/asignaciones", """{"roleId":"{roleA}"}""" },
        { "DELETE", "/api/v1/usuarios/{cashierB}/asignaciones/{assignmentB}", "" },
        { "DELETE", "/api/v1/usuarios/{cashierA}/asignaciones/{assignmentB}", "" },
        { "POST", "/api/v1/usuarios/{cashierA}/asignaciones", """{"roleId":"{roleB}"}""" },
        { "POST", "/api/v1/usuarios/{cashierA}/asignaciones", """{"roleId":"{roleA}","branchId":"{branchB}"}""" },
        { "POST", "/api/v1/usuarios", """{"fullName":"X","pin":"1111","assignments":[{"roleId":"{roleB}"}]}""" },
        { "POST", "/api/v1/usuarios", """{"fullName":"X","pin":"1111","assignments":[{"roleId":"{roleA}","branchId":"{branchB}"}]}""" },
    };

    [Theory]
    [MemberData(nameof(ForeignIdRequests))]
    public async Task Ids_of_another_company_answer_not_found(string method, string route, string body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), Fill(route));
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("If-Match", "\"AAAAAAAAB9E=\"");
        if (body.Length > 0)
            request.Content = new StringContent(Fill(body), System.Text.Encoding.UTF8, "application/json");

        var response = await _api.ClientWithToken(A.OwnerToken).SendAsync(request);

        Assert.True(response.StatusCode == HttpStatusCode.NotFound,
            $"{method} {route} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Lists_only_show_the_token_company()
    {
        var owner = _api.ClientWithToken(A.OwnerToken);

        var registers = await owner.GetFromJsonAsync<List<CashRegisterDto>>("/api/v1/cajas");
        var devices = await owner.GetFromJsonAsync<List<DeviceDto>>("/api/v1/dispositivos");
        var users = await owner.GetFromJsonAsync<List<UserDto>>("/api/v1/usuarios");
        var roles = await owner.GetFromJsonAsync<List<RoleDto>>("/api/v1/roles");
        var company = await owner.GetFromJsonAsync<CompanyDto>("/api/v1/empresa");

        Assert.Contains(registers!, r => r.Id == A.CashRegisterId);
        Assert.DoesNotContain(registers!, r => r.Id == B.CashRegisterId);
        Assert.DoesNotContain(devices!, d => d.Id == B.DeviceId);
        Assert.DoesNotContain(users!, u => u.Id == B.CashierUserId || u.Id == B.OwnerUserId);
        Assert.Contains(users!, u => u.Id == A.CashierUserId);
        Assert.DoesNotContain(roles!, r => r.Id == B.CashierRoleId);
        Assert.Equal(A.CompanyId, company!.Id);
    }

    [Fact]
    public async Task Cashier_of_one_company_cannot_enter_through_a_device_of_the_other()
    {
        var response = await _api.DeviceClient(B.DeviceToken).PostAsJsonAsync("/api/v1/auth/login-pin",
            new { userId = A.CashierUserId, pin = ApiFactory.CashierPin });
        var users = await _api.DeviceClient(B.DeviceToken).GetFromJsonAsync<List<DeviceUserDto>>("/api/v1/dispositivo/usuarios");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(users!, u => u.UserId == A.CashierUserId);
    }

    [Fact]
    public async Task Platform_admin_without_company_sees_no_company_data()
    {
        var platform = _api.ClientFor(null, Tahona.BakeryPOS.Application.Common.Security.Permissions.PlataformaAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/v1/usuarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.GetAsync("/api/v1/empresa")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientWithToken(A.OwnerToken).GetAsync("/api/v1/plataforma/empresas")).StatusCode);
    }

    private string Fill(string text) => text
        .Replace("{branchB}", B.BranchId.ToString())
        .Replace("{registerB}", B.CashRegisterId.ToString())
        .Replace("{deviceB}", B.DeviceId.ToString())
        .Replace("{cashierA}", A.CashierUserId.ToString())
        .Replace("{cashierB}", B.CashierUserId.ToString())
        .Replace("{assignmentB}", B.CashierAssignmentId.ToString())
        .Replace("{roleA}", A.CashierRoleId.ToString())
        .Replace("{roleB}", B.CashierRoleId.ToString());
}
