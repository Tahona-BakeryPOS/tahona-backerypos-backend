using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Application.Identity.Devices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>Endpoints used by the cash-register device: pair with a code and list who can sign in.</summary>
[ApiController]
[Route("api/v{version:apiVersion}/dispositivo")]
[ApiVersion("1.0")]
[AllowAnonymous]
[EnableRateLimiting(RateLimiting.Auth)]
public sealed class DeviceController : ControllerBase
{
    private readonly ISender _mediator;

    public DeviceController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Pairs the device. The device token in the response is only delivered once.</summary>
    [HttpPost("vincular")]
    [ProducesResponseType<PairedDeviceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Pair([FromBody] PairDeviceCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToActionResult(HttpContext);

    /// <summary>Users with a PIN and a current assignment at the branch of the device's cash register.</summary>
    [HttpGet("usuarios")]
    [ProducesResponseType<IReadOnlyList<DeviceUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Users([FromHeader(Name = AuthController.DeviceTokenHeader)] string? deviceToken, CancellationToken ct) =>
        (await _mediator.Send(new GetDeviceUsersQuery(deviceToken ?? string.Empty), ct)).ToActionResult(HttpContext);
}
