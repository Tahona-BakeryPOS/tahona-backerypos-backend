using BakeryPOS.Api.Extensions;
using BakeryPOS.Application.Auth.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakeryPOS.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// CU-01: Inicio de sesión por PIN.
    /// El cajero ingresa su PIN de 4-6 dígitos y la sucursal donde trabaja.
    /// </summary>
    [HttpPost("login-pin")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LoginByPin([FromBody] LoginByPinCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToActionResult();
    }
}
