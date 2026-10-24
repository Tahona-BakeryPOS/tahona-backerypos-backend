using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Sales.Commands;

/// <summary>
/// CU-07: Void a sale.
/// </summary>
public sealed record VoidSaleCommand(Guid SaleId, string Reason) : IRequest<Result>;
