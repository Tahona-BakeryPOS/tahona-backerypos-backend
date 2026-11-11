using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Companies.Platform;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>
/// Platform administration (D-09): company onboarding and listing, with no data about their operation.
/// Onboarding does not use Idempotency-Key because the response carries the owner's one-time password
/// and it must not be stored; a retry collides with DUPLICATE_NIT.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/platform/companies")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission(Permissions.PlatformAdmin)]
public sealed class PlatformCompaniesController : ControllerBase
{
    private readonly ISender _mediator;

    public PlatformCompaniesController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlatformCompanyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        (await _mediator.Send(new GetCompaniesQuery(), ct)).ToActionResult(HttpContext);

    [HttpPost]
    [ProducesResponseType<CreatedCompanyDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCompanyCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);
}
