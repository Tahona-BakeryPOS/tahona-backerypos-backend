using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Identity;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity.Auth;

/// <summary>
/// Correo + contraseña. Con una sola empresa entra directo; con varias (D-08) o siendo administrador
/// de plataforma recibe una sesión sin empresa y elige con <see cref="SelectCompanyCommand"/>.
/// </summary>
public sealed record LoginCommand(string Email, string Password) : IRequest<Result<AuthSessionDto>>;

/// <summary>Emite tokens para una empresa del usuario. Sirve también para cambiar de empresa.</summary>
/// <param name="RefreshToken">Refresh token de la sesión anterior, que queda revocado.</param>
public sealed record SelectCompanyCommand(Guid CompanyId, string? RefreshToken) : IRequest<Result<AuthSessionDto>>;

/// <summary>Rota el refresh token. Reusar uno ya rotado revoca todas las sesiones del usuario.</summary>
public sealed record RefreshSessionCommand(string RefreshToken) : IRequest<Result<AuthSessionDto>>;

public sealed record LogoutCommand(string RefreshToken) : IRequest<Result>;

/// <summary>Cambia la contraseña (obligatorio tras la de un solo uso) y cierra las demás sesiones.</summary>
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
            return Result.Failure<AuthSessionDto>(Error.NoAutenticado);
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
            return Result.Failure<AuthSessionDto>(Error.NoAutenticado with { Message = "El usuario no tiene acceso a ninguna empresa activa." });
        }

        var session = await _sessions.IssueBackofficeAsync(user, membership, ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success(session);
    }

    public async Task<Result<AuthSessionDto>> Handle(SelectCompanyCommand request, CancellationToken ct)
    {
        var user = await CurrentUserAsync(ct);
        if (user is null)
            return Result.Failure<AuthSessionDto>(Error.NoAutenticado);
        if (user.MustChangePassword)
            return Result.Failure<AuthSessionDto>(Error.CambioContrasenaRequerido);

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
            return Result.Failure<AuthSessionDto>(Error.NoAutenticado);

        if (!token.IsActive(now))
        {
            // Un token ya rotado que vuelve a aparecer fue copiado: se cierran todas las sesiones.
            if (token.ReplacedById is not null)
            {
                await _sessions.RevokeAllAsync(token.UserId, null, ct);
                await _db.SaveChangesAsync(ct);
            }
            return Result.Failure<AuthSessionDto>(Error.NoAutenticado);
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
            return Result.Failure<AuthSessionDto>(Error.NoAutenticado);
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
            return Result.Failure<AuthSessionDto>(Error.NoAutenticado);

        var check = await VerifyPasswordAsync(user, request.CurrentPassword, ct);
        if (check.IsFailure)
            return Result.Failure<AuthSessionDto>(check.Error.Code == Error.NoAutenticado.Code ? Error.ContrasenaIncorrecta : check.Error);

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

    /// <summary>Verifica la contraseña aplicando el bloqueo por intentos; guarda el intento fallido.</summary>
    private async Task<Result> VerifyPasswordAsync(User user, string password, CancellationToken ct)
    {
        var now = Now;
        if (user.IsLockedOut(now))
            return Result.Failure(Error.UsuarioBloqueado(user.LockedUntil!.Value));

        if (!_hasher.Verify(password, user.PasswordHash!))
        {
            var locked = user.RegisterFailedLogin(now);
            await _db.SaveChangesAsync(ct);
            return Result.Failure(locked ? Error.UsuarioBloqueado(user.LockedUntil!.Value) : Error.NoAutenticado);
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
