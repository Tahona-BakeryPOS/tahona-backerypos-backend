using BakeryPOS.Api.Extensions;
using BakeryPOS.Api.Filters;
using BakeryPOS.Application.Products.Commands;
using BakeryPOS.Application.Products.Queries;
using BakeryPOS.SharedKernel.Pagination;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BakeryPOS.Api.Controllers;

[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiVersion("1.0")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// CU-04: Listar productos con paginación, filtro por categoría y búsqueda.
    /// </summary>
    [HttpGet]
    [RequirePermission("productos.ver")]
    [ProducesResponseType(typeof(PagedResult<ProductListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? categoryId,
        [FromQuery] string? search,
        [FromQuery] Guid? priceListId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = new GetProductsQuery(categoryId, search, priceListId, page, pageSize);
        var result = await _mediator.Send(query);
        return result.ToActionResult();
    }

    /// <summary>
    /// CU-09: Crear producto. El SKU es único por organización.
    /// </summary>
    [HttpPost]
    [RequirePermission("catalogo.gestionar")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand command)
    {
        var result = await _mediator.Send(command);
        return result.ToCreatedResult();
    }

    /// <summary>
    /// CU-09: Desactivar producto (nunca se elimina si tiene historial de ventas).
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission("catalogo.gestionar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _mediator.Send(new DeactivateProductCommand(id));
        return result.ToActionResult();
    }

    /// <summary>
    /// CU-09: Cambiar el precio de un producto en una lista.
    /// Abre un nuevo período de vigencia; nunca sobrescribe el precio anterior.
    /// </summary>
    [HttpPost("{id:guid}/precios")]
    [RequirePermission("catalogo.gestionar")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPrice(Guid id, [FromBody] SetProductPriceCommand command)
    {
        var cmd = command with { ProductId = id };
        var result = await _mediator.Send(cmd);
        return result.ToCreatedResult();
    }
}
