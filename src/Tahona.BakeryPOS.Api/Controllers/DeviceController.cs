using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Application.Identity.Devices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>Endpoints que usa el equipo de caja: vincularse con un código y listar quién puede entrar.</summary>
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

    /// <summary>Vincula el equipo. El token de dispositivo de la respuesta solo se entrega una vez.</summary>
    [HttpPost("vincular")]
    [ProducesResponseType<PairedDeviceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Pair([FromBody] PairDeviceCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToActionResult(HttpContext);

    /// <summary>Usuarios con PIN y asignación vigente en la sucursal de la caja del equipo.</summary>
    [HttpGet("usuarios")]
    [ProducesResponseType<IReadOnlyList<DeviceUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Users([FromHeader(Name = AuthController.DeviceTokenHeader)] string? deviceToken, CancellationToken ct) =>
        (await _mediator.Send(new GetDeviceUsersQuery(deviceToken ?? string.Empty), ct)).ToActionResult(HttpContext);
}
