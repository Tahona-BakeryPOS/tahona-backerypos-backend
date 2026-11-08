using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Identity.Devices;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>Equipos vinculados de la empresa. Desactivar uno revoca su acceso.</summary>
[ApiController]
[Route("api/v{version:apiVersion}/dispositivos")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission(Permissions.OrganizacionGestionar)]
public sealed class DevicesController : ControllerBase
{
    private readonly ISender _mediator;

    public DevicesController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DeviceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        (await _mediator.Send(new GetDevicesQuery(), ct)).ToActionResult(HttpContext);

    [HttpPost("{id:guid}/desactivar")]
    [ProducesResponseType<DeviceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new DeactivateDeviceCommand(id), ct)).ToActionResult(HttpContext);
}
