using BakeryPOS.Api.Extensions;
using BakeryPOS.Api.Filters;
using BakeryPOS.Application.Catalog.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakeryPOS.Api.Controllers;

/// <summary>
/// CU-09: Gestión de categorías, impuestos y listas de precio.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/catalogo")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission("catalogo.gestionar")]
public class CatalogController : ControllerBase
{
    private readonly IMediator _mediator;

    public CatalogController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("categorias")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateProductCategoryCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToCreatedResult();
    }

    [HttpPost("impuestos")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTax([FromBody] CreateTaxCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToCreatedResult();
    }

    [HttpPost("listas-precio")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreatePriceList([FromBody] CreatePriceListCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToCreatedResult();
    }
}
