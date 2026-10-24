using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Products.Commands;

/// <summary>
/// CU-09: Un producto con historial de ventas se desactiva, nunca se elimina.
/// </summary>
public sealed record DeactivateProductCommand(Guid ProductId) : IRequest<Result>;
