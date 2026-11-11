using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.SharedKernel.Pagination;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;

namespace Tahona.BakeryPOS.Application.Auditing;

/// <param name="Entity">Table with schema, e.g. <c>cat.products</c>.</param>
/// <param name="Action"><c>create</c>, <c>update</c> or <c>delete</c>.</param>
/// <param name="OldValues">JSON of the fields before the change (sensitive ones masked).</param>
/// <param name="NewValues">JSON of the fields after the change.</param>
public sealed record AuditLogDto(Guid Id, DateTime OccurredAt, Guid? UserId, string? UserName, string Entity, Guid EntityId,
    string Action, string? OldValues, string? NewValues);

public sealed record AuditLogFilter(Guid CompanyId, string? Entity, Guid? EntityId, DateTime? From, DateTime? To, int Page, int PageSize);

/// <summary>
/// Reading the audit log (<c>aud.audit_logs</c>, insert-only). It is implemented by
/// infrastructure: the log is cross-cutting and belongs to no module.
/// </summary>
public interface IAuditLogReader
{
    Task<PagedResult<AuditLogDto>> ReadAsync(AuditLogFilter filter, CancellationToken ct);

    /// <summary>Tables with company records, for the screen filter.</summary>
    Task<IReadOnlyList<string>> EntitiesAsync(Guid companyId, CancellationToken ct);
}

/// <summary>Change history of the company in session, newest to oldest.</summary>
public sealed record GetAuditLogQuery(string? Entity, Guid? EntityId, DateTime? From, DateTime? To, int Page = 1, int PageSize = 50)
    : IRequest<Result<PagedResult<AuditLogDto>>>;

public sealed record GetAuditEntitiesQuery : IRequest<Result<IReadOnlyList<string>>>;

public sealed class GetAuditLogValidator : AbstractValidator<GetAuditLogQuery>
{
    public GetAuditLogValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Entity).MaximumLength(100);
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.From <= x.To)
            .WithName("desde").WithMessage("La fecha inicial debe ser anterior a la final.");
    }
}

public sealed class AuditLogHandler :
    IRequestHandler<GetAuditLogQuery, Result<PagedResult<AuditLogDto>>>,
    IRequestHandler<GetAuditEntitiesQuery, Result<IReadOnlyList<string>>>
{
    private readonly IAuditLogReader _reader;
    private readonly ITenantContext _tenant;

    public AuditLogHandler(IAuditLogReader reader, ITenantContext tenant)
    {
        _reader = reader;
        _tenant = tenant;
    }

    public async Task<Result<PagedResult<AuditLogDto>>> Handle(GetAuditLogQuery request, CancellationToken ct) =>
        Result.Success(await _reader.ReadAsync(new AuditLogFilter(_tenant.RequiredCompanyId, request.Entity, request.EntityId,
            request.From?.ToUniversalTime(), request.To?.ToUniversalTime(), request.Page, request.PageSize), ct));

    public async Task<Result<IReadOnlyList<string>>> Handle(GetAuditEntitiesQuery request, CancellationToken ct) =>
        Result.Success(await _reader.EntitiesAsync(_tenant.RequiredCompanyId, ct));
}
