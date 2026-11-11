using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Sales;
using Tahona.BakeryPOS.SharedKernel.Pagination;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>Queries for returns and the sales report (phase 9).</summary>
[ApiController]
[Route("api/v{version:apiVersion}")]
[ApiVersion("1.0")]
[Authorize]
public sealed class SaleReturnsController : ControllerBase
{
    private readonly ISender _mediator;

    public SaleReturnsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("devoluciones")]
    [RequirePermission(Permissions.ReportsView)]
    [ProducesResponseType<PagedResult<SaleReturnListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] Guid? sucursalId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await _mediator.Send(new GetSaleReturnsQuery(desde, hasta, sucursalId, page, pageSize), ct)).ToActionResult(HttpContext);

    /// <summary>Colombian days (yyyy-MM-dd) inclusive; sold excludes voided, returns are separate, and net.</summary>
    [HttpGet("informes/ventas")]
    [RequirePermission(Permissions.ReportsView)]
    [ProducesResponseType<SalesReportDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report([FromQuery] DateOnly desde, [FromQuery] DateOnly hasta, [FromQuery] Guid? sucursalId, CancellationToken ct) =>
        (await _mediator.Send(new GetSalesReportQuery(desde, hasta, sucursalId), ct)).ToActionResult(HttpContext);
}
