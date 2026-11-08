using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Catalog.Products;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.SharedKernel.Pagination;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>
/// Productos, precios con vigencia y disponibilidad por sucursal (HU-3.1 a 3.3). Los precios de
/// sucursal admiten <c>precios.sucursal</c> en lugar de <c>catalogo.gestionar</c>: el permiso se valida
/// en el caso de uso junto con el alcance por sucursal.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/productos")]
[ApiVersion("1.0")]
[Authorize]
public sealed class ProductsController : ControllerBase
{
    private readonly ISender _mediator;

    public ProductsController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Con <c>sucursalId</c>, precio vigente y disponibilidad en esa sucursal.</summary>
    [HttpGet]
    [RequirePermission(Permissions.ProductosVer)]
    [ProducesResponseType<PagedResult<ProductDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] Guid? sucursalId, [FromQuery] string? buscar, [FromQuery] Guid? categoriaId,
        [FromQuery] bool incluirInactivos = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await _mediator.Send(new GetProductsQuery(sucursalId, buscar, categoriaId, incluirInactivos, page, pageSize), ct)).ToActionResult(HttpContext);

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.ProductosVer)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid? sucursalId, CancellationToken ct) =>
        (await _mediator.Send(new GetProductByIdQuery(id, sucursalId), ct)).ToVersionedResult(HttpContext, p => p.Version);

    [HttpPost]
    [Idempotent]
    [RequirePermission(Permissions.CatalogoGestionar)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return result.ToCreatedResult(HttpContext, result.IsSuccess ? $"/api/v1/productos/{result.Value.Id}" : null);
    }

    /// <summary>Edita el producto (el SKU no cambia). Requiere If-Match con la versión leída.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.CatalogoGestionar)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest body, CancellationToken ct) =>
        (await _mediator.Send(new UpdateProductCommand(id, Request.IfMatch(), body.Name, body.CategoryId, body.UnitId, body.TaxId,
            body.Barcode, body.TrackStock, body.NegativeStockPolicy), ct)).ToVersionedResult(HttpContext, p => p.Version);

    [HttpPost("{id:guid}/desactivar")]
    [RequirePermission(Permissions.CatalogoGestionar)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new SetProductActiveCommand(id, false), ct)).ToVersionedResult(HttpContext, p => p.Version);

    [HttpPost("{id:guid}/activar")]
    [RequirePermission(Permissions.CatalogoGestionar)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new SetProductActiveCommand(id, true), ct)).ToVersionedResult(HttpContext, p => p.Version);

    /// <summary>Historial de precios: base y por sucursal.</summary>
    [HttpGet("{id:guid}/precios")]
    [RequirePermission(Permissions.ProductosVer)]
    [ProducesResponseType<IReadOnlyList<ProductPriceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPrices(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetProductPricesQuery(id), ct)).ToActionResult(HttpContext);

    /// <summary>Abre una vigencia de precio; la anterior se cierra, no se borra.</summary>
    [HttpPost("{id:guid}/precios")]
    [Idempotent]
    [ProducesResponseType<ProductPriceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetPrice(Guid id, [FromBody] SetPriceRequest body, CancellationToken ct) =>
        (await _mediator.Send(new SetProductPriceCommand(id, body.BranchId, body.Amount, body.IncludesTax ?? true, body.ValidFrom), ct))
            .ToCreatedResult(HttpContext);

    /// <summary>Disponible o no en una sucursal (HU-3.3).</summary>
    [HttpPut("/api/v{version:apiVersion}/sucursales/{branchId:guid}/productos/{productId:guid}/disponibilidad")]
    [RequirePermission(Permissions.PreciosSucursal)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetAvailability(Guid branchId, Guid productId, [FromBody] SetAvailabilityRequest body, CancellationToken ct) =>
        (await _mediator.Send(new SetProductAvailabilityCommand(branchId, productId, body.IsAvailable), ct)).ToActionResult(HttpContext);
}

/// <param name="NegativeStockPolicy">D-06: <c>allow</c> o <c>block</c>; null = conservar la actual.</param>
public sealed record UpdateProductRequest(string Name, Guid CategoryId, Guid UnitId, Guid TaxId, string? Barcode, bool TrackStock,
    string? NegativeStockPolicy = null);

/// <param name="BranchId">null = precio base.</param>
/// <param name="IncludesTax">null = true (D-07: precio de lista con impuesto incluido).</param>
public sealed record SetPriceRequest(Guid? BranchId, decimal Amount, bool? IncludesTax, DateTime? ValidFrom);

public sealed record SetAvailabilityRequest(bool IsAvailable);
