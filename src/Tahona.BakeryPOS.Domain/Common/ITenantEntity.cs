namespace Tahona.BakeryPOS.Domain.Common;

/// <summary>
/// Entidad que pertenece a una empresa. El DbContext filtra por la empresa del usuario autenticado
/// y rechaza guardar filas de otra empresa: es la base del aislamiento multiempresa.
/// </summary>
public interface ITenantEntity
{
    Guid CompanyId { get; }
}
