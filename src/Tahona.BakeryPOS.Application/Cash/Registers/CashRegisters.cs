using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Cash;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Cash.Registers;

public sealed record CashRegisterDto(Guid Id, Guid BranchId, string Code, string Name, bool IsActive);

/// <summary>The company's cash registers, optionally of one branch.</summary>
public sealed record GetCashRegistersQuery(Guid? BranchId) : IRequest<Result<IReadOnlyList<CashRegisterDto>>>;

/// <summary>Creates a cash register at an active branch of the company. Code unique per branch.</summary>
public sealed record CreateCashRegisterCommand(Guid BranchId, string Code, string Name) : IRequest<Result<CashRegisterDto>>;

public sealed class CreateCashRegisterValidator : AbstractValidator<CreateCashRegisterCommand>
{
    public CreateCashRegisterValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("El código es obligatorio: letras, números, guion o guion bajo (máx. 20).");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
    }
}

public sealed class CashRegistersHandler :
    IRequestHandler<GetCashRegistersQuery, Result<IReadOnlyList<CashRegisterDto>>>,
    IRequestHandler<CreateCashRegisterCommand, Result<CashRegisterDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IBranchDirectory _branches;

    public CashRegistersHandler(IAppDbContext db, ITenantContext tenant, IBranchDirectory branches)
    {
        _db = db;
        _tenant = tenant;
        _branches = branches;
    }

    public async Task<Result<IReadOnlyList<CashRegisterDto>>> Handle(GetCashRegistersQuery request, CancellationToken ct)
    {
        IReadOnlyList<CashRegisterDto> registers = await _db.CashRegisters.AsNoTracking()
            .Where(r => request.BranchId == null || r.BranchId == request.BranchId)
            .OrderBy(r => r.Code)
            .Select(r => new CashRegisterDto(r.Id, r.BranchId, r.Code, r.Name, r.IsActive))
            .ToListAsync(ct);
        return Result.Success(registers);
    }

    public async Task<Result<CashRegisterDto>> Handle(CreateCashRegisterCommand request, CancellationToken ct)
    {
        // A branch of another company does not exist for this session: 404, the same as an invented id.
        var branch = await _branches.FindAsync(request.BranchId, ct);
        if (branch is null)
            return Result.Failure<CashRegisterDto>(Error.NotFound("Sucursal", request.BranchId));
        if (!branch.IsActive)
            return Result.Failure<CashRegisterDto>(Error.Validation("La sucursal está desactivada."));

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.CashRegisters.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(r => r.BranchId == branch.Id && r.Code == code, ct))
            return Result.Failure<CashRegisterDto>(Error.CodigoDuplicado);

        var register = CashRegister.Create(_tenant.RequiredCompanyId, branch.Id, code, request.Name);
        _db.CashRegisters.Add(register);
        await _db.SaveChangesAsync(ct);
        return Result.Success(new CashRegisterDto(register.Id, register.BranchId, register.Code, register.Name, register.IsActive));
    }
}

/// <summary>What other modules can query from Cash.</summary>
public sealed class CashRegisterDirectory : ICashRegisterDirectory
{
    private readonly IAppDbContext _db;

    public CashRegisterDirectory(IAppDbContext db)
    {
        _db = db;
    }

    public Task<CashRegisterSummary?> FindAsync(Guid cashRegisterId, CancellationToken ct) =>
        Find(_db.CashRegisters, cashRegisterId, ct);

    public Task<CashRegisterSummary?> FindForDeviceAsync(Guid cashRegisterId, CancellationToken ct) =>
        Find(_db.CashRegisters.IgnoreQueryFilters([QueryFilters.Tenant]), cashRegisterId, ct);

    private static Task<CashRegisterSummary?> Find(IQueryable<CashRegister> source, Guid id, CancellationToken ct) =>
        source.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new CashRegisterSummary(r.Id, r.CompanyId, r.BranchId, r.Code, r.Name, r.IsActive))
            .FirstOrDefaultAsync(ct);
}
