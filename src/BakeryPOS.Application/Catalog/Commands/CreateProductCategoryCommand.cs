using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Catalog.Commands;

/// <summary>
/// CU-09: Crear categoría de producto.
/// </summary>
public sealed record CreateProductCategoryCommand(
    string Name,
    string? Description,
    int DisplayOrder) : IRequest<Result<Guid>>;
