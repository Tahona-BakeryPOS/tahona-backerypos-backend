using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Companies.Branches;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>Branches of the token's company (US-1.3). An id of another company answers 404.</summary>
[ApiController]
[Route("api/v{version:apiVersion}/sucursales")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission(Permissions.OrganizationManage)]
public sealed class BranchesController : ControllerBase
{
    private readonly ISender _mediator;

    public BranchesController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BranchDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        (await _mediator.Send(new GetBranchesQuery(), ct)).ToActionResult(HttpContext);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BranchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetBranchByIdQuery(id), ct)).ToVersionedResult(HttpContext, b => b.Version);

    [HttpPost]
    [Idempotent]
    [ProducesResponseType<BranchDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateBranchCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return result.ToCreatedResult(HttpContext, result.IsSuccess ? $"/api/v1/sucursales/{result.Value.Id}" : null);
    }

    /// <summary>Edits the branch. Requires If-Match with the ETag read.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<BranchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateBranchRequest body, CancellationToken ct) =>
        (await _mediator.Send(new UpdateBranchCommand(id, Request.IfMatch(), body.Name, body.Address, body.CityCode, body.Phone), ct))
            .ToVersionedResult(HttpContext, b => b.Version);

    [HttpPost("{id:guid}/desactivar")]
    [ProducesResponseType<BranchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new DeactivateBranchCommand(id), ct)).ToVersionedResult(HttpContext, b => b.Version);
}

public sealed record UpdateBranchRequest(string Name, string? Address, string? CityCode, string? Phone);
