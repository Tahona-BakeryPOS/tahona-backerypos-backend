namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>
/// An attempt was made to save a row of another company, or to change the company of an existing row.
/// It is never a user error: it indicates a defect or an improper access attempt.
/// </summary>
public sealed class TenantIsolationException : Exception
{
    public TenantIsolationException(string entity, Guid entityCompanyId, Guid? currentCompanyId)
        : base($"Escritura de {entity} de la empresa {entityCompanyId} con sesión de la empresa {currentCompanyId?.ToString() ?? "(ninguna)"}.")
    {
    }
}
