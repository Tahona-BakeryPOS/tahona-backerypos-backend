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
/// Basic inventory (docs/inventory.md): stock, movement ledger, initial stock, adjustments
/// with a reason, physical count and reconciliation. The balance is never edited: every change is a movement.
/// The token's branch scope is validated in every use case.
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

    [HttpGet("stock")]
    [RequirePermission(Permissions.InventoryView)]
    [ProducesResponseType<PagedResult<StockRowDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStock([FromQuery] Guid branchId, [FromQuery] Guid? locationId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await _mediator.Send(new GetStockQuery(branchId, locationId, search, page, pageSize), ct)).ToActionResult(HttpContext);

    /// <summary><c>type</c>: initial, sale, return, void_reversal, adjustment_in o adjustment_out.</summary>
    [HttpGet("inventory-movements")]
    [RequirePermission(Permissions.InventoryView)]
    [ProducesResponseType<PagedResult<StockMovementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovements([FromQuery] Guid branchId, [FromQuery] Guid? locationId, [FromQuery] Guid? productId,
        [FromQuery] string? type, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await _mediator.Send(new GetStockMovementsQuery(branchId, locationId, productId, type, from, to, page, pageSize), ct))
            .ToActionResult(HttpContext);

    [HttpGet("locations")]
    [RequirePermission(Permissions.InventoryView)]
    [ProducesResponseType<IReadOnlyList<StockLocationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLocations([FromQuery] Guid? branchId, CancellationToken ct) =>
        (await _mediator.Send(new GetStockLocationsQuery(branchId), ct)).ToActionResult(HttpContext);

    [HttpPost("locations")]
    [Idempotent]
    [RequirePermission(Permissions.InventoryAdjust)]
    [ProducesResponseType<StockLocationDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateLocation([FromBody] CreateStockLocationCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    [HttpGet("inventory/reasons")]
    [RequirePermission(Permissions.InventoryView)]
    [ProducesResponseType<IReadOnlyList<AdjustmentReasonDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReasons(CancellationToken ct) =>
        (await _mediator.Send(new GetAdjustmentReasonsQuery(), ct)).ToActionResult(HttpContext);

    /// <summary>Initial stock (once per product and location).</summary>
    [HttpPost("inventory/initial")]
    [Idempotent]
    [RequirePermission(Permissions.InventoryAdjust)]
    [ProducesResponseType<IReadOnlyList<StockMovementDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterInitial([FromBody] RegisterInitialStockCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    /// <summary>Adjustment with a reason; signed quantity. 422 INSUFFICIENT_STOCK only if there is no authorizer.</summary>
    [HttpPost("inventory/adjustments")]
    [Idempotent]
    [RequirePermission(Permissions.InventoryAdjust)]
    [ProducesResponseType<StockMovementDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterAdjustment([FromBody] RegisterAdjustmentCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    /// <summary>Physical count: adjusts each product to the counted amount and returns the differences.</summary>
    [HttpPost("inventory/counts")]
    [Idempotent]
    [RequirePermission(Permissions.InventoryAdjust)]
    [ProducesResponseType<IReadOnlyList<CountResultLine>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterCount([FromBody] RegisterPhysicalCountCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    [HttpGet("inventory/reconciliation")]
    [RequirePermission(Permissions.InventoryView)]
    [ProducesResponseType<ReconciliationDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reconcile([FromQuery] Guid? branchId, CancellationToken ct) =>
        (await _mediator.Send(new GetReconciliationQuery(branchId), ct)).ToActionResult(HttpContext);
}
