using BakeryPOS.Api.Extensions;
using BakeryPOS.Api.Filters;
using BakeryPOS.Application.CashShifts.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakeryPOS.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/cash-shifts")]
[ApiVersion("1.0")]
[Authorize]
public class CashShiftsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CashShiftsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// CU-02: Abrir turno de caja.
    /// </summary>
    [HttpPost("open")]
    [RequirePermission("turnos.abrir")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Open([FromBody] OpenCashShiftCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToCreatedResult();
    }

    /// <summary>
    /// CU-03: Cerrar turno de caja.
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [RequirePermission("turnos.cerrar")]
    [ProducesResponseType(typeof(CloseCashShiftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Close(Guid id, [FromBody] CloseCashShiftCommand command)
    {
        // Asegurar que el id de la ruta coincida
        var cmd = command with { CashShiftId = id };
        var result = await _mediator.Send(cmd);
        return result.ToActionResult();
    }
}
