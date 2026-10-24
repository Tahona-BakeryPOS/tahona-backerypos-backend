using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Catalog.Commands;

/// <summary>
/// CU-09: Crear lista de precios (General, Mayorista, Empleados, etc.).
/// </summary>
public sealed record CreatePriceListCommand(string Name, bool IsDefault) : IRequest<Result<Guid>>;
