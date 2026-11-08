using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Inventory;
using Tahona.BakeryPOS.SharedKernel.Pagination;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>
/// Inventario básico (docs/inventory.md): existencias, libro de movimientos, existencia inicial, ajustes
/// con motivo, conteo físico y reconciliación. El saldo nunca se edita: todo cambio es un movimiento.
/// El alcance por sucursal del token se valida en cada caso de uso.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}")]
[ApiVersion("1.0")]
[Authorize]
public sealed class InventoryController : ControllerBase
{
    private readonly ISender _mediator;

    public InventoryController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("existencias")]
    [RequirePermission(Permissions.InventarioVer)]
    [ProducesResponseType<PagedResult<StockRowDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStock([FromQuery] Guid sucursalId, [FromQuery] Guid? ubicacionId, [FromQuery] string? buscar,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await _mediator.Send(new GetStockQuery(sucursalId, ubicacionId, buscar, page, pageSize), ct)).ToActionResult(HttpContext);

    /// <summary><c>tipo</c>: initial, sale, return, void_reversal, adjustment_in o adjustment_out.</summary>
    [HttpGet("movimientos-inventario")]
    [RequirePermission(Permissions.InventarioVer)]
    [ProducesResponseType<PagedResult<StockMovementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovements([FromQuery] Guid sucursalId, [FromQuery] Guid? ubicacionId, [FromQuery] Guid? productoId,
        [FromQuery] string? tipo, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await _mediator.Send(new GetStockMovementsQuery(sucursalId, ubicacionId, productoId, tipo, desde, hasta, page, pageSize), ct))
            .ToActionResult(HttpContext);

    [HttpGet("ubicaciones")]
    [RequirePermission(Permissions.InventarioVer)]
    [ProducesResponseType<IReadOnlyList<StockLocationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLocations([FromQuery] Guid? sucursalId, CancellationToken ct) =>
        (await _mediator.Send(new GetStockLocationsQuery(sucursalId), ct)).ToActionResult(HttpContext);

    [HttpPost("ubicaciones")]
    [Idempotent]
    [RequirePermission(Permissions.InventarioAjustar)]
    [ProducesResponseType<StockLocationDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateLocation([FromBody] CreateStockLocationCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    [HttpGet("inventario/motivos")]
    [RequirePermission(Permissions.InventarioVer)]
    [ProducesResponseType<IReadOnlyList<AdjustmentReasonDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReasons(CancellationToken ct) =>
        (await _mediator.Send(new GetAdjustmentReasonsQuery(), ct)).ToActionResult(HttpContext);

    /// <summary>Existencia inicial (una vez por producto y ubicación).</summary>
    [HttpPost("inventario/inicial")]
    [Idempotent]
    [RequirePermission(Permissions.InventarioAjustar)]
    [ProducesResponseType<IReadOnlyList<StockMovementDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterInitial([FromBody] RegisterInitialStockCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    /// <summary>Ajuste con motivo; cantidad con signo. 422 EXISTENCIAS_INSUFICIENTES solo si no hay autorizador.</summary>
    [HttpPost("inventario/ajustes")]
    [Idempotent]
    [RequirePermission(Permissions.InventarioAjustar)]
    [ProducesResponseType<StockMovementDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterAdjustment([FromBody] RegisterAdjustmentCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    /// <summary>Conteo físico: ajusta cada producto a lo contado y devuelve las diferencias.</summary>
    [HttpPost("inventario/conteos")]
    [Idempotent]
    [RequirePermission(Permissions.InventarioAjustar)]
    [ProducesResponseType<IReadOnlyList<CountResultLine>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterCount([FromBody] RegisterPhysicalCountCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    [HttpGet("inventario/reconciliacion")]
    [RequirePermission(Permissions.InventarioVer)]
    [ProducesResponseType<ReconciliationDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reconcile([FromQuery] Guid? sucursalId, CancellationToken ct) =>
        (await _mediator.Send(new GetReconciliationQuery(sucursalId), ct)).ToActionResult(HttpContext);
}
