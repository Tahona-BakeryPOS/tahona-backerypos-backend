using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Organizations.Commands;

/// <summary>
/// CU-10: Crear usuario (cajero/propietario) con PIN inicial.
/// </summary>
public sealed record CreateUserCommand(
    string FullName,
    string? Email,
    string? Phone,
    string Pin) : IRequest<Result<Guid>>;
