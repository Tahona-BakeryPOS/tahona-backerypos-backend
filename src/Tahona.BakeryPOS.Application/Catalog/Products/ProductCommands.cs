using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Catalog;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Catalog.Products;

/// <summary>
/// Creates a product (US-3.1). With <paramref name="BasePrice"/> it opens its first base price in the same transaction,
/// valid from now and with tax included (D-07).
/// </summary>
/// <param name="NegativeStockPolicy">D-06: <c>allow</c> (default) or <c>block</c>.</param>
public sealed record CreateProductCommand(string Sku, string Name, Guid CategoryId, Guid UnitId, Guid TaxId, string? Barcode,
    bool TrackStock, decimal? BasePrice, string? NegativeStockPolicy = null) : IRequest<Result<ProductDto>>;

/// <summary>Edits a product (the SKU does not change). Requires the version read (If-Match).</summary>
public sealed record UpdateProductCommand(Guid Id, string? ExpectedVersion, string Name, Guid CategoryId, Guid UnitId, Guid TaxId,
    string? Barcode, bool TrackStock, string? NegativeStockPolicy = null) : IRequest<Result<ProductDto>>;

/// <summary>Deactivates or reactivates a product: it is never deleted, sales reference it.</summary>
public sealed record SetProductActiveCommand(Guid Id, bool IsActive) : IRequest<Result<ProductDto>>;

/// <summary>
/// Opens a price validity period (US-3.2). <paramref name="BranchId"/> null = base price (permission
/// <c>catalog.manage</c>); with a branch, <c>prices.branch</c> and scope over it are enough.
/// <paramref name="ValidFrom"/> null = from now; past dates are not allowed.
/// </summary>
public sealed record SetProductPriceCommand(Guid ProductId, Guid? BranchId, decimal Amount, bool IncludesTax = true, DateTime? ValidFrom = null)
    : IRequest<Result<ProductPriceDto>>;

/// <summary>Marks a product as (un)available at a branch (US-3.3).</summary>
public sealed record SetProductAvailabilityCommand(Guid BranchId, Guid ProductId, bool IsAvailable) : IRequest<Result>;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("El código es obligatorio: letras, números, punto, guion o guion bajo (máx. 50).");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.TaxId).NotEmpty();
        RuleFor(x => x.Barcode).MaximumLength(50);
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true).When(x => x.BasePrice is not null);
        RuleFor(x => x.NegativeStockPolicy).Must(ProductRules.IsPolicy).WithMessage(ProductRules.PolicyMessage);
    }
}

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.TaxId).NotEmpty();
        RuleFor(x => x.Barcode).MaximumLength(50);
        RuleFor(x => x.NegativeStockPolicy).Must(ProductRules.IsPolicy).WithMessage(ProductRules.PolicyMessage);
    }
}

internal static class ProductRules
{
    public const string PolicyMessage = "Política de existencias: allow (permitir negativo) o block (bloquear).";

    public static bool IsPolicy(string? value) => value is null || Enum.TryParse<NegativeStockPolicy>(value, true, out _);

    public static NegativeStockPolicy Policy(string? value) =>
        value is null ? NegativeStockPolicy.Allow : Enum.Parse<NegativeStockPolicy>(value, true);
}

public sealed class SetProductPriceValidator : AbstractValidator<SetProductPriceCommand>
{
    public SetProductPriceValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
    }
}

public sealed class ProductCommandsHandler :
    IRequestHandler<CreateProductCommand, Result<ProductDto>>,
    IRequestHandler<UpdateProductCommand, Result<ProductDto>>,
    IRequestHandler<SetProductActiveCommand, Result<ProductDto>>,
    IRequestHandler<SetProductPriceCommand, Result<ProductPriceDto>>,
    IRequestHandler<SetProductAvailabilityCommand, Result>
{
    private static readonly Error BarrasDuplicado = Error.CodigoDuplicado with { Message = "Otro producto ya tiene ese código de barras." };

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IBranchDirectory _branches;
    private readonly ISender _mediator;
    private readonly TimeProvider _clock;

    public ProductCommandsHandler(IAppDbContext db, ITenantContext tenant, IBranchDirectory branches, ISender mediator, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _branches = branches;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<Result<ProductDto>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var sku = Product.NormalizeSku(request.Sku);
        if (await _db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(p => p.Sku == sku, ct))
            return Result.Failure<ProductDto>(Error.CodigoDuplicado);
        var check = await CheckReferencesAsync(request.CategoryId, request.UnitId, request.TaxId, Product.NormalizeBarcode(request.Barcode), null, ct);
        if (check.IsFailure)
            return Result.Failure<ProductDto>(check.Error);

        var companyId = _tenant.RequiredCompanyId;
        var product = Product.Create(companyId, sku, request.Name, request.CategoryId, request.UnitId, request.TaxId,
            request.Barcode, request.TrackStock, ProductRules.Policy(request.NegativeStockPolicy));
        _db.Products.Add(product);
        if (request.BasePrice is { } amount)
            _db.ProductPrices.Add(ProductPrice.Open(companyId, product.Id, null, amount, includesTax: true, Now, []));
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetProductByIdQuery(product.Id, null), ct);
    }

    public async Task<Result<ProductDto>> Handle(UpdateProductCommand request, CancellationToken ct)
    {
        var version = RowVersions.Decode(request.ExpectedVersion);
        if (version.IsFailure)
            return Result.Failure<ProductDto>(version.Error);

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.Id, ct);
        if (product is null)
            return Result.Failure<ProductDto>(Error.NotFound("Producto", request.Id));
        var check = await CheckReferencesAsync(request.CategoryId, request.UnitId, request.TaxId, Product.NormalizeBarcode(request.Barcode), product, ct);
        if (check.IsFailure)
            return Result.Failure<ProductDto>(check.Error);

        _db.ExpectVersion(product, version.Value);
        // Without a policy in the body the current one is kept.
        var policy = request.NegativeStockPolicy is null ? product.NegativeStockPolicy : ProductRules.Policy(request.NegativeStockPolicy);
        product.Update(request.Name, request.CategoryId, request.UnitId, request.TaxId, request.Barcode, request.TrackStock, policy);
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetProductByIdQuery(product.Id, null), ct);
    }

    public async Task<Result<ProductDto>> Handle(SetProductActiveCommand request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.Id, ct);
        if (product is null)
            return Result.Failure<ProductDto>(Error.NotFound("Producto", request.Id));

        if (request.IsActive) product.Activate();
        else product.Deactivate();
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetProductByIdQuery(product.Id, null), ct);
    }

    public async Task<Result<ProductPriceDto>> Handle(SetProductPriceCommand request, CancellationToken ct)
    {
        var allowed = await CheckPriceScopeAsync(request.BranchId, ct);
        if (allowed.IsFailure)
            return Result.Failure<ProductPriceDto>(allowed.Error);
        if (!await _db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
            return Result.Failure<ProductPriceDto>(Error.NotFound("Producto", request.ProductId));

        var now = Now;
        var validFrom = request.ValidFrom?.ToUniversalTime() ?? now;
        // A past validity period would rewrite the price of sales already made. One minute of margin for clocks.
        if (validFrom < now.AddMinutes(-1))
            return Result.Failure<ProductPriceDto>(Error.Validation("La vigencia no puede empezar en el pasado."));
        if (validFrom < now) validFrom = now;

        var history = await _db.ProductPrices
            .Where(p => p.ProductId == request.ProductId && p.BranchId == request.BranchId)
            .ToListAsync(ct);
        var price = ProductPrice.Open(_tenant.RequiredCompanyId, request.ProductId, request.BranchId, request.Amount,
            request.IncludesTax, validFrom, history);
        _db.ProductPrices.Add(price);
        await _db.SaveChangesAsync(ct);
        return Result.Success(ProductQueriesHandler.ToDto(price, now));
    }

    public async Task<Result> Handle(SetProductAvailabilityCommand request, CancellationToken ct)
    {
        var scope = await CheckBranchScopeAsync(request.BranchId, ct);
        if (scope.IsFailure)
            return scope;
        if (!await _db.Products.AnyAsync(p => p.Id == request.ProductId, ct))
            return Result.Failure(Error.NotFound("Producto", request.ProductId));

        var row = await _db.BranchProducts.FirstOrDefaultAsync(b => b.BranchId == request.BranchId && b.ProductId == request.ProductId, ct);
        if (row is null)
            _db.BranchProducts.Add(BranchProduct.Create(_tenant.RequiredCompanyId, request.BranchId, request.ProductId, request.IsAvailable));
        else
            row.SetAvailable(request.IsAvailable);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Base price: catalog.manage. Branch price: catalog.manage or prices.branch + scope.</summary>
    private async Task<Result> CheckPriceScopeAsync(Guid? branchId, CancellationToken ct)
    {
        if (branchId is null)
            return _tenant.HasPermission(Permissions.CatalogManage) ? Result.Success() : Result.Failure(Error.SinPermiso);
        if (!_tenant.HasPermission(Permissions.CatalogManage) && !_tenant.HasPermission(Permissions.BranchPrices))
            return Result.Failure(Error.SinPermiso);
        return await CheckBranchScopeAsync(branchId.Value, ct);
    }

    private async Task<Result> CheckBranchScopeAsync(Guid branchId, CancellationToken ct)
    {
        if (await _branches.FindAsync(branchId, ct) is null)
            return Result.Failure(Error.NotFound("Sucursal", branchId));
        return _tenant.CanAccessBranch(branchId) ? Result.Success() : Result.Failure(Error.SinAsignacionVigente);
    }

    /// <summary>Category and tax must exist in the company and be active; the unit must exist.</summary>
    private async Task<Result> CheckReferencesAsync(Guid categoryId, Guid unitId, Guid taxId, string? barcode, Product? current, CancellationToken ct)
    {
        var category = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is null)
            return Result.Failure(Error.NotFound("Categoría", categoryId));
        if (!category.IsActive && category.Id != current?.CategoryId)
            return Result.Failure(Error.Validation("La categoría está desactivada."));

        var tax = await _db.Taxes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taxId, ct);
        if (tax is null)
            return Result.Failure(Error.NotFound("Impuesto", taxId));
        if (!tax.IsActive && tax.Id != current?.TaxId)
            return Result.Failure(Error.Validation("El impuesto está desactivado."));

        if (!await _db.Units.AnyAsync(u => u.Id == unitId, ct))
            return Result.Failure(Error.NotFound("Unidad", unitId));

        if (barcode is not null && await _db.Products.IgnoreQueryFilters([QueryFilters.SoftDelete])
                .AnyAsync(p => p.Barcode == barcode && p.Id != (current == null ? Guid.Empty : current.Id), ct))
            return Result.Failure(BarrasDuplicado);

        return Result.Success();
    }
}
