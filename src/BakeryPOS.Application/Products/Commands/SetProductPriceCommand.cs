using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Products.Commands;

/// <summary>
/// CU-09: Cambiar el precio de un producto en una lista. Un cambio de precio
/// NUNCA sobrescribe el valor anterior: cierra el período vigente (soft delete)
/// y abre uno nuevo, preservando el histórico de precios.
/// </summary>
public sealed record SetProductPriceCommand(
    Guid ProductId,
    Guid PriceListId,
    decimal UnitPrice) : IRequest<Result<Guid>>;
