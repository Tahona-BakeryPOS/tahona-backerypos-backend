using BakeryPOS.Api.Extensions;
using BakeryPOS.Api.Filters;
using BakeryPOS.Application.Sync.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakeryPOS.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/sincronizacion")]
[ApiVersion("1.0")]
[Authorize]
public class SyncController : ControllerBase
{
    private readonly IMediator _mediator;

    public SyncController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// CU-11: Sincronizar en lote las ventas generadas sin conexión.
    /// Cada venta es idempotente por su SaleId (UUID v7 generado en el dispositivo);
    /// reenviar el mismo lote nunca genera duplicados.
    /// </summary>
    [HttpPost]
    [RequirePermission("ventas.sincronizar")]
    [ProducesResponseType(typeof(SyncSalesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncSales([FromBody] SyncSalesCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToActionResult();
    }
}
