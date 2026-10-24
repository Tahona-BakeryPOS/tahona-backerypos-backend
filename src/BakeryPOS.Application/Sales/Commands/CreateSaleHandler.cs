using BakeryPOS.Application.Common.Interfaces;
using BakeryPOS.Domain.Entities;
using BakeryPOS.Domain.Enums;
using BakeryPOS.SharedKernel.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BakeryPOS.Application.Sales.Commands;

public sealed class CreateSaleHandler : IRequestHandler<CreateSaleCommand, Result<SaleCreatedResponse>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CreateSaleHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<SaleCreatedResponse>> Handle(CreateSaleCommand request, CancellationToken ct)
    {
        // Idempotency check: if sale with this ID already exists
        var existing = await _db.Sales.FirstOrDefaultAsync(s => s.Id == request.SaleId, ct);
        if (existing is not null)
        {
            if (existing.Status == SaleStatus.Completed)
                return Result.Success(new SaleCreatedResponse(existing.Id, existing.DocumentNumber!, existing.Total));

            return Result.Failure<SaleCreatedResponse>(Error.VentaDuplicadaDistinta);
        }

        // Verify shift is open
        var shift = await _db.CashShifts
            .FirstOrDefaultAsync(s => s.Id == request.CashShiftId && s.OrganizationId == _currentUser.OrganizationId, ct);

        if (shift is null)
            return Result.Failure<SaleCreatedResponse>(Error.NotFound("Turno", request.CashShiftId));

        if (shift.Status == CashShiftStatus.Closed)
            return Result.Failure<SaleCreatedResponse>(Error.TurnoCerrado);

        // Get next document number
        var sequence = await _db.DocumentSequences
            .FirstOrDefaultAsync(s => s.BranchId == _currentUser.BranchId && s.IsActive, ct);

        var documentNumber = sequence?.GetNextNumber() ?? $"POS-{DateTime.UtcNow:yyyyMMddHHmmss}";

        // Create sale
        var sale = new Sale
        {
            Id = request.SaleId,
            OrganizationId = _currentUser.OrganizationId,
            BranchId = _currentUser.BranchId,
            CashRegisterId = shift.CashRegisterId,
            CashShiftId = request.CashShiftId,
            SoldByUserId = _currentUser.UserId,
            CustomerId = request.CustomerId,
            DocumentNumber = documentNumber,
            Notes = request.Notes,
            Origin = SyncOrigin.Server
        };

        // Add lines
        var lineNumber = 1;
        foreach (var lineDto in request.Lines)
        {
            var product = await _db.Products.FindAsync([lineDto.ProductId], ct);
            if (product is null)
                return Result.Failure<SaleCreatedResponse>(Error.NotFound("Producto", lineDto.ProductId));

            var line = new SaleLine
            {
                OrganizationId = _currentUser.OrganizationId,
                SaleId = sale.Id,
                ProductId = lineDto.ProductId,
                ProductName = product.Name,
                Quantity = lineDto.Quantity,
                UnitPrice = lineDto.UnitPrice,
                DiscountPercent = lineDto.DiscountPercent,
                TaxRate = lineDto.TaxRate,
                LineNumber = lineNumber++,
                CreatedBy = _currentUser.UserId.ToString()
            };
            line.Calculate();
            sale.Lines.Add(line);
        }

        // Add payments
        foreach (var paymentDto in request.Payments)
        {
            sale.Payments.Add(new Payment
            {
                OrganizationId = _currentUser.OrganizationId,
                SaleId = sale.Id,
                PaymentMethodId = paymentDto.PaymentMethodId,
                Amount = paymentDto.Amount,
                Reference = paymentDto.Reference,
                CreatedBy = _currentUser.UserId.ToString()
            });
        }

        // Calculate and validate
        sale.Complete();

        await _db.Sales.AddAsync(sale, ct);
        await _db.SaveChangesAsync(ct);

        return Result.Success(new SaleCreatedResponse(sale.Id, documentNumber, sale.Total));
    }
}
