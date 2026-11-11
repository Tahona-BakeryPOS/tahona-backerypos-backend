using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Cash.Sessions;
using Tahona.BakeryPOS.Application.Common.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>
/// Cash sessions (US-5.2 to 5.4): open, income and withdrawals, expected-amount summary and closing. A withdrawal
/// is authorized by a supervisor sending their own token (permission <c>cash.withdrawal</c>).
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}")]
[ApiVersion("1.0")]
[Authorize]
public sealed class CashSessionsController : ControllerBase
{
    private readonly ISender _mediator;

    public CashSessionsController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("cash-registers/{id:guid}/sessions")]
    [Idempotent]
    [RequirePermission(Permissions.ShiftsOpen)]
    [ProducesResponseType<CashSessionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Open(Guid id, [FromBody] OpenSessionRequest body, CancellationToken ct) =>
        (await _mediator.Send(new OpenCashSessionCommand(id, body.OpeningAmount), ct)).ToCreatedResult(HttpContext);

    [HttpGet("cash-registers/{id:guid}/open-session")]
    [RequirePermission(Permissions.ShiftsOpen)]
    [ProducesResponseType<CashSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOpen(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetOpenCashSessionQuery(id), ct)).ToActionResult(HttpContext);

    [HttpGet("cash-reasons")]
    [RequirePermission(Permissions.CashMovements)]
    [ProducesResponseType<IReadOnlyList<CashReasonDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReasons(CancellationToken ct) =>
        (await _mediator.Send(new GetCashReasonsQuery(), ct)).ToActionResult(HttpContext);

    /// <summary><c>kind</c>: income or expense (the latter requires <c>cash.withdrawal</c> and cannot exceed the expected amount).</summary>
    [HttpPost("cash-sessions/{id:guid}/movements")]
    [Idempotent]
    [RequirePermission(Permissions.CashMovements)]
    [ProducesResponseType<CashMovementDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterMovement(Guid id, [FromBody] CashMovementRequest body, CancellationToken ct) =>
        (await _mediator.Send(new RegisterCashMovementCommand(id, body.Kind, body.Amount, body.ReasonId, body.Note), ct)).ToCreatedResult(HttpContext);

    [HttpGet("cash-sessions/{id:guid}/movements")]
    [RequirePermission(Permissions.CashMovements)]
    [ProducesResponseType<IReadOnlyList<CashMovementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovements(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetCashMovementsQuery(id), ct)).ToActionResult(HttpContext);

    /// <summary>Itemized expected amount and pending payments that block the close (D-12).</summary>
    [HttpGet("cash-sessions/{id:guid}/summary")]
    [RequirePermission(Permissions.ShiftsClose)]
    [ProducesResponseType<CashSessionSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetCashSessionSummaryQuery(id), ct)).ToActionResult(HttpContext);

    [HttpPost("cash-sessions/{id:guid}/close")]
    [Idempotent]
    [RequirePermission(Permissions.ShiftsClose)]
    [ProducesResponseType<CashSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Close(Guid id, [FromBody] CloseSessionRequest body, CancellationToken ct) =>
        (await _mediator.Send(new CloseCashSessionCommand(id, body.CountedAmount, body.Notes), ct)).ToActionResult(HttpContext);

    /// <summary><c>status</c>: open or closed; dates refer to the opening.</summary>
    [HttpGet("cash-sessions")]
    [RequirePermission(Permissions.ReportsView)]
    [ProducesResponseType<IReadOnlyList<CashSessionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] Guid? branchId,
        [FromQuery] string? status, CancellationToken ct) =>
        (await _mediator.Send(new GetCashSessionsQuery(from, to, branchId, status), ct)).ToActionResult(HttpContext);
}

public sealed record OpenSessionRequest(decimal OpeningAmount);

/// <param name="Kind"><c>income</c> o <c>expense</c>.</param>
public sealed record CashMovementRequest(string Kind, decimal Amount, Guid ReasonId, string? Note);

public sealed record CloseSessionRequest(decimal CountedAmount, string? Notes);
