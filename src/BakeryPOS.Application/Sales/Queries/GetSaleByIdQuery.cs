using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Sales.Queries;

public sealed record GetSaleByIdQuery(Guid SaleId) : IRequest<Result<SaleDto>>;

public sealed record SaleDto(
    Guid Id,
    string? DocumentNumber,
    DateTime SaleDate,
    decimal Subtotal,
    decimal TaxTotal,
    decimal DiscountTotal,
    decimal Total,
    string Status,
    string SoldBy,
    string? CustomerName,
    IReadOnlyList<SaleLineDto> Lines,
    IReadOnlyList<PaymentDto> Payments);

public sealed record SaleLineDto(
    int LineNumber,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal TaxRate,
    decimal LineTotal);

public sealed record PaymentDto(
    string PaymentMethodName,
    decimal Amount,
    string? Reference);
