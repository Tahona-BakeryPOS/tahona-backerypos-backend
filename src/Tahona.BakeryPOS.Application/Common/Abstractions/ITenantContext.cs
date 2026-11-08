namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Quién hace la petición y en qué empresa. Sale del token; nunca de parámetros del cliente.
/// </summary>
public interface ITenantContext
{
    /// <summary>Empresa del token; null en peticiones anónimas, de plataforma o de procesos internos.</summary>
    Guid? CompanyId { get; }

    Guid? UserId { get; }

    IReadOnlyCollection<string> Permissions { get; }

    bool HasPermission(string permission);

    /// <summary>
    /// Alcance por sucursal del token: true si el usuario tiene asignación en todas las sucursales o
    /// en esta. Los módulos con datos por sucursal (precios locales, inventario, caja) lo validan.
    /// </summary>
    bool CanAccessBranch(Guid branchId);

    /// <summary>Empresa del token o excepción: para handlers que solo tienen sentido dentro de una empresa.</summary>
    Guid RequiredCompanyId { get; }
}

/// <summary>
/// Solo para procesos internos sin petición HTTP (outbox, workers): actúa como la empresa indicada, con
/// alcance a todas sus sucursales y sin permisos de usuario. Mantiene el filtro y la protección por empresa.
/// </summary>
public interface IBackgroundTenant
{
    void Enter(Guid companyId, Guid? userId = null);
}
