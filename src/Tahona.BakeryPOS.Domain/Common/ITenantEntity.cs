namespace Tahona.BakeryPOS.Domain.Common;

/// <summary>
/// An entity that belongs to a company. The DbContext filters by the authenticated user's company
/// and refuses to save rows of another company: it is the basis of multi-company isolation.
/// </summary>
public interface ITenantEntity
{
    Guid CompanyId { get; }
}
