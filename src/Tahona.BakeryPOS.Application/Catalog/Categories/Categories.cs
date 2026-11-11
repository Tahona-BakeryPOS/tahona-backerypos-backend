using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Catalog;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Catalog.Categories;

/// <param name="Version">ETag for If-Match on PUT.</param>
public sealed record CategoryDto(Guid Id, string Name, int DisplayOrder, bool IsActive, int ProductCount, string Version);

public sealed record GetCategoriesQuery : IRequest<Result<IReadOnlyList<CategoryDto>>>;

/// <summary>Creates a category. Name unique within the company.</summary>
public sealed record CreateCategoryCommand(string Name, int DisplayOrder) : IRequest<Result<CategoryDto>>;

/// <summary>Renames, reorders or (de)activates a category. Requires the version read (If-Match).</summary>
public sealed record UpdateCategoryCommand(Guid Id, string? ExpectedVersion, string Name, int DisplayOrder, bool IsActive)
    : IRequest<Result<CategoryDto>>;

public sealed class CreateCategoryValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80).WithMessage("El nombre es obligatorio (máx. 80).");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateCategoryValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80).WithMessage("El nombre es obligatorio (máx. 80).");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CategoriesHandler :
    IRequestHandler<GetCategoriesQuery, Result<IReadOnlyList<CategoryDto>>>,
    IRequestHandler<CreateCategoryCommand, Result<CategoryDto>>,
    IRequestHandler<UpdateCategoryCommand, Result<CategoryDto>>
{
    private static readonly Error NombreDuplicado = Error.DuplicateCode with { Message = "Ya existe una categoría con ese nombre." };

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public CategoriesHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(GetCategoriesQuery request, CancellationToken ct)
    {
        var rows = await _db.Categories.AsNoTracking()
            .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.Name, c.DisplayOrder, c.IsActive, c.RowVersion,
                Products = _db.Products.Count(p => p.CategoryId == c.Id) })
            .ToListAsync(ct);
        IReadOnlyList<CategoryDto> list = rows
            .Select(c => new CategoryDto(c.Id, c.Name, c.DisplayOrder, c.IsActive, c.Products, RowVersions.Encode(c.RowVersion)))
            .ToList();
        return Result.Success(list);
    }

    public async Task<Result<CategoryDto>> Handle(CreateCategoryCommand request, CancellationToken ct)
    {
        var name = request.Name.Trim();
        if (await NameTakenAsync(name, null, ct))
            return Result.Failure<CategoryDto>(NombreDuplicado);

        var category = Category.Create(_tenant.RequiredCompanyId, name, request.DisplayOrder);
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(category, 0));
    }

    public async Task<Result<CategoryDto>> Handle(UpdateCategoryCommand request, CancellationToken ct)
    {
        var version = RowVersions.Decode(request.ExpectedVersion);
        if (version.IsFailure)
            return Result.Failure<CategoryDto>(version.Error);

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == request.Id, ct);
        if (category is null)
            return Result.Failure<CategoryDto>(Error.NotFound("Categoría", request.Id));
        var name = request.Name.Trim();
        if (await NameTakenAsync(name, category.Id, ct))
            return Result.Failure<CategoryDto>(NombreDuplicado);

        _db.ExpectVersion(category, version.Value);
        category.Update(name, request.DisplayOrder, request.IsActive);
        await _db.SaveChangesAsync(ct);
        var products = await _db.Products.CountAsync(p => p.CategoryId == category.Id, ct);
        return Result.Success(ToDto(category, products));
    }

    // PostgreSQL compares case-sensitively: "Panes" and "panes" would be two different categories.
    private Task<bool> NameTakenAsync(string name, Guid? except, CancellationToken ct) =>
        _db.Categories.AnyAsync(c => c.Name.ToLower() == name.ToLower() && c.Id != except, ct);

    private static CategoryDto ToDto(Category c, int products) =>
        new(c.Id, c.Name, c.DisplayOrder, c.IsActive, products, RowVersions.Encode(c.RowVersion));
}
