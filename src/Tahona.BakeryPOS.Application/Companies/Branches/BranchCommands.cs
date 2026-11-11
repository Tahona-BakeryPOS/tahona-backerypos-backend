using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Companies.Branches;

/// <summary>Edits a branch (the code does not change). Requires the version read (If-Match).</summary>
public sealed record UpdateBranchCommand(Guid Id, string? ExpectedVersion, string Name, string? Address, string? CityCode, string? Phone)
    : IRequest<Result<BranchDto>>;

/// <summary>Deactivates a branch: it is not deleted because it may have sales (US-1.3).</summary>
public sealed record DeactivateBranchCommand(Guid Id) : IRequest<Result<BranchDto>>;

public sealed class UpdateBranchValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120).WithMessage("El nombre es obligatorio (máx. 120).");
        RuleFor(x => x.Address).MaximumLength(200);
        RuleFor(x => x.CityCode).Matches("^[0-9]{5}$").When(x => x.CityCode is not null)
            .WithMessage("El código de ciudad es el código DANE de 5 dígitos.");
        RuleFor(x => x.Phone).MaximumLength(30);
    }
}

public sealed class CreateBranchHandler : IRequestHandler<CreateBranchCommand, Result<BranchDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IInventoryProvisioning _inventory;

    public CreateBranchHandler(IAppDbContext db, ITenantContext tenant, IInventoryProvisioning inventory)
    {
        _db = db;
        _tenant = tenant;
        _inventory = inventory;
    }

    public async Task<Result<BranchDto>> Handle(CreateBranchCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        // The company filter limits the search to the company's own branches.
        if (await _db.Branches.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(b => b.Code == code, ct))
            return Result.Failure<BranchDto>(Error.DuplicateCode);

        var branch = Branch.Create(_tenant.RequiredCompanyId, code, request.Name, request.Address, request.CityCode, request.Phone);
        _db.Branches.Add(branch);
        _inventory.ProvisionBranch(branch.CompanyId, branch.Id);
        await _db.SaveChangesAsync(ct);

        return Result.Success(BranchDto.From(branch));
    }
}

public sealed class BranchMaintenanceHandler :
    IRequestHandler<UpdateBranchCommand, Result<BranchDto>>,
    IRequestHandler<DeactivateBranchCommand, Result<BranchDto>>
{
    private readonly IAppDbContext _db;

    public BranchMaintenanceHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<BranchDto>> Handle(UpdateBranchCommand request, CancellationToken ct)
    {
        var version = RowVersions.Decode(request.ExpectedVersion);
        if (version.IsFailure)
            return Result.Failure<BranchDto>(version.Error);

        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == request.Id, ct);
        if (branch is null)
            return Result.Failure<BranchDto>(Error.NotFound("Sucursal", request.Id));

        _db.ExpectVersion(branch, version.Value);
        branch.Update(request.Name, request.Address, request.CityCode, request.Phone);
        await _db.SaveChangesAsync(ct);
        return Result.Success(BranchDto.From(branch));
    }

    public async Task<Result<BranchDto>> Handle(DeactivateBranchCommand request, CancellationToken ct)
    {
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == request.Id, ct);
        if (branch is null)
            return Result.Failure<BranchDto>(Error.NotFound("Sucursal", request.Id));

        branch.Deactivate();
        await _db.SaveChangesAsync(ct);
        return Result.Success(BranchDto.From(branch));
    }
}

public sealed class GetBranchesHandler :
    IRequestHandler<GetBranchesQuery, Result<IReadOnlyList<BranchDto>>>,
    IRequestHandler<GetBranchByIdQuery, Result<BranchDto>>
{
    private readonly IAppDbContext _db;

    public GetBranchesHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Result<IReadOnlyList<BranchDto>>> Handle(GetBranchesQuery request, CancellationToken ct)
    {
        IReadOnlyList<BranchDto> branches = await _db.Branches
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(BranchDto.Projection)
            .ToListAsync(ct);
        return Result.Success(branches);
    }

    public async Task<Result<BranchDto>> Handle(GetBranchByIdQuery request, CancellationToken ct)
    {
        var branch = await _db.Branches
            .AsNoTracking()
            .Where(b => b.Id == request.Id)
            .Select(BranchDto.Projection)
            .FirstOrDefaultAsync(ct);
        return branch is null ? Result.Failure<BranchDto>(Error.NotFound("Sucursal", request.Id)) : Result.Success(branch);
    }
}
