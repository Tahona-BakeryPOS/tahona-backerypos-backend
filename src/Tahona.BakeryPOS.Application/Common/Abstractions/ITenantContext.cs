namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Who makes the request and in which company. It comes from the token; never from client parameters.
/// </summary>
public interface ITenantContext
{
    /// <summary>The token's company; null in anonymous, platform or internal-process requests.</summary>
    Guid? CompanyId { get; }

    Guid? UserId { get; }

    IReadOnlyCollection<string> Permissions { get; }

    bool HasPermission(string permission);

    /// <summary>
    /// The token's branch scope: true if the user has an assignment at all branches or
    /// at this one. Modules with per-branch data (local prices, inventory, cash) validate it.
    /// </summary>
    bool CanAccessBranch(Guid branchId);

    /// <summary>The token's company or an exception: for handlers that only make sense inside a company.</summary>
    Guid RequiredCompanyId { get; }
}

/// <summary>
/// Only for internal processes with no HTTP request (outbox, workers): acts as the indicated company, with
/// scope over all its branches and no user permissions. It keeps the company filter and protection.
/// </summary>
public interface IBackgroundTenant
{
    void Enter(Guid companyId, Guid? userId = null);
}
