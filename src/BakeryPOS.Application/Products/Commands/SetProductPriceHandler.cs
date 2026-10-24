using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Products.Commands;

public sealed class SetProductPriceHandler : IRequestHandler<SetProductPriceCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SetProductPriceHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(SetProductPriceCommand request, CancellationToken ct)
    {
        var productExists = await _db.Products.AnyAsync(p =>
            p.Id == request.ProductId && p.OrganizationId == _currentUser.OrganizationId && p.DeletedAt == null, ct);
        if (!productExists)
            return Result.Failure<Guid>(Error.NotFound("Producto", request.ProductId));

        var priceListExists = await _db.PriceLists.AnyAsync(pl =>
            pl.Id == request.PriceListId && pl.OrganizationId == _currentUser.OrganizationId && pl.DeletedAt == null, ct);
        if (!priceListExists)
            return Result.Failure<Guid>(Error.NotFound("Lista de precios", request.PriceListId));

        // Cierra el período vigente (soft delete) en vez de sobrescribirlo (CU-09).
        var currentPrice = await _db.Prices.FirstOrDefaultAsync(p =>
            p.ProductId == request.ProductId &&
            p.PriceListId == request.PriceListId &&
            p.DeletedAt == null, ct);

        if (currentPrice is not null)
        {
            currentPrice.DeletedAt = DateTime.UtcNow;
            currentPrice.DeletedBy = _currentUser.UserId.ToString();
        }

        var newPrice = new Price
        {
            OrganizationId = _currentUser.OrganizationId,
            ProductId = request.ProductId,
            PriceListId = request.PriceListId,
            UnitPrice = request.UnitPrice,
            CreatedBy = _currentUser.UserId.ToString()
        };

        await _db.Prices.AddAsync(newPrice, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success(newPrice.Id);
    }
}
