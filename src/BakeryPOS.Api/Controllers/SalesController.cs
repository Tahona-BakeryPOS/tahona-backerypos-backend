using BakeryPOS.Api.Extensions;
using BakeryPOS.Api.Filters;
using BakeryPOS.Application.Sales.Commands;
using BakeryPOS.Application.Sales.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakeryPOS.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
public class SalesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SalesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// CU-05: Registrar venta (idempotente por saleId UUID v7 generado en el cliente).
    /// </summary>
    [HttpPost]
    [RequirePermission("ventas.crear")]
    [ProducesResponseType(typeof(SaleCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateSaleCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToCreatedResult();
    }

    /// <summary>
    /// CU-07: Anular venta.
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [RequirePermission("ventas.anular")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Void(Guid id, [FromBody] VoidSaleCommand command)
    {
        var cmd = command with { SaleId = id };
        var result = await _mediator.Send(cmd);
        return result.ToActionResult();
    }

    /// <summary>
    /// CU-06: Consultar detalle de una venta.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("ventas.ver")]
    [ProducesResponseType(typeof(SaleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _mediator.Send(new GetSaleByIdQuery(id));
        return result.ToActionResult();
    }
}
