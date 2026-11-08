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
/// Administración de plataforma (D-09): alta y lista de empresas, sin datos de su operación.
/// El alta no usa Idempotency-Key porque la respuesta trae la contraseña de un solo uso del
/// propietario y no debe quedar guardada; un reintento choca con NIT_DUPLICADO.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/plataforma/empresas")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission(Permissions.PlataformaAdmin)]
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
