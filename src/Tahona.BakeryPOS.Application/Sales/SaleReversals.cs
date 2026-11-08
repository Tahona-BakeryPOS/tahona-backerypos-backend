using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Sales;
using Tahona.BakeryPOS.SharedKernel.Pagination;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Sales;

/// <summary>
/// D-11: anulación total de una venta confirmada mientras su turno sigue abierto. Lo envía quien tiene
/// <c>ventas.anular</c> con su propio token (en el POS: el superior digita su PIN). En una transacción: efectivo de
/// vuelta desde la caja de la venta, reverso de inventario, reembolso de los pagos y nota crédito si hay factura.
/// </summary>
public sealed record VoidSaleCommand(Guid SaleId, string Reason) : IRequest<Result<SaleDto>>;

/// <param name="Restock">El producto vuelve a la venta; false si está dañado o es perecedero (no genera movimiento).</param>
public sealed record SaleReturnLineInput(Guid SaleLineId, decimal Quantity, bool Restock = true);

/// <summary>
/// HU-9.2: devolución total o parcial. La autoriza en el momento quien tiene <c>devoluciones.aprobar</c> enviando su
/// propio token (PIN en el POS). El dinero sale en efectivo de la sesión abierta de la caja indicada (D-11), aunque la
/// venta se haya pagado con pasarela; lo marcado reingresa al inventario y, si la venta tiene factura, se emite nota crédito.
/// </summary>
public sealed record CreateSaleReturnCommand(Guid SaleId, Guid CashRegisterId, string Reason, IReadOnlyList<SaleReturnLineInput> Lines)
    : IRequest<Result<SaleDto>>;

public sealed record SaleReturnListItemDto(Guid Id, Guid SaleId, long? SaleNumber, Guid BranchId, DateTime OccurredAt, string Reason,
    string? ApprovedByName, decimal Total, int LineCount, string? CreditNoteStatus);

/// <summary>Devoluciones por fecha y sucursal, de la más reciente a la más antigua.</summary>
public sealed record GetSaleReturnsQuery(DateTime? From, DateTime? To, Guid? BranchId, int Page = 1, int PageSize = 50)
    : IRequest<Result<PagedResult<SaleReturnListItemDto>>>;

public sealed class VoidSaleValidator : AbstractValidator<VoidSaleCommand>
{
    public VoidSaleValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("La anulación exige motivo.").MaximumLength(300);
    }
}

public sealed class CreateSaleReturnValidator : AbstractValidator<CreateSaleReturnCommand>
{
    public CreateSaleReturnValidator()
    {
        RuleFor(x => x.CashRegisterId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().WithMessage("La devolución exige motivo.").MaximumLength(300);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Indica qué se devuelve.").Must(l => l.Count <= 200);
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.SaleLineId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(18, 2, true);
        });
    }
}

public sealed class GetSaleReturnsValidator : AbstractValidator<GetSaleReturnsQuery>
{
    public GetSaleReturnsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class SaleReversalsHandler :
    IRequestHandler<VoidSaleCommand, Result<SaleDto>>,
    IRequestHandler<CreateSaleReturnCommand, Result<SaleDto>>,
    IRequestHandler<GetSaleReturnsQuery, Result<PagedResult<SaleReturnListItemDto>>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICashSessionDirectory _sessions;
    private readonly IInventoryPosting _inventory;
    private readonly ICashDrawer _drawer;
    private readonly ISalePayments _payments;
    private readonly IFiscalDocumentIssuer _fiscal;
    private readonly IUserDirectory _users;
    private readonly ISender _mediator;
    private readonly TimeProvider _clock;

    public SaleReversalsHandler(IAppDbContext db, ITenantContext tenant, ICashSessionDirectory sessions, IInventoryPosting inventory, ICashDrawer drawer,
        ISalePayments payments, IFiscalDocumentIssuer fiscal, IUserDirectory users, ISender mediator, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _sessions = sessions;
        _inventory = inventory;
        _drawer = drawer;
        _payments = payments;
        _fiscal = fiscal;
        _users = users;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<Result<SaleDto>> Handle(VoidSaleCommand request, CancellationToken ct)
    {
        if (!_tenant.HasPermission(Permissions.VentasAnular))
            return Result.Failure<SaleDto>(Error.SinPermiso with { Message = "Anular ventas requiere el permiso ventas.anular." });
        var found = await FindAsync(request.SaleId, ct);
        if (found.IsFailure) return Result.Failure<SaleDto>(found.Error);
        var sale = found.Value;
        if (sale.Status == SaleStatus.Voided)
            return await _mediator.Send(new GetSaleQuery(sale.Id), ct);
        if (sale.Status != SaleStatus.Confirmed)
            return Result.Failure<SaleDto>(Error.Validation("Solo se anula una venta confirmada; una sin cobrar se cancela."));
        if (sale.RefundedTotal > 0)
            return Result.Failure<SaleDto>(Error.AnulacionNoPermitida with { Message = "La venta ya tiene devoluciones: registra otra devolución en vez de anularla." });
        var session = await _sessions.FindAsync(sale.CashSessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure<SaleDto>(Error.AnulacionNoPermitida with { Message = "El turno de esta venta ya se cerró: registra una devolución." });

        var by = _tenant.UserId!.Value;
        var cash = await _drawer.RecordRefundAsync(sale.CashSessionId, RefundSources.Void, sale.Id, sale.Total, by, ct);
        if (cash.IsFailure) return Result.Failure<SaleDto>(cash.Error);
        if (sale.InventoryStatus == SaleInventoryStatus.Applied)
        {
            var reversed = await _inventory.ReverseSaleAsync(sale.BranchId, sale.Id, sale.Lines.Select(l => (l.Id, l.ProductId, l.Quantity)).ToList(), ct);
            if (reversed.IsFailure) return Result.Failure<SaleDto>(reversed.Error);
        }
        var refunded = await _payments.RefundAsync(sale.Id, RefundSources.Void, sale.Id, sale.Total, ct);
        if (refunded.IsFailure) return Result.Failure<SaleDto>(refunded.Error);
        await _fiscal.EnqueueCreditNoteAsync(sale.Id, sale.Id, isVoid: true, lines: null, payments: null, sale.Total, ct);

        sale.Void(request.Reason, by, _clock.GetUtcNow().UtcDateTime);
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetSaleQuery(sale.Id), ct);
    }

    public async Task<Result<SaleDto>> Handle(CreateSaleReturnCommand request, CancellationToken ct)
    {
        if (!_tenant.HasPermission(Permissions.DevolucionesAprobar))
            return Result.Failure<SaleDto>(Error.SinPermiso with { Message = "Autorizar devoluciones requiere el permiso devoluciones.aprobar." });
        var found = await FindAsync(request.SaleId, ct);
        if (found.IsFailure) return Result.Failure<SaleDto>(found.Error);
        var sale = found.Value;
        if (sale.Status != SaleStatus.Confirmed)
            return Result.Failure<SaleDto>(Error.Validation("Solo se devuelve sobre una venta confirmada (no anulada)."));
        var session = await _sessions.FindOpenForRegisterAsync(request.CashRegisterId, ct);
        if (session is null)
            return Result.Failure<SaleDto>(Error.CajaCerrada with { Message = "La caja no tiene una sesión abierta: el efectivo de la devolución sale de ella." });
        if (session.BranchId != sale.BranchId)
            return Result.Failure<SaleDto>(Error.Validation("La devolución se hace en una caja de la misma sucursal de la venta."));

        // Lo ya devuelto por línea limita la nueva devolución (ninguna supera lo vendido).
        var previous = (await _db.SaleReturnLines.AsNoTracking()
                .Where(l => _db.SaleReturns.Any(r => r.Id == l.SaleReturnId && r.SaleId == sale.Id)).ToListAsync(ct))
            .GroupBy(l => l.SaleLineId).ToDictionary(g => g.Key, g => (g.Sum(l => l.Quantity), g.Sum(l => l.Amount)));
        var by = _tenant.UserId!.Value;
        var now = _clock.GetUtcNow().UtcDateTime;
        var saleReturn = SaleReturn.Create(sale, session.Id, request.Reason, by, now);
        foreach (var input in request.Lines)
        {
            var line = sale.Lines.FirstOrDefault(l => l.Id == input.SaleLineId);
            if (line is null)
                return Result.Failure<SaleDto>(Error.NotFound("Línea de venta", input.SaleLineId));
            saleReturn.AddLine(line, input.Quantity, previous.GetValueOrDefault(line.Id), input.Restock);
        }
        _db.SaleReturns.Add(saleReturn);

        var cash = await _drawer.RecordRefundAsync(session.Id, RefundSources.Return, saleReturn.Id, saleReturn.Total, by, ct);
        if (cash.IsFailure) return Result.Failure<SaleDto>(cash.Error);
        var restock = saleReturn.Lines.Where(l => l.Restock).Select(l => (l.Id, l.ProductId, l.Quantity)).ToList();
        var posted = await _inventory.PostReturnAsync(sale.BranchId, saleReturn.Id, restock, by, ct);
        if (posted.IsFailure) return Result.Failure<SaleDto>(posted.Error);
        var refunded = await _payments.RefundAsync(sale.Id, RefundSources.Return, saleReturn.Id, saleReturn.Total, ct);
        if (refunded.IsFailure) return Result.Failure<SaleDto>(refunded.Error);

        // Nota crédito por lo devuelto: precio con impuesto y descuento proporcional, de modo que cada línea sume lo reembolsado.
        var lines = saleReturn.Lines.Select(r =>
        {
            var l = sale.Lines.First(x => x.Id == r.SaleLineId);
            var gross = decimal.Round(r.Quantity * l.UnitPriceWithTax, 2);
            return new SaleFiscalLine(l.ProductId, l.Sku, l.Description, r.Quantity, l.UnitPriceWithTax, l.TaxId, Math.Max(0, gross - r.Amount));
        }).ToList();
        saleReturn.SetFiscalDocument(await _fiscal.EnqueueCreditNoteAsync(sale.Id, saleReturn.Id, isVoid: false, lines, refunded.Value,
            saleReturn.Total, ct));

        sale.RegisterReturn(saleReturn.Total);
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetSaleQuery(sale.Id), ct);
    }

    public async Task<Result<PagedResult<SaleReturnListItemDto>>> Handle(GetSaleReturnsQuery request, CancellationToken ct)
    {
        var query = _db.SaleReturns.AsNoTracking();
        if (request.From is { } from) query = query.Where(r => r.OccurredAt >= from.ToUniversalTime());
        if (request.To is { } to) query = query.Where(r => r.OccurredAt <= to.ToUniversalTime());
        if (request.BranchId is { } branchId) query = query.Where(r => r.BranchId == branchId);

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.OccurredAt)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(r => new { Return = r, LineCount = r.Lines.Count, SaleNumber = _db.Sales.Where(s => s.Id == r.SaleId).Select(s => s.Number).FirstOrDefault() })
            .ToListAsync(ct);
        rows = rows.Where(r => _tenant.CanAccessBranch(r.Return.BranchId)).ToList();
        var notes = (await _fiscal.CreditNotesForSalesAsync(rows.Select(r => r.Return.SaleId).ToList(), ct)).ToDictionary(n => n.SourceId);
        var users = await _users.NamesAsync(rows.Select(r => r.Return.ApprovedBy).ToList(), ct);
        var items = rows.Select(r => new SaleReturnListItemDto(r.Return.Id, r.Return.SaleId, r.SaleNumber, r.Return.BranchId, r.Return.OccurredAt,
            r.Return.Reason, users.GetValueOrDefault(r.Return.ApprovedBy), r.Return.Total, r.LineCount,
            notes.GetValueOrDefault(r.Return.Id)?.Status)).ToList();
        return Result.Success(new PagedResult<SaleReturnListItemDto>(items, request.Page, request.PageSize, total));
    }

    private async Task<Result<Sale>> FindAsync(Guid id, CancellationToken ct)
    {
        var sale = await _db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sale is null)
            return Result.Failure<Sale>(Error.NotFound("Venta", id));
        return _tenant.CanAccessBranch(sale.BranchId) ? Result.Success(sale) : Result.Failure<Sale>(Error.SinAsignacionVigente);
    }
}
