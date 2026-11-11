using Tahona.BakeryPOS.Api.Middleware;
using Tahona.BakeryPOS.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Tahona.BakeryPOS.Api.Filters;

/// <summary>
/// Requires a permission (the token's "permission" claim). Usage: <c>[RequirePermission(Permissions.X)]</c>.
/// The scope (company) is guaranteed by the DbContext filter; this filter only validates the permission.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _permission;

    public RequirePermissionAttribute(string permission)
    {
        _permission = permission;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new ObjectResult(ErrorBody.From("NO_AUTENTICADO", "Debe iniciar sesión.", http))
            { StatusCode = StatusCodes.Status401Unauthorized };
            return;
        }

        if (!http.User.FindAll(HttpTenantContext.PermissionClaim).Any(c => c.Value == _permission))
        {
            context.Result = new ObjectResult(ErrorBody.From("SIN_PERMISO", $"No tiene el permiso requerido: {_permission}", http))
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
