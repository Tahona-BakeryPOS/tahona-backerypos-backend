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
/// Products, prices with validity and per-branch availability (US-3.1 to 3.3). Branch prices
/// accept <c>prices.branch</c> instead of <c>catalog.manage</c>: the permission is validated
/// in the use case together with the branch scope.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/products")]
[ApiVersion("1.0")]
[Authorize]
public sealed class ProductsController : ControllerBase
{
    private readonly ISender _mediator;

    public ProductsController(ISender mediator)
    {
        _mediator = mediator;
    }

    /// <summary>With <c>branchId</c>, current price and availability at that branch.</summary>
    [HttpGet]
    [RequirePermission(Permissions.ProductsView)]
    [ProducesResponseType<PagedResult<ProductDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] Guid? branchId, [FromQuery] string? search, [FromQuery] Guid? categoryId,
        [FromQuery] bool includeInactive = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        (await _mediator.Send(new GetProductsQuery(branchId, search, categoryId, includeInactive, page, pageSize), ct)).ToActionResult(HttpContext);

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.ProductsView)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid? branchId, CancellationToken ct) =>
        (await _mediator.Send(new GetProductByIdQuery(id, branchId), ct)).ToVersionedResult(HttpContext, p => p.Version);

    [HttpPost]
    [Idempotent]
    [RequirePermission(Permissions.CatalogManage)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateProductCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return result.ToCreatedResult(HttpContext, result.IsSuccess ? $"/api/v1/products/{result.Value.Id}" : null);
    }

    /// <summary>Edits the product (the SKU does not change). Requires If-Match with the version read.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.CatalogManage)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest body, CancellationToken ct) =>
        (await _mediator.Send(new UpdateProductCommand(id, Request.IfMatch(), body.Name, body.CategoryId, body.UnitId, body.TaxId,
            body.Barcode, body.TrackStock, body.NegativeStockPolicy), ct)).ToVersionedResult(HttpContext, p => p.Version);

    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(Permissions.CatalogManage)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new SetProductActiveCommand(id, false), ct)).ToVersionedResult(HttpContext, p => p.Version);

    [HttpPost("{id:guid}/activate")]
    [RequirePermission(Permissions.CatalogManage)]
    [ProducesResponseType<ProductDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new SetProductActiveCommand(id, true), ct)).ToVersionedResult(HttpContext, p => p.Version);

    /// <summary>Price history: base and per branch.</summary>
    [HttpGet("{id:guid}/prices")]
    [RequirePermission(Permissions.ProductsView)]
    [ProducesResponseType<IReadOnlyList<ProductPriceDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPrices(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetProductPricesQuery(id), ct)).ToActionResult(HttpContext);

    /// <summary>Opens a price validity period; the previous one is closed, not deleted.</summary>
    [HttpPost("{id:guid}/prices")]
    [Idempotent]
    [ProducesResponseType<ProductPriceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetPrice(Guid id, [FromBody] SetPriceRequest body, CancellationToken ct) =>
        (await _mediator.Send(new SetProductPriceCommand(id, body.BranchId, body.Amount, body.IncludesTax ?? true, body.ValidFrom), ct))
            .ToCreatedResult(HttpContext);

    /// <summary>Available or not at a branch (US-3.3).</summary>
    [HttpPut("/api/v{version:apiVersion}/branches/{branchId:guid}/products/{productId:guid}/availability")]
    [RequirePermission(Permissions.BranchPrices)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetAvailability(Guid branchId, Guid productId, [FromBody] SetAvailabilityRequest body, CancellationToken ct) =>
        (await _mediator.Send(new SetProductAvailabilityCommand(branchId, productId, body.IsAvailable), ct)).ToActionResult(HttpContext);
}

/// <param name="NegativeStockPolicy">D-06: <c>allow</c> or <c>block</c>; null = keep the current one.</param>
public sealed record UpdateProductRequest(string Name, Guid CategoryId, Guid UnitId, Guid TaxId, string? Barcode, bool TrackStock,
    string? NegativeStockPolicy = null);

/// <param name="BranchId">null = base price.</param>
/// <param name="IncludesTax">null = true (D-07: list price with tax included).</param>
public sealed record SetPriceRequest(Guid? BranchId, decimal Amount, bool? IncludesTax, DateTime? ValidFrom);

public sealed record SetAvailabilityRequest(bool IsAvailable);
