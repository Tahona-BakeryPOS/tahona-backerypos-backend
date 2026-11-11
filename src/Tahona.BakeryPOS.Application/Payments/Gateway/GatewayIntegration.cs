using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Payments;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Payments.Gateway;

/// <summary>
/// Status of the Bold integration per environment. It never includes the keys: only whether they are configured.
/// </summary>
public sealed record GatewayAccountDto(string Provider, string Environment, bool HasIdentityKey, bool HasSecretKey, string? SellerEmail,
    bool IsActive, DateTime? UpdatedAt);

public sealed record GetGatewayAccountsQuery : IRequest<Result<IReadOnlyList<GatewayAccountDto>>>;

/// <summary>
/// Stores the keys of an environment (encrypted). A null key keeps the stored one. <paramref name="Activate"/>
/// makes this environment the company's active one (and deactivates the other).
/// </summary>
public sealed record SaveGatewayAccountCommand(string Environment, string? IdentityKey, string? SecretKey, string SellerEmail, bool Activate)
    : IRequest<Result<GatewayAccountDto>>;

/// <summary>Card terminals linked to the Bold account of the active environment (live query to Bold).</summary>
public sealed record GetBoldTerminalsQuery : IRequest<Result<IReadOnlyList<BoldTerminal>>>;

/// <summary>Methods enabled on the Bold account of the active environment (live query to Bold).</summary>
public sealed record GetBoldMethodsQuery : IRequest<Result<IReadOnlyList<BoldMethodStatus>>>;

public sealed record RegisterTerminalDto(Guid CashRegisterId, string TerminalModel, string TerminalSerial);

public sealed record GetRegisterTerminalsQuery : IRequest<Result<IReadOnlyList<RegisterTerminalDto>>>;

/// <summary>Assigns a register's Bold card terminal (model and serial as Bold returns them).</summary>
public sealed record SetRegisterTerminalCommand(Guid CashRegisterId, string TerminalModel, string TerminalSerial) : IRequest<Result<RegisterTerminalDto>>;

public sealed class SaveGatewayAccountValidator : AbstractValidator<SaveGatewayAccountCommand>
{
    public SaveGatewayAccountValidator()
    {
        RuleFor(x => x.Environment).Must(e => e is "sandbox" or "production").WithMessage("Ambiente: sandbox o production.");
        RuleFor(x => x.IdentityKey).MaximumLength(200);
        RuleFor(x => x.SecretKey).MaximumLength(200);
        RuleFor(x => x.SellerEmail).NotEmpty().EmailAddress().MaximumLength(200);
    }
}

public sealed class SetRegisterTerminalValidator : AbstractValidator<SetRegisterTerminalCommand>
{
    public SetRegisterTerminalValidator()
    {
        RuleFor(x => x.TerminalModel).NotEmpty().MaximumLength(30);
        RuleFor(x => x.TerminalSerial).NotEmpty().MaximumLength(50);
    }
}

public sealed class GatewayIntegrationHandler :
    IRequestHandler<GetGatewayAccountsQuery, Result<IReadOnlyList<GatewayAccountDto>>>,
    IRequestHandler<SaveGatewayAccountCommand, Result<GatewayAccountDto>>,
    IRequestHandler<GetBoldTerminalsQuery, Result<IReadOnlyList<BoldTerminal>>>,
    IRequestHandler<GetBoldMethodsQuery, Result<IReadOnlyList<BoldMethodStatus>>>,
    IRequestHandler<GetRegisterTerminalsQuery, Result<IReadOnlyList<RegisterTerminalDto>>>,
    IRequestHandler<SetRegisterTerminalCommand, Result<RegisterTerminalDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _secrets;
    private readonly IBoldGateway _bold;
    private readonly ICashRegisterDirectory _registers;

    public GatewayIntegrationHandler(IAppDbContext db, ITenantContext tenant, ISecretProtector secrets, IBoldGateway bold, ICashRegisterDirectory registers)
    {
        _db = db;
        _tenant = tenant;
        _secrets = secrets;
        _bold = bold;
        _registers = registers;
    }

    public async Task<Result<IReadOnlyList<GatewayAccountDto>>> Handle(GetGatewayAccountsQuery request, CancellationToken ct)
    {
        var accounts = await _db.GatewayAccounts.AsNoTracking().OrderBy(a => a.Environment).ToListAsync(ct);
        IReadOnlyList<GatewayAccountDto> list = accounts.Select(ToDto).ToList();
        return Result.Success(list);
    }

    public async Task<Result<GatewayAccountDto>> Handle(SaveGatewayAccountCommand request, CancellationToken ct)
    {
        var environment = request.Environment == "production" ? GatewayEnvironment.Production : GatewayEnvironment.Sandbox;
        var account = await _db.GatewayAccounts.FirstOrDefaultAsync(a => a.Environment == environment, ct);
        if (account is null)
        {
            if (string.IsNullOrWhiteSpace(request.IdentityKey))
                return Result.Failure<GatewayAccountDto>(Error.Validation("La llave de identidad es obligatoria la primera vez."));
            account = GatewayAccount.Create(_tenant.RequiredCompanyId, environment);
            _db.GatewayAccounts.Add(account);
        }

        var identity = string.IsNullOrWhiteSpace(request.IdentityKey) ? account.EncryptedIdentityKey : _secrets.Protect(request.IdentityKey.Trim());
        var secret = request.SecretKey is null ? account.EncryptedSecretKey
            : request.SecretKey.Trim().Length == 0 ? null : _secrets.Protect(request.SecretKey.Trim());
        account.SetCredentials(identity, secret, request.SellerEmail);

        if (request.Activate)
        {
            // A single active environment: the other is deactivated before activating this one (filtered unique index).
            foreach (var other in await _db.GatewayAccounts.Where(a => a.Id != account.Id && a.IsActive).ToListAsync(ct))
                other.Deactivate();
            await _db.SaveChangesAsync(ct);
            account.Activate();
        }
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(account));
    }

    public async Task<Result<IReadOnlyList<BoldTerminal>>> Handle(GetBoldTerminalsQuery request, CancellationToken ct)
    {
        var key = await ActiveIdentityKeyAsync(ct);
        return key is null
            ? Result.Failure<IReadOnlyList<BoldTerminal>>(Error.GatewayNotConfigured)
            : Result.Success(await _bold.ListTerminalsAsync(key, ct));
    }

    public async Task<Result<IReadOnlyList<BoldMethodStatus>>> Handle(GetBoldMethodsQuery request, CancellationToken ct)
    {
        var key = await ActiveIdentityKeyAsync(ct);
        return key is null
            ? Result.Failure<IReadOnlyList<BoldMethodStatus>>(Error.GatewayNotConfigured)
            : Result.Success(await _bold.ListMethodsAsync(key, ct));
    }

    public async Task<Result<IReadOnlyList<RegisterTerminalDto>>> Handle(GetRegisterTerminalsQuery request, CancellationToken ct)
    {
        IReadOnlyList<RegisterTerminalDto> list = await _db.RegisterTerminals.AsNoTracking()
            .Select(t => new RegisterTerminalDto(t.CashRegisterId, t.TerminalModel, t.TerminalSerial)).ToListAsync(ct);
        return Result.Success(list);
    }

    public async Task<Result<RegisterTerminalDto>> Handle(SetRegisterTerminalCommand request, CancellationToken ct)
    {
        if (await _registers.FindAsync(request.CashRegisterId, ct) is null)
            return Result.Failure<RegisterTerminalDto>(Error.NotFound("Caja", request.CashRegisterId));
        var terminal = await _db.RegisterTerminals.FirstOrDefaultAsync(t => t.CashRegisterId == request.CashRegisterId, ct);
        if (terminal is null)
            _db.RegisterTerminals.Add(terminal = RegisterTerminal.Create(_tenant.RequiredCompanyId, request.CashRegisterId, request.TerminalModel, request.TerminalSerial));
        else
            terminal.Set(request.TerminalModel, request.TerminalSerial);
        await _db.SaveChangesAsync(ct);
        return Result.Success(new RegisterTerminalDto(terminal.CashRegisterId, terminal.TerminalModel, terminal.TerminalSerial));
    }

    private async Task<string?> ActiveIdentityKeyAsync(CancellationToken ct)
    {
        var account = await _db.GatewayAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.IsActive, ct);
        return account is null ? null : _secrets.Unprotect(account.EncryptedIdentityKey);
    }

    private static GatewayAccountDto ToDto(GatewayAccount a) => new(a.Provider, a.Environment.ToString().ToLowerInvariant(),
        a.EncryptedIdentityKey.Length > 0, a.EncryptedSecretKey is not null, a.SellerEmail, a.IsActive, a.ModifiedAt ?? a.CreatedAt);
}
