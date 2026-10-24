using BakeryPOS.Api.Extensions;
using BakeryPOS.Api.Filters;
using BakeryPOS.Application.Reports.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakeryPOS.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/reportes")]
[ApiVersion("1.0")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReportsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// CU-08: Reporte de ventas diarias, consolidado y por sucursal.
    /// Solo lectura; las ventas anuladas se muestran separadas del total vendido.
    /// </summary>
    [HttpGet("ventas-diarias")]
    [RequirePermission("reportes.ver")]
    [ProducesResponseType(typeof(DailySalesReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDailySales(
        [FromQuery] DateOnly desde,
        [FromQuery] DateOnly hasta,
        [FromQuery] Guid? sucursalId)
    {
        var result = await _mediator.Send(new GetDailySalesReportQuery(desde, hasta, sucursalId));
        return result.ToActionResult();
    }
}
