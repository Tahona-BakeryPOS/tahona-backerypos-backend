using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Companies.Profile;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>La empresa del token (HU-1.2).</summary>
[ApiController]
[Route("api/v{version:apiVersion}/empresa")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission(Permissions.OrganizacionGestionar)]
public sealed class CompanyController : ControllerBase
{
    private readonly ISender _mediator;

    public CompanyController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType<CompanyDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        (await _mediator.Send(new GetMyCompanyQuery(), ct)).ToVersionedResult(HttpContext, c => c.Version);

    /// <summary>Contacto y zona horaria. Requiere If-Match con el ETag leído.</summary>
    [HttpPut]
    [ProducesResponseType<CompanyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update([FromBody] UpdateCompanyRequest body, CancellationToken ct) =>
        (await _mediator.Send(new UpdateMyCompanyCommand(Request.IfMatch(), body.TradeName, body.Email, body.Phone,
            body.Address, body.CityCode, body.TimeZone), ct)).ToVersionedResult(HttpContext, c => c.Version);
}

public sealed record UpdateCompanyRequest(string? TradeName, string Email, string? Phone, string? Address, string? CityCode, string? TimeZone);
