using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Auth.Commands;

public sealed class LoginByPinHandler : IRequestHandler<LoginByPinCommand, Result<LoginResponse>>
{
    private readonly IApplicationDbContext _db;
    private readonly IJwtTokenService _jwtService;

    public LoginByPinHandler(IApplicationDbContext db, IJwtTokenService jwtService)
    {
        _db = db;
        _jwtService = jwtService;
    }

    public async Task<Result<LoginResponse>> Handle(LoginByPinCommand request, CancellationToken ct)
    {
        // Find active users at the given branch
        var assignments = await _db.UserAssignments
            .Include(ua => ua.User)
            .Include(ua => ua.Role)
                .ThenInclude(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .Where(ua => ua.BranchId == request.BranchId && ua.IsActive && ua.User.IsActive)
            .ToListAsync(ct);

        // Try PIN against each user at this branch
        var match = assignments.FirstOrDefault(ua => ua.User.VerifyPin(request.Pin));

        if (match is null)
            return Result.Failure<LoginResponse>(Error.NoAutenticado);

        var user = match.User;
        var orgId = match.OrganizationId;

        // Collect all permissions from all roles at this branch
        var permissions = assignments
            .Where(ua => ua.UserId == user.Id)
            .SelectMany(ua => ua.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        var accessToken = _jwtService.GenerateToken(user, orgId, request.BranchId, permissions);
        var refreshToken = _jwtService.GenerateRefreshToken();

        return Result.Success(new LoginResponse(
            accessToken,
            refreshToken,
            user.Id,
            user.FullName,
            orgId,
            request.BranchId,
            permissions));
    }
}
