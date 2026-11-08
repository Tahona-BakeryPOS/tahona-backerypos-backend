using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Catalog;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Catalog.Taxes;

/// <param name="Kind"><c>iva</c>, <c>inc</c> o <c>none</c>.</param>
/// <param name="Version">ETag para If-Match en PUT.</param>
public sealed record TaxDto(Guid Id, string Code, string Name, string Kind, decimal Rate, string DianCode, bool IsActive, string Version)
{
    public static TaxDto From(Tax t) =>
        new(t.Id, t.Code, t.Name, t.Kind.ToString().ToLowerInvariant(), t.Rate, t.DianCode, t.IsActive, RowVersions.Encode(t.RowVersion));
}

public sealed record GetTaxesQuery : IRequest<Result<IReadOnlyList<TaxDto>>>;

/// <summary>Crea un impuesto. Código único en la empresa; la tarifa no se puede editar después.</summary>
public sealed record CreateTaxCommand(string Code, string Name, string Kind, decimal Rate) : IRequest<Result<TaxDto>>;

/// <summary>Cambia el nombre o (des)activa un impuesto. Exige la versión leída (If-Match).</summary>
public sealed record UpdateTaxCommand(Guid Id, string? ExpectedVersion, string Name, bool IsActive) : IRequest<Result<TaxDto>>;

public sealed class CreateTaxValidator : AbstractValidator<CreateTaxCommand>
{
    public CreateTaxValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("El código es obligatorio: letras, números, guion o guion bajo (máx. 20).");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Kind).Must(k => Enum.TryParse<TaxKind>(k, true, out _))
            .WithMessage("Tipo de impuesto: iva, inc o none.");
        RuleFor(x => x.Rate).InclusiveBetween(0, 100).PrecisionScale(7, 4, true);
    }
}

public sealed class UpdateTaxValidator : AbstractValidator<UpdateTaxCommand>
{
    public UpdateTaxValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
    }
}

public sealed class TaxesHandler :
    IRequestHandler<GetTaxesQuery, Result<IReadOnlyList<TaxDto>>>,
    IRequestHandler<CreateTaxCommand, Result<TaxDto>>,
    IRequestHandler<UpdateTaxCommand, Result<TaxDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public TaxesHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Result<IReadOnlyList<TaxDto>>> Handle(GetTaxesQuery request, CancellationToken ct)
    {
        var taxes = await _db.Taxes.AsNoTracking().OrderBy(t => t.Kind).ThenByDescending(t => t.Rate).ThenBy(t => t.Code).ToListAsync(ct);
        IReadOnlyList<TaxDto> list = taxes.Select(TaxDto.From).ToList();
        return Result.Success(list);
    }

    public async Task<Result<TaxDto>> Handle(CreateTaxCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Taxes.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(t => t.Code == code, ct))
            return Result.Failure<TaxDto>(Error.CodigoDuplicado);

        var tax = Tax.Create(_tenant.RequiredCompanyId, code, request.Name, Enum.Parse<TaxKind>(request.Kind, true), request.Rate);
        _db.Taxes.Add(tax);
        await _db.SaveChangesAsync(ct);
        return Result.Success(TaxDto.From(tax));
    }

    public async Task<Result<TaxDto>> Handle(UpdateTaxCommand request, CancellationToken ct)
    {
        var version = RowVersions.Decode(request.ExpectedVersion);
        if (version.IsFailure)
            return Result.Failure<TaxDto>(version.Error);

        var tax = await _db.Taxes.FirstOrDefaultAsync(t => t.Id == request.Id, ct);
        if (tax is null)
            return Result.Failure<TaxDto>(Error.NotFound("Impuesto", request.Id));

        _db.ExpectVersion(tax, version.Value);
        tax.Update(request.Name, request.IsActive);
        await _db.SaveChangesAsync(ct);
        return Result.Success(TaxDto.From(tax));
    }
}
