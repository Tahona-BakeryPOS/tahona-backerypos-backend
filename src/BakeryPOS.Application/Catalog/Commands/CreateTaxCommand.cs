using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Catalog.Commands;

/// <summary>
/// CU-09: Crear impuesto (IVA 19%, IVA 5%, Excluido, etc.).
/// </summary>
public sealed record CreateTaxCommand(string Name, decimal Rate) : IRequest<Result<Guid>>;
