using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Products.Commands;

public sealed class DeactivateProductHandler : IRequestHandler<DeactivateProductCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public DeactivateProductHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(DeactivateProductCommand request, CancellationToken ct)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p =>
            p.Id == request.ProductId && p.OrganizationId == _currentUser.OrganizationId && p.DeletedAt == null, ct);

        if (product is null)
            return Result.Failure(Error.NotFound("Producto", request.ProductId));

        // Regla de negocio: nunca se elimina físicamente un producto con historial (CU-09).
        product.IsActive = false;
        product.ModifiedBy = _currentUser.UserId.ToString();
        product.ModifiedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
