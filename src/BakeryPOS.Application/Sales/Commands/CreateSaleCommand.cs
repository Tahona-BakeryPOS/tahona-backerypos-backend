using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Sales.Commands;

/// <summary>
/// CU-05 / CU-06: Create a sale (online or synced from offline).
/// The SaleId is a UUID v7 generated on the device for idempotency.
/// </summary>
public sealed record CreateSaleCommand(
    Guid SaleId,
    Guid CashShiftId,
    Guid? CustomerId,
    string? Notes,
    IReadOnlyList<CreateSaleLineDto> Lines,
    IReadOnlyList<CreatePaymentDto> Payments) : IRequest<Result<SaleCreatedResponse>>;

public sealed record CreateSaleLineDto(
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal TaxRate);

public sealed record CreatePaymentDto(
    Guid PaymentMethodId,
    decimal Amount,
    string? Reference);

public sealed record SaleCreatedResponse(
    Guid SaleId,
    string DocumentNumber,
    decimal Total);
