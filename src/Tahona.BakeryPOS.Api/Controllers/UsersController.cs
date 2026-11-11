using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Identity.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>
/// The company's users and their per-branch roles (US-2.1). Creation does not use Idempotency-Key: the
/// response may carry a one-time password that must not be stored.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission(Permissions.OrganizationManage)]
public sealed class UsersController : ControllerBase
{
    private readonly ISender _mediator;

    public UsersController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("usuarios")]
    [ProducesResponseType<IReadOnlyList<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        (await _mediator.Send(new GetUsersQuery(), ct)).ToActionResult(HttpContext);

    [HttpGet("usuarios/{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetUserByIdQuery(id), ct)).ToVersionedResult(HttpContext, u => u.Version);

    [HttpPost("usuarios")]
    [ProducesResponseType<CreatedUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateUserCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return result.ToCreatedResult(HttpContext, result.IsSuccess ? $"/api/v1/usuarios/{result.Value.User.Id}" : null);
    }

    /// <summary>Name, status and PIN in this company. Requires If-Match with the ETag read.</summary>
    [HttpPut("usuarios/{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserRequest body, CancellationToken ct) =>
        (await _mediator.Send(new UpdateUserCommand(id, Request.IfMatch(), body.FullName, body.IsActive, body.Pin), ct))
            .ToVersionedResult(HttpContext, u => u.Version);

    [HttpPost("usuarios/{id:guid}/asignaciones")]
    [Idempotent]
    [ProducesResponseType<AssignmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddAssignment(Guid id, [FromBody] AssignmentInput body, CancellationToken ct) =>
        (await _mediator.Send(new AddAssignmentCommand(id, body.RoleId, body.BranchId, body.ValidFrom, body.ValidTo), ct))
            .ToCreatedResult(HttpContext);

    /// <summary>Closes the assignment's validity; it does not delete it.</summary>
    [HttpDelete("usuarios/{id:guid}/asignaciones/{asignacionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EndAssignment(Guid id, Guid asignacionId, CancellationToken ct) =>
        (await _mediator.Send(new EndAssignmentCommand(id, asignacionId), ct)).ToActionResult(HttpContext);

    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken ct) =>
        (await _mediator.Send(new GetRolesQuery(), ct)).ToActionResult(HttpContext);
}

public sealed record UpdateUserRequest(string FullName, bool IsActive, string? Pin);
