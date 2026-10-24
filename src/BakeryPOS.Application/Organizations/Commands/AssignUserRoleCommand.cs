using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Organizations.Commands;

/// <summary>
/// CU-10: Asignar un rol a un usuario en una sucursal específica.
/// </summary>
public sealed record AssignUserRoleCommand(Guid UserId, Guid RoleId, Guid BranchId) : IRequest<Result<Guid>>;
