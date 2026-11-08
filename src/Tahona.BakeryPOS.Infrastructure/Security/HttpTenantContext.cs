using System.Security.Claims;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Tahona.BakeryPOS.Infrastructure.Security;

/// <summary>
/// Contexto de la petición HTTP: empresa, usuario, permisos y alcance tomados solo de los claims del token.
/// En procesos internos sin petición (outbox) la empresa se fija con <see cref="IBackgroundTenant.Enter"/>.
/// </summary>
public sealed class HttpTenantContext : ITenantContext, IBackgroundTenant
{
    public const string CompanyClaim = "company_id";
    public const string PermissionClaim = "permission";
    public const string BranchClaim = "branch";
    public const string AllBranches = "*";
    public const string DeviceClaim = "device_id";
    public const string CashRegisterClaim = "cash_register_id";
    public const string MustChangePasswordClaim = "pwd_change";

    private readonly IHttpContextAccessor _http;
    private Guid? _backgroundCompany;
    private Guid? _backgroundUser;

    public HttpTenantContext(IHttpContextAccessor http)
    {
        _http = http;
    }

    private ClaimsPrincipal? User => _http.HttpContext?.User is { Identity.IsAuthenticated: true } u ? u : null;

    public Guid? CompanyId => _backgroundCompany ?? (Guid.TryParse(User?.FindFirstValue(CompanyClaim), out var id) ? id : null);

    public Guid? UserId => _backgroundCompany is not null ? _backgroundUser
        : Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub"), out var id) ? id : null;

    public IReadOnlyCollection<string> Permissions =>
        User?.FindAll(PermissionClaim).Select(c => c.Value).ToArray() ?? [];

    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    public bool CanAccessBranch(Guid branchId)
    {
        if (_backgroundCompany is not null) return true;
        var scope = User?.FindAll(BranchClaim).Select(c => c.Value).ToArray() ?? [];
        return scope.Contains(AllBranches) || scope.Contains(branchId.ToString());
    }

    public void Enter(Guid companyId, Guid? userId = null)
    {
        if (User is not null)
            throw new InvalidOperationException("IBackgroundTenant es solo para procesos sin petición HTTP.");
        _backgroundCompany = companyId;
        _backgroundUser = userId;
    }

    public Guid RequiredCompanyId =>
        CompanyId ?? throw new DomainException("EMPRESA_REQUERIDA", "La operación requiere una sesión dentro de una empresa.");
}
