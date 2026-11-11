using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Cash.Registers;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Identity.Devices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>The company's cash registers and the codes to pair devices to them.</summary>
[ApiController]
[Route("api/v{version:apiVersion}/cajas")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission(Permissions.OrganizationManage)]
public sealed class CashRegistersController : ControllerBase
{
    private readonly ISender _mediator;

    public CashRegistersController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CashRegisterDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] Guid? branchId, CancellationToken ct) =>
        (await _mediator.Send(new GetCashRegistersQuery(branchId), ct)).ToActionResult(HttpContext);

    [HttpPost]
    [Idempotent]
    [ProducesResponseType<CashRegisterDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCashRegisterCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    /// <summary>One-time code (15 min) to pair a device to this cash register.</summary>
    [HttpPost("{id:guid}/codigos-vinculacion")]
    [ProducesResponseType<PairingCodeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePairingCode(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new CreatePairingCodeCommand(id), ct)).ToCreatedResult(HttpContext);
}
