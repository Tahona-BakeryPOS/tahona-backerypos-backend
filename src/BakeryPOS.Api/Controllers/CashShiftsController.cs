using BakeryPOS.Api.Extensions;
using BakeryPOS.Api.Filters;
using BakeryPOS.Application.CashMovements.Commands;
using BakeryPOS.Application.CashMovements.Queries;
using BakeryPOS.Application.CashShifts.Commands;
using BakeryPOS.Domain.Enums;
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
    /// CU-07: Cerrar turno de caja.
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

    /// <summary>
    /// CU-06: Registrar ingreso o retiro de efectivo en el turno.
    /// Los retiros requieren el permiso "caja.retiro" (autorización del propietario).
    /// </summary>
    [HttpPost("{id:guid}/movimientos")]
    [RequirePermission("caja.movimientos")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RegisterMovement(Guid id, [FromBody] RegisterCashMovementCommand command)
    {
        var cmd = command with { CashShiftId = id };

        // Los retiros requieren autorización explícita del propietario (CU-06)
        if (cmd.Type == CashMovementType.Expense && !User.HasClaim("permission", "caja.retiro"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                codigo = "SIN_PERMISO",
                mensaje = "Los retiros de efectivo requieren autorización del propietario (permiso caja.retiro)."
            });
        }

        var result = await _mediator.Send(cmd);
        return result.ToCreatedResult();
    }

    /// <summary>
    /// CU-06: Listar los movimientos de efectivo de un turno.
    /// </summary>
    [HttpGet("{id:guid}/movimientos")]
    [RequirePermission("caja.movimientos")]
    [ProducesResponseType(typeof(IReadOnlyList<CashMovementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovements(Guid id)
    {
        var result = await _mediator.Send(new GetCashMovementsByShiftQuery(id));
        return result.ToActionResult();
    }
}
