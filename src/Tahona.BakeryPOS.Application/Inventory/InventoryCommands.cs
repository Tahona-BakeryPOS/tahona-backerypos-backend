using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Inventory;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Inventory;

public sealed record StockLocationDto(Guid Id, Guid BranchId, string Code, string Name, bool IsDefault, bool IsActive);

/// <param name="Direction"><c>in</c>, <c>out</c> o <c>both</c>.</param>
public sealed record AdjustmentReasonDto(Guid Id, string Code, string Name, string Direction, bool IsActive);

public sealed record StockMovementDto(Guid Id, DateTime OccurredAt, Guid LocationId, string LocationName, Guid ProductId, string Sku,
    string ProductName, string UnitCode, decimal Quantity, string Kind, string? ReasonName, string? UserName, string? AuthorizedByName,
    string? Note);

public sealed record InitialStockLine(Guid ProductId, decimal Quantity);

public sealed record CountLine(Guid ProductId, decimal Counted);

/// <param name="Difference">Counted − previous balance; 0 = it generated no adjustment.</param>
public sealed record CountResultLine(Guid ProductId, string ProductName, decimal Previous, decimal Counted, decimal Difference);

/// <summary>Locations of a branch (or of all in the scope).</summary>
public sealed record GetStockLocationsQuery(Guid? BranchId) : IRequest<Result<IReadOnlyList<StockLocationDto>>>;

public sealed record CreateStockLocationCommand(Guid BranchId, string Code, string Name) : IRequest<Result<StockLocationDto>>;

public sealed record GetAdjustmentReasonsQuery : IRequest<Result<IReadOnlyList<AdjustmentReasonDto>>>;

/// <summary>Initial stock (US-4.1): only once per product and location; afterwards, adjustments.</summary>
public sealed record RegisterInitialStockCommand(Guid LocationId, IReadOnlyList<InitialStockLine> Lines, string? Note)
    : IRequest<Result<IReadOnlyList<StockMovementDto>>>;

/// <summary>Adjustment with a reason (US-4.4). Signed quantity; authorized by whoever records it.</summary>
public sealed record RegisterAdjustmentCommand(Guid LocationId, Guid ProductId, decimal Quantity, Guid ReasonId, string? Note)
    : IRequest<Result<StockMovementDto>>;

/// <summary>Physical count: adjusts each product to the counted amount with the "Conteo físico" reason.</summary>
public sealed record RegisterPhysicalCountCommand(Guid LocationId, IReadOnlyList<CountLine> Lines, string? Note)
    : IRequest<Result<IReadOnlyList<CountResultLine>>>;

public sealed class CreateStockLocationValidator : AbstractValidator<CreateStockLocationCommand>
{
    public CreateStockLocationValidator()
    {
        RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("El código es obligatorio: letras, números, guion o guion bajo (máx. 20).");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
    }
}

public sealed class RegisterInitialStockValidator : AbstractValidator<RegisterInitialStockCommand>
{
    public RegisterInitialStockValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count <= 500).WithMessage("Entre 1 y 500 productos por registro.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(18, 4, true);
        });
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

public sealed class RegisterAdjustmentValidator : AbstractValidator<RegisterAdjustmentCommand>
{
    public RegisterAdjustmentValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ReasonId).NotEmpty().WithMessage("El motivo es obligatorio.");
        RuleFor(x => x.Quantity).NotEqual(0).PrecisionScale(18, 4, true);
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

public sealed class RegisterPhysicalCountValidator : AbstractValidator<RegisterPhysicalCountCommand>
{
    public RegisterPhysicalCountValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().Must(l => l.Count <= 500).WithMessage("Entre 1 y 500 productos por conteo.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductId).NotEmpty();
            l.RuleFor(x => x.Counted).GreaterThanOrEqualTo(0).PrecisionScale(18, 4, true);
        });
        RuleFor(x => x.Note).MaximumLength(200);
    }
}

public sealed class InventoryCommandsHandler :
    IRequestHandler<GetStockLocationsQuery, Result<IReadOnlyList<StockLocationDto>>>,
    IRequestHandler<CreateStockLocationCommand, Result<StockLocationDto>>,
    IRequestHandler<GetAdjustmentReasonsQuery, Result<IReadOnlyList<AdjustmentReasonDto>>>,
    IRequestHandler<RegisterInitialStockCommand, Result<IReadOnlyList<StockMovementDto>>>,
    IRequestHandler<RegisterAdjustmentCommand, Result<StockMovementDto>>,
    IRequestHandler<RegisterPhysicalCountCommand, Result<IReadOnlyList<CountResultLine>>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IBranchDirectory _branches;
    private readonly IProductDirectory _products;
    private readonly InventoryLedger _ledger;
    private readonly ISender _mediator;

    public InventoryCommandsHandler(IAppDbContext db, ITenantContext tenant, IBranchDirectory branches, IProductDirectory products,
        InventoryLedger ledger, ISender mediator)
    {
        _db = db;
        _tenant = tenant;
        _branches = branches;
        _products = products;
        _ledger = ledger;
        _mediator = mediator;
    }

    public async Task<Result<IReadOnlyList<StockLocationDto>>> Handle(GetStockLocationsQuery request, CancellationToken ct)
    {
        var locations = await _db.StockLocations.AsNoTracking()
            .Where(l => request.BranchId == null || l.BranchId == request.BranchId)
            .OrderBy(l => l.BranchId).ThenByDescending(l => l.IsDefault).ThenBy(l => l.Name)
            .ToListAsync(ct);
        IReadOnlyList<StockLocationDto> list = locations.Where(l => _tenant.CanAccessBranch(l.BranchId)).Select(ToDto).ToList();
        return Result.Success(list);
    }

    public async Task<Result<StockLocationDto>> Handle(CreateStockLocationCommand request, CancellationToken ct)
    {
        var branch = await _branches.FindAsync(request.BranchId, ct);
        if (branch is null)
            return Result.Failure<StockLocationDto>(Error.NotFound("Sucursal", request.BranchId));
        if (!_tenant.CanAccessBranch(branch.Id))
            return Result.Failure<StockLocationDto>(Error.SinAsignacionVigente);
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.StockLocations.AnyAsync(l => l.BranchId == branch.Id && l.Code == code, ct))
            return Result.Failure<StockLocationDto>(Error.CodigoDuplicado);

        // The first location of a branch (created before phase 4) is left as the default one.
        var isDefault = !await _db.StockLocations.AnyAsync(l => l.BranchId == branch.Id && l.IsDefault, ct);
        var location = StockLocation.Create(_tenant.RequiredCompanyId, branch.Id, code, request.Name, isDefault);
        _db.StockLocations.Add(location);
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(location));
    }

    public async Task<Result<IReadOnlyList<AdjustmentReasonDto>>> Handle(GetAdjustmentReasonsQuery request, CancellationToken ct)
    {
        var reasons = await _db.AdjustmentReasons.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        IReadOnlyList<AdjustmentReasonDto> list = reasons
            .Select(r => new AdjustmentReasonDto(r.Id, r.Code, r.Name, r.Direction.ToString().ToLowerInvariant(), r.IsActive)).ToList();
        return Result.Success(list);
    }

    public async Task<Result<IReadOnlyList<StockMovementDto>>> Handle(RegisterInitialStockCommand request, CancellationToken ct)
    {
        var location = await _ledger.FindLocationAsync(request.LocationId, ct);
        if (location.IsFailure)
            return Result.Failure<IReadOnlyList<StockMovementDto>>(location.Error);

        // The key of the initial stock is (location, product): already recorded = use an adjustment.
        var productIds = request.Lines.Select(l => l.ProductId).ToList();
        if (await _db.StockMovements.AnyAsync(m => m.Kind == StockMovementKind.Initial && m.SourceId == request.LocationId
                && productIds.Contains(m.SourceLineId), ct))
            return Result.Failure<IReadOnlyList<StockMovementDto>>(Error.InventarioInicialRegistrado);

        var posted = await _ledger.PostAsync(new StockPosting(request.LocationId, StockMovementKind.Initial, StockSources.Initial,
            request.LocationId, request.Lines.Select(l => new StockPostingLine(l.ProductId, l.Quantity, l.ProductId)).ToList(),
            AuthorizedBy: _tenant.UserId, Note: request.Note), ct);
        if (posted.IsFailure)
            return Result.Failure<IReadOnlyList<StockMovementDto>>(posted.Error);

        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetMovementsByIdQuery(posted.Value.Select(m => m.Id).ToList()), ct);
    }

    public async Task<Result<StockMovementDto>> Handle(RegisterAdjustmentCommand request, CancellationToken ct)
    {
        var location = await _ledger.FindLocationAsync(request.LocationId, ct);
        if (location.IsFailure)
            return Result.Failure<StockMovementDto>(location.Error);
        var reason = await _db.AdjustmentReasons.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.ReasonId, ct);
        if (reason is null)
            return Result.Failure<StockMovementDto>(Error.NotFound("Motivo", request.ReasonId));
        if (!reason.IsActive)
            return Result.Failure<StockMovementDto>(Error.Validation("El motivo está desactivado."));
        if (!reason.Allows(request.Quantity))
            return Result.Failure<StockMovementDto>(Error.Validation(
                $"El motivo \"{reason.Name}\" solo admite {(reason.Direction == AdjustmentDirection.In ? "entradas" : "salidas")}."));

        var adjustmentId = Guid.CreateVersion7();
        var posted = await _ledger.PostAsync(new StockPosting(request.LocationId,
            request.Quantity > 0 ? StockMovementKind.AdjustmentIn : StockMovementKind.AdjustmentOut,
            StockSources.Adjustment, adjustmentId, [new StockPostingLine(request.ProductId, request.Quantity, Guid.Empty)],
            reason.Id, _tenant.UserId, request.Note), ct);
        if (posted.IsFailure)
            return Result.Failure<StockMovementDto>(posted.Error);

        await _db.SaveChangesAsync(ct);
        var dto = await _mediator.Send(new GetMovementsByIdQuery([posted.Value[0].Id]), ct);
        return dto.IsSuccess ? Result.Success(dto.Value[0]) : Result.Failure<StockMovementDto>(dto.Error);
    }

    public async Task<Result<IReadOnlyList<CountResultLine>>> Handle(RegisterPhysicalCountCommand request, CancellationToken ct)
    {
        var location = await _ledger.FindLocationAsync(request.LocationId, ct);
        if (location.IsFailure)
            return Result.Failure<IReadOnlyList<CountResultLine>>(location.Error);
        if (request.Lines.GroupBy(l => l.ProductId).Any(g => g.Count() > 1))
            return Result.Failure<IReadOnlyList<CountResultLine>>(Error.Validation("Un producto aparece dos veces en el conteo."));
        var reason = await _db.AdjustmentReasons.AsNoTracking().FirstOrDefaultAsync(r => r.Code == AdjustmentReason.PhysicalCountCode, ct);
        if (reason is null)
            return Result.Failure<IReadOnlyList<CountResultLine>>(Error.Validation("Falta el motivo de ajuste \"Conteo físico\" (CONTEO)."));

        var productIds = request.Lines.Select(l => l.ProductId).ToList();
        var products = await _products.FindAsync(productIds, ct);
        foreach (var line in request.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
                return Result.Failure<IReadOnlyList<CountResultLine>>(Error.NotFound("Producto", line.ProductId));
            if (!product.TrackStock)
                return Result.Failure<IReadOnlyList<CountResultLine>>(Error.Validation($"{product.Name} no controla existencias: actívalo en el catálogo."));
            if (!product.AllowsDecimals && decimal.Truncate(line.Counted) != line.Counted)
                return Result.Failure<IReadOnlyList<CountResultLine>>(Error.Validation($"{product.Name} se cuenta por unidades: usa cantidades enteras."));
        }

        var previous = await _db.StockBalances.AsNoTracking()
            .Where(b => b.LocationId == request.LocationId && productIds.Contains(b.ProductId))
            .ToDictionaryAsync(b => b.ProductId, b => b.OnHand, ct);

        var diffs = request.Lines
            .Select(l => (Line: l, Previous: previous.GetValueOrDefault(l.ProductId), Difference: l.Counted - previous.GetValueOrDefault(l.ProductId)))
            .ToList();
        var countId = Guid.CreateVersion7();
        IReadOnlyList<StockMovement> moved = [];
        // Entries and exits are two movement types: they are recorded separately with the same source.
        foreach (var kind in new[] { StockMovementKind.AdjustmentIn, StockMovementKind.AdjustmentOut })
        {
            var lines = diffs.Where(d => kind == StockMovementKind.AdjustmentIn ? d.Difference > 0 : d.Difference < 0)
                .Select(d => new StockPostingLine(d.Line.ProductId, d.Difference, d.Line.ProductId)).ToList();
            if (lines.Count == 0) continue;
            var posted = await _ledger.PostAsync(new StockPosting(request.LocationId, kind, StockSources.PhysicalCount, countId, lines,
                reason.Id, _tenant.UserId, request.Note ?? "Conteo físico"), ct);
            if (posted.IsFailure)
                return Result.Failure<IReadOnlyList<CountResultLine>>(posted.Error);
            moved = [.. moved, .. posted.Value];
        }

        // With no differences there are no movements to save.
        if (moved.Count > 0)
            await _db.SaveChangesAsync(ct);

        IReadOnlyList<CountResultLine> result = diffs
            .Select(d => new CountResultLine(d.Line.ProductId, products[d.Line.ProductId].Name, d.Previous, d.Line.Counted, d.Difference))
            .ToList();
        return Result.Success(result);
    }

    private static StockLocationDto ToDto(StockLocation l) => new(l.Id, l.BranchId, l.Code, l.Name, l.IsDefault, l.IsActive);
}
