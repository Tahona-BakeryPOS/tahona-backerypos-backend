using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Identity;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity.Auth;

/// <summary>
/// Email + password. With a single company it signs in directly; with several (D-08) or as a platform
/// administrator it receives a session with no company and chooses with <see cref="SelectCompanyCommand"/>.
/// </summary>
public sealed record LoginCommand(string Email, string Password) : IRequest<Result<AuthSessionDto>>;

/// <summary>Issues tokens for one of the user's companies. Also used to switch company.</summary>
/// <param name="RefreshToken">Refresh token of the previous session, which is revoked.</param>
public sealed record SelectCompanyCommand(Guid CompanyId, string? RefreshToken) : IRequest<Result<AuthSessionDto>>;

/// <summary>Rotates the refresh token. Reusing one that was already rotated revokes all the user's sessions.</summary>
public sealed record RefreshSessionCommand(string RefreshToken) : IRequest<Result<AuthSessionDto>>;

public sealed record LogoutCommand(string RefreshToken) : IRequest<Result>;

/// <summary>Changes the password (mandatory after the one-time one) and closes the other sessions.</summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Result<AuthSessionDto>>;

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public const int MinLength = 10;

    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(MinLength).MaximumLength(128)
            .Must(p => p.Any(char.IsLetter) && p.Any(char.IsDigit))
            .WithMessage($"La contraseña debe tener al menos {MinLength} caracteres, con letras y números.")
            .NotEqual(x => x.CurrentPassword).WithMessage("La nueva contraseña debe ser distinta de la actual.");
    }
}

public sealed class BackofficeAuthHandler :
    IRequestHandler<LoginCommand, Result<AuthSessionDto>>,
    IRequestHandler<SelectCompanyCommand, Result<AuthSessionDto>>,
    IRequestHandler<RefreshSessionCommand, Result<AuthSessionDto>>,
    IRequestHandler<LogoutCommand, Result>,
    IRequestHandler<ChangePasswordCommand, Result<AuthSessionDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IPasswordHasher _hasher;
    private readonly SessionIssuer _sessions;
    private readonly TimeProvider _clock;

    public BackofficeAuthHandler(IAppDbContext db, ITenantContext tenant, IPasswordHasher hasher, SessionIssuer sessions, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _hasher = hasher;
        _sessions = sessions;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<Result<AuthSessionDto>> Handle(LoginCommand request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !user.IsActive || !user.CanUseBackoffice)
        {
            _hasher.VerifyDummy(request.Password);
            return Result.Failure<AuthSessionDto>(Error.Unauthenticated);
        }

        var check = await VerifyPasswordAsync(user, request.Password, ct);
        if (check.IsFailure)
            return Result.Failure<AuthSessionDto>(check.Error);

        var companies = await _sessions.CompaniesOfAsync(user.Id, ct);
        Membership? membership = null;
        if (companies.Count == 1 && !user.IsPlatformAdmin)
            membership = await _sessions.ActiveMembershipAsync(user.Id, companies[0].Id, ct);
        if (companies.Count == 0 && !user.IsPlatformAdmin)
        {
            await _db.SaveChangesAsync(ct);
            return Result.Failure<AuthSessionDto>(Error.Unauthenticated with { Message = "El usuario no tiene acceso a ninguna empresa activa." });
        }

        var session = await _sessions.IssueBackofficeAsync(user, membership, ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success(session);
    }

    public async Task<Result<AuthSessionDto>> Handle(SelectCompanyCommand request, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        if (user is null)
            return Result.Failure<AuthSessionDto>(Error.Unauthenticated);
        if (user.MustChangePassword)
            return Result.Failure<AuthSessionDto>(Error.PasswordChangeRequired);

        var membership = await _sessions.ActiveMembershipAsync(user.Id, request.CompanyId, ct);
        if (membership is null)
            return Result.Failure<AuthSessionDto>(Error.NotFound("Empresa", request.CompanyId));

        var previous = await FindRefreshTokenAsync(request.RefreshToken, ct);
        var session = await _sessions.IssueBackofficeAsync(user, membership, ct,
            previous is not null && previous.UserId == user.Id ? previous : null);
        await _db.SaveChangesAsync(ct);
        return Result.Success(session);
    }

    public async Task<Result<AuthSessionDto>> Handle(RefreshSessionCommand request, CancellationToken ct)
    {
        var now = Now;
        var token = await FindRefreshTokenAsync(request.RefreshToken, ct);
        if (token is null)
            return Result.Failure<AuthSessionDto>(Error.Unauthenticated);

        if (!token.IsActive(now))
        {
            // A token that was already rotated and shows up again was copied: all sessions are closed.
            if (token.ReplacedById is not null)
            {
                await _sessions.RevokeAllAsync(token.UserId, null, ct);
                await _db.SaveChangesAsync(ct);
            }
            return Result.Failure<AuthSessionDto>(Error.Unauthenticated);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, ct);
        Membership? membership = null;
        if (token.CompanyId is { } companyId && user is not null)
            membership = await _sessions.ActiveMembershipAsync(user.Id, companyId, ct);

        var invalid = user is null || !user.IsActive || !user.CanUseBackoffice || user.IsLockedOut(now)
            || (token.CompanyId is not null && membership is null);
        if (invalid)
        {
            token.Revoke(now);
            await _db.SaveChangesAsync(ct);
            return Result.Failure<AuthSessionDto>(Error.Unauthenticated);
        }

        var session = await _sessions.IssueBackofficeAsync(user!, membership, ct, token);
        await _db.SaveChangesAsync(ct);
        return Result.Success(session);
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken ct)
    {
        var token = await FindRefreshTokenAsync(request.RefreshToken, ct);
        if (token is not null && token.UserId == _tenant.UserId)
        {
            token.Revoke(Now);
            await _db.SaveChangesAsync(ct);
        }
        return Result.Success();
    }

    public async Task<Result<AuthSessionDto>> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        if (user is null || !user.CanUseBackoffice)
            return Result.Failure<AuthSessionDto>(Error.Unauthenticated);

        var check = await VerifyPasswordAsync(user, request.CurrentPassword, ct);
        if (check.IsFailure)
            return Result.Failure<AuthSessionDto>(check.Error.Code == Error.Unauthenticated.Code ? Error.IncorrectPassword : check.Error);

        user.ChangePassword(_hasher.Hash(request.NewPassword));
        await _sessions.RevokeAllAsync(user.Id, null, ct);

        var companies = await _sessions.CompaniesOfAsync(user.Id, ct);
        var membership = companies.Count == 1 && !user.IsPlatformAdmin
            ? await _sessions.ActiveMembershipAsync(user.Id, companies[0].Id, ct)
            : null;
        var session = await _sessions.IssueBackofficeAsync(user, membership, ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success(session);
    }

    /// <summary>Verifies the password applying the attempt lockout; stores the failed attempt.</summary>
    private async Task<Result> VerifyPasswordAsync(User user, string password, CancellationToken ct)
    {
        var now = Now;
        if (user.IsLockedOut(now))
            return Result.Failure(Error.UserLocked(user.LockedUntil!.Value));

        if (!_hasher.Verify(password, user.PasswordHash!))
        {
            var locked = user.RegisterFailedLogin(now);
            await _db.SaveChangesAsync(ct);
            return Result.Failure(locked ? Error.UserLocked(user.LockedUntil!.Value) : Error.Unauthenticated);
        }

        user.RegisterSuccessfulLogin();
        return Result.Success();
    }

    private async Task<User?> CurrentUserAsync(CancellationToken ct)
    {
        if (_tenant.UserId is not { } userId) return null;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is { IsActive: true } && !user.IsLockedOut(Now) ? user : null;
    }

    private async Task<RefreshToken?> FindRefreshTokenAsync(string? value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var hash = SecretTokens.Hash(value);
        return await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
    }
}
