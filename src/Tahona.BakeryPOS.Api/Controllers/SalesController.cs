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

/// <summary>
/// Sale as a persistent process (architecture.md §4): draft with server prices → payments → confirmed
/// when what was approved covers the total. A Bold payment is only approved by the webhook; the POS polls the status.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/ventas")]
[ApiVersion("1.0")]
[Authorize]
public sealed class SalesController : ControllerBase
{
    private readonly ISender _mediator;

    public SalesController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [Idempotent]
    [RequirePermission(Permissions.SalesCreate)]
    [ProducesResponseType<SaleDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateSaleCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return result.ToCreatedResult(HttpContext, result.IsSuccess ? $"/api/v1/ventas/{result.Value.Id}" : null);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.SalesView)]
    [ProducesResponseType<SaleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetSaleQuery(id), ct)).ToActionResult(HttpContext);

    [HttpPut("{id:guid}/cliente")]
    [RequirePermission(Permissions.SalesCreate)]
    [ProducesResponseType<SaleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetCustomer(Guid id, [FromBody] SetCustomerRequest body, CancellationToken ct) =>
        (await _mediator.Send(new SetSaleCustomerCommand(id, body.CustomerIdentification), ct)).ToActionResult(HttpContext);

    /// <summary>Sent by the authorizing supervisor (their token, permission inventory.adjust).</summary>
    [HttpPost("{id:guid}/autorizar-existencias")]
    [ProducesResponseType<SaleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AuthorizeStock(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new AuthorizeSaleStockCommand(id), ct)).ToActionResult(HttpContext);

    /// <summary>422 EXISTENCIAS_INSUFICIENTES (D-06) or 409 PAGO_EN_CURSO; with cash, <c>amountTendered</c> gives the change.</summary>
    [HttpPost("{id:guid}/pagos")]
    [Idempotent]
    [RequirePermission(Permissions.SalesCreate)]
    [ProducesResponseType<SaleDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddPayment(Guid id, [FromBody] AddPaymentRequest body, CancellationToken ct) =>
        (await _mediator.Send(new AddSalePaymentCommand(id, body.PaymentMethodId, body.Amount, body.AmountTendered, body.Reference), ct))
            .ToCreatedResult(HttpContext);

    [HttpPost("{id:guid}/cancelar")]
    [RequirePermission(Permissions.SalesCreate)]
    [ProducesResponseType<SaleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new CancelSaleCommand(id), ct)).ToActionResult(HttpContext);

    /// <summary>
    /// D-11: a full void while the sale's shift is still open (otherwise 422 ANULACION_NO_PERMITIDA). Sent by whoever
    /// authorizes with their token (permission sales.void).
    /// </summary>
    [HttpPost("{id:guid}/anulacion")]
    [ProducesResponseType<SaleDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Void(Guid id, [FromBody] VoidSaleRequest body, CancellationToken ct) =>
        (await _mediator.Send(new VoidSaleCommand(id, body.Reason), ct)).ToActionResult(HttpContext);

    /// <summary>US-9.2: return authorized on the spot (token of whoever holds returns.approve); cash from the indicated register.</summary>
    [HttpPost("{id:guid}/devoluciones")]
    [Idempotent]
    [ProducesResponseType<SaleDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Return(Guid id, [FromBody] SaleReturnRequest body, CancellationToken ct) =>
        (await _mediator.Send(new CreateSaleReturnCommand(id, body.CashRegisterId, body.Reason, body.Lines), ct)).ToCreatedResult(HttpContext);

    /// <summary><c>estado</c>: draft, pending_payment, confirmed, cancelled, voided; <c>numero</c>: the sale's consecutive number.</summary>
    [HttpGet]
    [RequirePermission(Permissions.SalesView)]
    [ProducesResponseType<PagedResult<SaleListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] Guid? sucursalId,
        [FromQuery] Guid? sesionCajaId, [FromQuery] string? estado, [FromQuery] long? numero, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await _mediator.Send(new GetSalesQuery(desde, hasta, sucursalId, sesionCajaId, estado, page, pageSize, numero), ct)).ToActionResult(HttpContext);
}

public sealed record SetCustomerRequest(string? CustomerIdentification);

public sealed record AddPaymentRequest(Guid PaymentMethodId, decimal Amount, decimal? AmountTendered, string? Reference);

public sealed record VoidSaleRequest(string Reason);

public sealed record SaleReturnRequest(Guid CashRegisterId, string Reason, IReadOnlyList<SaleReturnLineInput> Lines);
