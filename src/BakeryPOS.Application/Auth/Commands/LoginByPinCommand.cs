using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Auth.Commands;

/// <summary>
/// CU-01: Login por PIN del cajero.
/// </summary>
public sealed record LoginByPinCommand(
    string Pin,
    Guid BranchId,
    Guid? DeviceId) : IRequest<Result<LoginResponse>>;

public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    Guid UserId,
    string FullName,
    Guid OrganizationId,
    Guid BranchId,
    IReadOnlyList<string> Permissions);
