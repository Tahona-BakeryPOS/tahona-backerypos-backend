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
/// Sesiones de caja (HU-5.2 a 5.4): abrir, ingresos y retiros, resumen del esperado y cierre. El retiro
/// lo autoriza un superior enviando su propio token (permiso <c>caja.retiro</c>).
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

    [HttpPost("cajas/{id:guid}/sesiones")]
    [Idempotent]
    [RequirePermission(Permissions.TurnosAbrir)]
    [ProducesResponseType<CashSessionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Open(Guid id, [FromBody] OpenSessionRequest body, CancellationToken ct) =>
        (await _mediator.Send(new OpenCashSessionCommand(id, body.OpeningAmount), ct)).ToCreatedResult(HttpContext);

    [HttpGet("cajas/{id:guid}/sesion-abierta")]
    [RequirePermission(Permissions.TurnosAbrir)]
    [ProducesResponseType<CashSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOpen(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetOpenCashSessionQuery(id), ct)).ToActionResult(HttpContext);

    [HttpGet("motivos-caja")]
    [RequirePermission(Permissions.CajaMovimientos)]
    [ProducesResponseType<IReadOnlyList<CashReasonDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReasons(CancellationToken ct) =>
        (await _mediator.Send(new GetCashReasonsQuery(), ct)).ToActionResult(HttpContext);

    /// <summary><c>kind</c>: income o expense (este exige <c>caja.retiro</c> y no puede superar el esperado).</summary>
    [HttpPost("sesiones-caja/{id:guid}/movimientos")]
    [Idempotent]
    [RequirePermission(Permissions.CajaMovimientos)]
    [ProducesResponseType<CashMovementDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> RegisterMovement(Guid id, [FromBody] CashMovementRequest body, CancellationToken ct) =>
        (await _mediator.Send(new RegisterCashMovementCommand(id, body.Kind, body.Amount, body.ReasonId, body.Note), ct)).ToCreatedResult(HttpContext);

    [HttpGet("sesiones-caja/{id:guid}/movimientos")]
    [RequirePermission(Permissions.CajaMovimientos)]
    [ProducesResponseType<IReadOnlyList<CashMovementDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovements(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetCashMovementsQuery(id), ct)).ToActionResult(HttpContext);

    /// <summary>Esperado desglosado y pagos pendientes que bloquean el cierre (D-12).</summary>
    [HttpGet("sesiones-caja/{id:guid}/resumen")]
    [RequirePermission(Permissions.TurnosCerrar)]
    [ProducesResponseType<CashSessionSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetCashSessionSummaryQuery(id), ct)).ToActionResult(HttpContext);

    [HttpPost("sesiones-caja/{id:guid}/cierre")]
    [Idempotent]
    [RequirePermission(Permissions.TurnosCerrar)]
    [ProducesResponseType<CashSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Close(Guid id, [FromBody] CloseSessionRequest body, CancellationToken ct) =>
        (await _mediator.Send(new CloseCashSessionCommand(id, body.CountedAmount, body.Notes), ct)).ToActionResult(HttpContext);

    /// <summary><c>estado</c>: open o closed; fechas sobre la apertura.</summary>
    [HttpGet("sesiones-caja")]
    [RequirePermission(Permissions.ReportesVer)]
    [ProducesResponseType<IReadOnlyList<CashSessionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] Guid? sucursalId,
        [FromQuery] string? estado, CancellationToken ct) =>
        (await _mediator.Send(new GetCashSessionsQuery(desde, hasta, sucursalId, estado), ct)).ToActionResult(HttpContext);
}

public sealed record OpenSessionRequest(decimal OpeningAmount);

/// <param name="Kind"><c>income</c> o <c>expense</c>.</param>
public sealed record CashMovementRequest(string Kind, decimal Amount, Guid ReasonId, string? Note);

public sealed record CloseSessionRequest(decimal CountedAmount, string? Notes);
