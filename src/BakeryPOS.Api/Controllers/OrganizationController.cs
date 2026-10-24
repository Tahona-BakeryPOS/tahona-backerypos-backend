using BakeryPOS.Api.Extensions;
using BakeryPOS.Api.Filters;
using BakeryPOS.Application.Organizations.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakeryPOS.Api.Controllers;

/// <summary>
/// CU-10: Gestión de sucursales, cajas, usuarios y asignaciones de rol.
/// Reservado para el propietario (permiso organizacion.gestionar).
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/organizacion")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission("organizacion.gestionar")]
public class OrganizationController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrganizationController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("sucursales")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateBranch([FromBody] CreateBranchCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToCreatedResult();
    }

    [HttpPost("cajas")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateCashRegister([FromBody] CreateCashRegisterCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToCreatedResult();
    }

    [HttpPost("usuarios")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToCreatedResult();
    }

    [HttpPost("asignaciones")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AssignUserRole([FromBody] AssignUserRoleCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToCreatedResult();
    }
}
