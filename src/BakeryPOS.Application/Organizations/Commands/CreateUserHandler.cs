using System.Text.Json;
using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.SharedKernel.Results;
using MediatR;

namespace BakeryPOS.Application.Organizations.Commands;

public sealed class CreateUserHandler : IRequestHandler<CreateUserCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateUserHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var user = new AppUser
        {
            OrganizationId = _currentUser.OrganizationId,
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            CreatedBy = _currentUser.UserId.ToString()
        };

        // El PIN se almacena como hash (BCrypt), nunca en texto plano.
        user.SetPin(request.Pin);

        await _db.Users.AddAsync(user, ct);

        await _db.AuditLogs.AddAsync(new AuditLog
        {
            OrganizationId = _currentUser.OrganizationId,
            UserId = _currentUser.UserId,
            EntityName = nameof(AppUser),
            EntityId = user.Id,
            Action = "CREATE",
            NewValues = JsonSerializer.Serialize(new { user.FullName, user.Email, user.Phone }),
            CreatedBy = _currentUser.UserId.ToString()
        }, ct);

        await _db.SaveChangesAsync(ct);

        return Result.Success(user.Id);
    }
}
