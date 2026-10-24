using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Enums;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Sales.Commands;

public sealed class VoidSaleHandler : IRequestHandler<VoidSaleCommand, Result>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public VoidSaleHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(VoidSaleCommand request, CancellationToken ct)
    {
        var sale = await _db.Sales
            .FirstOrDefaultAsync(s => s.Id == request.SaleId && s.OrganizationId == _currentUser.OrganizationId, ct);

        if (sale is null)
            return Result.Failure(Error.NotFound("Venta", request.SaleId));

        if (sale.Status == SaleStatus.Voided)
            return Result.Failure(Error.Validation("La venta ya fue anulada."));

        sale.Void(request.Reason);
        await _db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
