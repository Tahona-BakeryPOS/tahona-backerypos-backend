using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Products.Commands;

/// <summary>
/// CU-09: Crear producto. El SKU es único por organización.
/// </summary>
public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string? Description,
    Guid CategoryId,
    Guid? TaxId,
    Guid? UnitOfMeasureId,
    string? ImageUrl,
    bool TrackInventory,
    string? Barcode) : IRequest<Result<Guid>>;
