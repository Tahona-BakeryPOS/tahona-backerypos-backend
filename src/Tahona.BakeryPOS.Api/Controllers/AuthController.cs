using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Application.Identity.Auth;
using Tahona.BakeryPOS.Application.Identity.Devices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>
/// Sessions. Backoffice: email + password → short access token + rotating refresh token; with several
/// companies one is chosen (D-08). Cash register: paired device (X-Device-Token header) + PIN.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/auth")]
[ApiVersion("1.0")]
public sealed class AuthController : ControllerBase
{
    public const string DeviceTokenHeader = "X-Device-Token";

    private readonly ISender _mediator;

    public AuthController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType<AuthSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToActionResult(HttpContext);

    /// <summary>Issues tokens for one of the user's companies (also to switch company).</summary>
    [HttpPost("select-company")]
    [Authorize]
    [ProducesResponseType<AuthSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SelectCompany([FromBody] SelectCompanyCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToActionResult(HttpContext);

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType<AuthSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshSessionCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToActionResult(HttpContext);

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToActionResult(HttpContext);

    /// <summary>Changes the password (mandatory after the one-time one) and returns a new session.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType<AuthSessionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToActionResult(HttpContext);

    /// <summary>Register sign-in: paired device + PIN. 5 failed attempts lock for 5 minutes (US-2.4).</summary>
    [HttpPost("login-pin")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimiting.Auth)]
    [ProducesResponseType<AuthSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    public async Task<IActionResult> LoginByPin([FromHeader(Name = DeviceTokenHeader)] string? deviceToken,
        [FromBody] PinLoginRequest body, CancellationToken ct) =>
        (await _mediator.Send(new LoginByPinCommand(deviceToken ?? string.Empty, body.UserId, body.Pin), ct)).ToActionResult(HttpContext);
}

public sealed record PinLoginRequest(Guid UserId, string Pin);
