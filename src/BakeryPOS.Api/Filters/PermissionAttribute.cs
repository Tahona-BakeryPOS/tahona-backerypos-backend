using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BakeryPOS.Api.Filters;

/// <summary>
/// Filtro de autorización basado en permisos embebidos en el JWT.
/// Uso: [RequirePermission("ventas.crear")]
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
        var user = context.HttpContext.User;

        if (!user.Identity?.IsAuthenticated ?? true)
        {
            context.Result = new JsonResult(new
            {
                codigo = "NO_AUTENTICADO",
                mensaje = "Debe iniciar sesión."
            })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        var permissions = user.FindAll("permission").Select(c => c.Value);
        if (!permissions.Contains(_permission))
        {
            context.Result = new JsonResult(new
            {
                codigo = "SIN_PERMISO",
                mensaje = $"No tiene el permiso requerido: {_permission}"
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
