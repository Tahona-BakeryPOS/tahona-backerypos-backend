using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Auditing;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.SharedKernel.Pagination;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>The company's change history (read-only): who changed what, with before and after.</summary>
[ApiController]
[Route("api/v{version:apiVersion}/auditoria")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission(Permissions.OrganizationManage)]
public sealed class AuditController : ControllerBase
{
    private readonly ISender _mediator;

    public AuditController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] string? entidad, [FromQuery] Guid? entidadId, [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await _mediator.Send(new GetAuditLogQuery(entidad, entidadId, desde, hasta, page, pageSize), ct)).ToActionResult(HttpContext);

    /// <summary>Tables with recorded changes, for filtering.</summary>
    [HttpGet("entidades")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Entities(CancellationToken ct) =>
        (await _mediator.Send(new GetAuditEntitiesQuery(), ct)).ToActionResult(HttpContext);
}
