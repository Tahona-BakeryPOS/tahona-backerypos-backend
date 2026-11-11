using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Payments;
using Tahona.BakeryPOS.Application.Payments.Gateway;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>
/// Payment methods, payments and Bold integration (docs/payment-integration.md). The gateway keys
/// are received here and never returned. A gateway payment is only approved by the signed webhook or by an
/// audited manual resolution (I-23).
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}")]
[ApiVersion("1.0")]
[Authorize]
public sealed class PaymentsController : ControllerBase
{
    private readonly ISender _mediator;

    public PaymentsController(ISender mediator)
    {
        _mediator = mediator;
    }

    // ---- Payment methods ----

    /// <summary>Used by the backoffice and the POS (to charge).</summary>
    [HttpGet("medios-pago")]
    [ProducesResponseType<IReadOnlyList<PaymentMethodDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMethods(CancellationToken ct) =>
        (await _mediator.Send(new GetPaymentMethodsQuery(), ct)).ToActionResult(HttpContext);

    [HttpPost("medios-pago")]
    [Idempotent]
    [RequirePermission(Permissions.OrganizationManage)]
    [ProducesResponseType<PaymentMethodDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateMethod([FromBody] CreatePaymentMethodCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    [HttpPut("medios-pago/{id:guid}")]
    [RequirePermission(Permissions.OrganizationManage)]
    [ProducesResponseType<PaymentMethodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    public async Task<IActionResult> UpdateMethod(Guid id, [FromBody] UpdatePaymentMethodRequest body, CancellationToken ct) =>
        (await _mediator.Send(new UpdatePaymentMethodCommand(id, Request.IfMatch(), body.Name, body.RequiresReference, body.DisplayOrder, body.IsActive), ct))
            .ToVersionedResult(HttpContext, m => m.Version);

    // ---- Pagos ----

    [HttpGet("pagos/{id:guid}")]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayment(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetPaymentQuery(id), ct)).ToActionResult(HttpContext);

    /// <summary><c>estado</c>: pending, approved, declined, cancelled…</summary>
    [HttpGet("pagos")]
    [RequirePermission(Permissions.ReportsView)]
    [ProducesResponseType<IReadOnlyList<PaymentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayments([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, [FromQuery] string? estado,
        [FromQuery] Guid? sesionCajaId, CancellationToken ct) =>
        (await _mediator.Send(new GetPaymentsQuery(desde, hasta, estado, sesionCajaId), ct)).ToActionResult(HttpContext);

    /// <summary>Manual resolution of a payment with no gateway response (I-23).</summary>
    [HttpPost("pagos/{id:guid}/resolver")]
    [RequirePermission(Permissions.PaymentsResolve)]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolvePaymentRequest body, CancellationToken ct) =>
        (await _mediator.Send(new ResolvePaymentCommand(id, body.Resolution, body.ApprovalNumber, body.Note), ct)).ToActionResult(HttpContext);

    // ---- Bold integration ----

    /// <summary>Status per environment; never returns the keys.</summary>
    [HttpGet("integraciones/pasarela")]
    [RequirePermission(Permissions.OrganizationManage)]
    [ProducesResponseType<IReadOnlyList<GatewayAccountDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGateway(CancellationToken ct) =>
        (await _mediator.Send(new GetGatewayAccountsQuery(), ct)).ToActionResult(HttpContext);

    /// <summary>Stores the (encrypted) keys of an environment; a null key keeps the stored one.</summary>
    [HttpPut("integraciones/pasarela")]
    [RequirePermission(Permissions.OrganizationManage)]
    [ProducesResponseType<GatewayAccountDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveGateway([FromBody] SaveGatewayAccountCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToActionResult(HttpContext);

    [HttpGet("integraciones/pasarela/datafonos")]
    [RequirePermission(Permissions.OrganizationManage)]
    [ProducesResponseType<IReadOnlyList<BoldTerminal>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTerminals(CancellationToken ct) =>
        (await _mediator.Send(new GetBoldTerminalsQuery(), ct)).ToActionResult(HttpContext);

    [HttpGet("integraciones/pasarela/metodos")]
    [RequirePermission(Permissions.OrganizationManage)]
    [ProducesResponseType<IReadOnlyList<BoldMethodStatus>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGatewayMethods(CancellationToken ct) =>
        (await _mediator.Send(new GetBoldMethodsQuery(), ct)).ToActionResult(HttpContext);

    /// <summary>Test charge on the card terminal of a register with an open session (without a sale).</summary>
    [HttpPost("integraciones/pasarela/prueba")]
    [Idempotent]
    [RequirePermission(Permissions.OrganizationManage)]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> TestCharge([FromBody] StartTestChargeCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    [HttpGet("integraciones/pasarela/cajas")]
    [RequirePermission(Permissions.OrganizationManage)]
    [ProducesResponseType<IReadOnlyList<RegisterTerminalDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRegisterTerminals(CancellationToken ct) =>
        (await _mediator.Send(new GetRegisterTerminalsQuery(), ct)).ToActionResult(HttpContext);

    [HttpPut("cajas/{id:guid}/datafono")]
    [RequirePermission(Permissions.OrganizationManage)]
    [ProducesResponseType<RegisterTerminalDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetRegisterTerminal(Guid id, [FromBody] SetTerminalRequest body, CancellationToken ct) =>
        (await _mediator.Send(new SetRegisterTerminalCommand(id, body.TerminalModel, body.TerminalSerial), ct)).ToActionResult(HttpContext);
}

public sealed record UpdatePaymentMethodRequest(string Name, bool RequiresReference, int DisplayOrder, bool IsActive);

/// <param name="Resolution"><c>approved</c>, <c>declined</c> o <c>cancelled</c>.</param>
public sealed record ResolvePaymentRequest(string Resolution, string? ApprovalNumber, string Note);

public sealed record SetTerminalRequest(string TerminalModel, string TerminalSerial);
