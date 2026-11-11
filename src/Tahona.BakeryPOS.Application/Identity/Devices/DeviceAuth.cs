using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Identity.Auth;
using Tahona.BakeryPOS.Domain.Identity;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Identity.Devices;

/// <summary>Pairs this device to the code's cash register. The token returned is only shown once.</summary>
public sealed record PairDeviceCommand(string Code, string DeviceName) : IRequest<Result<PairedDeviceDto>>;

public sealed record PairedDeviceDto(Guid DeviceId, string DeviceToken, Guid CompanyId, Guid BranchId, Guid CashRegisterId, string CashRegisterName);

/// <summary>Users who can sign in to this device's cash register (to tap the name, US-2.4).</summary>
public sealed record GetDeviceUsersQuery(string DeviceToken) : IRequest<Result<IReadOnlyList<DeviceUserDto>>>;

public sealed record DeviceUserDto(Guid UserId, string FullName);

/// <summary>Cash-register sign-in with a PIN from a paired device. 5 failed attempts lock for 5 minutes.</summary>
public sealed record LoginByPinCommand(string DeviceToken, Guid UserId, string Pin) : IRequest<Result<AuthSessionDto>>;

public sealed class PairDeviceValidator : AbstractValidator<PairDeviceCommand>
{
    public PairDeviceValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.DeviceName).NotEmpty().MaximumLength(80);
    }
}

public sealed class LoginByPinValidator : AbstractValidator<LoginByPinCommand>
{
    public LoginByPinValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Pin).NotEmpty().MaximumLength(12);
    }
}

/// <summary>
/// Flows authenticated by a device secret, with no company in session: they look up by hash without the company
/// filter and then work only with the device's company.
/// </summary>
public sealed class DeviceAuthHandler :
    IRequestHandler<PairDeviceCommand, Result<PairedDeviceDto>>,
    IRequestHandler<GetDeviceUsersQuery, Result<IReadOnlyList<DeviceUserDto>>>,
    IRequestHandler<LoginByPinCommand, Result<AuthSessionDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICashRegisterDirectory _registers;
    private readonly IPasswordHasher _hasher;
    private readonly SessionIssuer _sessions;
    private readonly TimeProvider _clock;

    public DeviceAuthHandler(IAppDbContext db, ICashRegisterDirectory registers, IPasswordHasher hasher, SessionIssuer sessions, TimeProvider clock)
    {
        _db = db;
        _registers = registers;
        _hasher = hasher;
        _sessions = sessions;
        _clock = clock;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<Result<PairedDeviceDto>> Handle(PairDeviceCommand request, CancellationToken ct)
    {
        var now = Now;
        var hash = SecretTokens.Hash(SecretTokens.NormalizePairingCode(request.Code));
        var pairing = await _db.DevicePairingCodes.IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(p => p.CodeHash == hash && p.ConsumedAt == null, ct);
        if (pairing is null || !pairing.IsUsable(now))
            return Result.Failure<PairedDeviceDto>(Error.CodigoVinculacionInvalido);

        var register = await _registers.FindForDeviceAsync(pairing.CashRegisterId, ct);
        if (register is null || !register.IsActive)
            return Result.Failure<PairedDeviceDto>(Error.CodigoVinculacionInvalido);

        var token = SecretTokens.NewToken();
        var device = Device.Create(pairing.CompanyId, register.Id, request.DeviceName, SecretTokens.Hash(token));
        _db.Devices.Add(device);
        pairing.Consume(device.Id, now);
        await _db.SaveChangesAsync(ct);

        return Result.Success(new PairedDeviceDto(device.Id, token, device.CompanyId, register.BranchId, register.Id, register.Name));
    }

    public async Task<Result<IReadOnlyList<DeviceUserDto>>> Handle(GetDeviceUsersQuery request, CancellationToken ct)
    {
        var resolved = await ResolveDeviceAsync(request.DeviceToken, ct);
        if (resolved.IsFailure)
            return Result.Failure<IReadOnlyList<DeviceUserDto>>(resolved.Error);
        var (device, register) = resolved.Value;

        var now = Now;
        var candidates = await _db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.CompanyId == device.CompanyId && m.IsActive && m.PinHash != null)
            .Join(_db.Users.Where(u => u.IsActive), m => m.UserId, u => u.Id, (m, u) => new { MembershipId = m.Id, u.Id, u.FullName })
            .ToListAsync(ct);
        var membershipIds = candidates.Select(c => c.MembershipId).ToList();
        var assignments = await _db.RoleAssignments.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(a => membershipIds.Contains(a.MembershipId))
            .ToListAsync(ct);
        var allowed = assignments
            .Where(a => a.IsValidAt(now) && a.Covers(register.BranchId))
            .Select(a => a.MembershipId)
            .ToHashSet();

        IReadOnlyList<DeviceUserDto> users = candidates
            .Where(c => allowed.Contains(c.MembershipId))
            .OrderBy(c => c.FullName)
            .Select(c => new DeviceUserDto(c.Id, c.FullName))
            .ToList();
        return Result.Success(users);
    }

    public async Task<Result<AuthSessionDto>> Handle(LoginByPinCommand request, CancellationToken ct)
    {
        var resolved = await ResolveDeviceAsync(request.DeviceToken, ct);
        if (resolved.IsFailure)
            return Result.Failure<AuthSessionDto>(resolved.Error);
        var (device, register) = resolved.Value;

        var now = Now;
        var membership = await _db.Memberships.IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(m => m.CompanyId == device.CompanyId && m.UserId == request.UserId && m.IsActive, ct);
        var user = membership is null ? null : await _db.Users.FirstOrDefaultAsync(u => u.Id == membership.UserId && u.IsActive, ct);
        if (membership?.PinHash is null || user is null)
        {
            _hasher.VerifyDummy(request.Pin);
            return Result.Failure<AuthSessionDto>(Error.NoAutenticado);
        }

        if (membership.IsPinLocked(now))
            return Result.Failure<AuthSessionDto>(Error.UsuarioBloqueado(membership.PinLockedUntil!.Value));

        if (!_hasher.Verify(request.Pin, membership.PinHash))
        {
            // The membership change is left in the audit trail: this is how the lockout gets recorded (US-2.4).
            var locked = membership.RegisterFailedPin(now);
            await _db.SaveChangesAsync(ct);
            return Result.Failure<AuthSessionDto>(locked ? Error.UsuarioBloqueado(membership.PinLockedUntil!.Value) : Error.NoAutenticado);
        }

        var grant = await _sessions.GrantOfAsync(membership.Id, register.BranchId, ct);
        if (!grant.HasAnyAssignment)
            return Result.Failure<AuthSessionDto>(Error.SinAsignacionVigente);

        membership.RegisterSuccessfulPin();
        device.Touch(now);
        await _db.SaveChangesAsync(ct);
        return Result.Success(_sessions.IssueCashier(user, membership, grant, register.BranchId, device.Id, register.Id));
    }

    private async Task<Result<(Device Device, CashRegisterSummary Register)>> ResolveDeviceAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure<(Device, CashRegisterSummary)>(Error.DispositivoNoVinculado);

        var hash = SecretTokens.Hash(token);
        var device = await _db.Devices.IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(d => d.TokenHash == hash && d.IsActive, ct);
        var register = device is null ? null : await _registers.FindForDeviceAsync(device.CashRegisterId, ct);
        if (device is null || register is null || !register.IsActive)
            return Result.Failure<(Device, CashRegisterSummary)>(Error.DispositivoNoVinculado);
        return Result.Success((device, register));
    }
}
