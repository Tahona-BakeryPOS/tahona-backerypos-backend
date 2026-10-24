using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Catalog.Commands;

public sealed class CreateTaxHandler : IRequestHandler<CreateTaxCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateTaxHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateTaxCommand request, CancellationToken ct)
    {
        var duplicate = await _db.Taxes.AnyAsync(t =>
            t.OrganizationId == _currentUser.OrganizationId &&
            t.Name == request.Name &&
            t.DeletedAt == null, ct);

        if (duplicate)
            return Result.Failure<Guid>(Error.CodigoDuplicado);

        var tax = new Tax
        {
            OrganizationId = _currentUser.OrganizationId,
            Name = request.Name,
            Rate = request.Rate,
            CreatedBy = _currentUser.UserId.ToString()
        };

        await _db.Taxes.AddAsync(tax, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success(tax.Id);
    }
}
