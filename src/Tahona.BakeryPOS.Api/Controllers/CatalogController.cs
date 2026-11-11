using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Catalog.Categories;
using Tahona.BakeryPOS.Application.Catalog.Measures;
using Tahona.BakeryPOS.Application.Catalog.Taxes;
using Tahona.BakeryPOS.Application.Common.Security;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>Supporting catalog data: categories, taxes and units of measure.</summary>
[ApiController]
[Route("api/v{version:apiVersion}")]
[ApiVersion("1.0")]
[Authorize]
[RequirePermission(Permissions.CatalogManage)]
public sealed class CatalogController : ControllerBase
{
    private readonly ISender _mediator;

    public CatalogController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("categorias")]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken ct) =>
        (await _mediator.Send(new GetCategoriesQuery(), ct)).ToActionResult(HttpContext);

    [HttpPost("categorias")]
    [Idempotent]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    /// <summary>Name, order and status. Requires If-Match with the version read.</summary>
    [HttpPut("categorias/{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryRequest body, CancellationToken ct) =>
        (await _mediator.Send(new UpdateCategoryCommand(id, Request.IfMatch(), body.Name, body.DisplayOrder, body.IsActive), ct))
            .ToVersionedResult(HttpContext, c => c.Version);

    [HttpGet("impuestos")]
    [ProducesResponseType<IReadOnlyList<TaxDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTaxes(CancellationToken ct) =>
        (await _mediator.Send(new GetTaxesQuery(), ct)).ToActionResult(HttpContext);

    [HttpPost("impuestos")]
    [Idempotent]
    [ProducesResponseType<TaxDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTax([FromBody] CreateTaxCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    /// <summary>Name and status (the rate does not change). Requires If-Match with the version read.</summary>
    [HttpPut("impuestos/{id:guid}")]
    [ProducesResponseType<TaxDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> UpdateTax(Guid id, [FromBody] UpdateTaxRequest body, CancellationToken ct) =>
        (await _mediator.Send(new UpdateTaxCommand(id, Request.IfMatch(), body.Name, body.IsActive), ct))
            .ToVersionedResult(HttpContext, t => t.Version);

    [HttpGet("unidades")]
    [ProducesResponseType<IReadOnlyList<UnitDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnits(CancellationToken ct) =>
        (await _mediator.Send(new GetUnitsQuery(), ct)).ToActionResult(HttpContext);
}

public sealed record UpdateCategoryRequest(string Name, int DisplayOrder, bool IsActive);
public sealed record UpdateTaxRequest(string Name, bool IsActive);
