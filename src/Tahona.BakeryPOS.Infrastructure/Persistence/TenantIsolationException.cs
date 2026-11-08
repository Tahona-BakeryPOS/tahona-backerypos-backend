namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>
/// Se intentó guardar una fila de otra empresa, o cambiar la empresa de una fila existente.
/// Nunca es un error de usuario: indica un defecto o un intento de acceso indebido.
/// </summary>
public sealed class TenantIsolationException : Exception
{
    public TenantIsolationException(string entity, Guid entityCompanyId, Guid? currentCompanyId)
        : base($"Escritura de {entity} de la empresa {entityCompanyId} con sesión de la empresa {currentCompanyId?.ToString() ?? "(ninguna)"}.")
    {
    }
}
