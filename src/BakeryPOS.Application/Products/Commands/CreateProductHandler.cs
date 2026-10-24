using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Products.Commands;

public sealed class CreateProductHandler : IRequestHandler<CreateProductCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateProductHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var categoryExists = await _db.ProductCategories.AnyAsync(c =>
            c.Id == request.CategoryId && c.OrganizationId == _currentUser.OrganizationId && c.DeletedAt == null, ct);

        if (!categoryExists)
            return Result.Failure<Guid>(Error.NotFound("Categoría", request.CategoryId));

        // SKU único por organización (CU-09)
        var duplicateSku = await _db.Products.AnyAsync(p =>
            p.OrganizationId == _currentUser.OrganizationId &&
            p.Sku == request.Sku &&
            p.DeletedAt == null, ct);

        if (duplicateSku)
            return Result.Failure<Guid>(Error.CodigoDuplicado);

        var product = new Product
        {
            OrganizationId = _currentUser.OrganizationId,
            CategoryId = request.CategoryId,
            TaxId = request.TaxId,
            UnitOfMeasureId = request.UnitOfMeasureId,
            Sku = request.Sku,
            Name = request.Name,
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            TrackInventory = request.TrackInventory,
            CreatedBy = _currentUser.UserId.ToString()
        };

        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            product.Barcodes.Add(new ProductBarcode
            {
                OrganizationId = _currentUser.OrganizationId,
                ProductId = product.Id,
                Barcode = request.Barcode,
                CreatedBy = _currentUser.UserId.ToString()
            });
        }

        await _db.Products.AddAsync(product, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success(product.Id);
    }
}
