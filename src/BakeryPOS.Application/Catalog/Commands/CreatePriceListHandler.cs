using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Catalog.Commands;

public sealed class CreatePriceListHandler : IRequestHandler<CreatePriceListCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreatePriceListHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreatePriceListCommand request, CancellationToken ct)
    {
        var duplicate = await _db.PriceLists.AnyAsync(pl =>
            pl.OrganizationId == _currentUser.OrganizationId &&
            pl.Name == request.Name &&
            pl.DeletedAt == null, ct);

        if (duplicate)
            return Result.Failure<Guid>(Error.CodigoDuplicado);

        // Solo puede existir una lista por defecto; si se marca esta, se desmarcan las demás.
        if (request.IsDefault)
        {
            var currentDefaults = await _db.PriceLists
                .Where(pl => pl.OrganizationId == _currentUser.OrganizationId && pl.IsDefault && pl.DeletedAt == null)
                .ToListAsync(ct);

            foreach (var pl in currentDefaults)
                pl.IsDefault = false;
        }

        var priceList = new PriceList
        {
            OrganizationId = _currentUser.OrganizationId,
            Name = request.Name,
            IsDefault = request.IsDefault,
            CreatedBy = _currentUser.UserId.ToString()
        };

        await _db.PriceLists.AddAsync(priceList, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success(priceList.Id);
    }
}
