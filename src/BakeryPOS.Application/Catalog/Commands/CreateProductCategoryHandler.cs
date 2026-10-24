using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Catalog.Commands;

public sealed class CreateProductCategoryHandler : IRequestHandler<CreateProductCategoryCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateProductCategoryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateProductCategoryCommand request, CancellationToken ct)
    {
        var duplicate = await _db.ProductCategories.AnyAsync(c =>
            c.OrganizationId == _currentUser.OrganizationId &&
            c.Name == request.Name &&
            c.DeletedAt == null, ct);

        if (duplicate)
            return Result.Failure<Guid>(Error.CodigoDuplicado);

        var category = new ProductCategory
        {
            OrganizationId = _currentUser.OrganizationId,
            Name = request.Name,
            Description = request.Description,
            DisplayOrder = request.DisplayOrder,
            CreatedBy = _currentUser.UserId.ToString()
        };

        await _db.ProductCategories.AddAsync(category, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success(category.Id);
    }
}
