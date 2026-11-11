using System.Text.Json;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Domain.Sales;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Sales;

/// <param name="DiscountAmount">The line's discount in money (requires a reason and <c>sales.discount</c>).</param>
public sealed record SaleLineInput(Guid ProductId, decimal Quantity, decimal DiscountAmount = 0, string? DiscountReason = null);

/// <summary>
/// [1] Creates the draft sale at the indicated register (it must have an open session). Prices and taxes are
/// set by the server with the branch's current price (D-07) and are frozen on the sale.
/// </summary>
public sealed record CreateSaleCommand(Guid CashRegisterId, string? CustomerIdentification, IReadOnlyList<SaleLineInput> Lines)
    : IRequest<Result<SaleDto>>;

/// <summary>Identifies the customer (NIT or ID for the invoice) or leaves them as a final consumer (null).</summary>
public sealed record SetSaleCustomerCommand(Guid SaleId, string? CustomerIdentification) : IRequest<Result<SaleDto>>;

/// <summary>
/// Authorizes selling "block"-policy products without enough stock (D-06). It is sent by the supervisor
/// who authorizes with their own token (permission <c>inventory.adjust</c>); it is stored on the sale and on the movement.
/// </summary>
public sealed record AuthorizeSaleStockCommand(Guid SaleId) : IRequest<Result<SaleDto>>;

/// <summary>
/// [2] Adds a payment. Cash (with received amount and change), transfer or external card terminal are approved when
/// recorded; if that covers the total, the sale is confirmed in the same request. Bold sends the charge to the
/// card terminal and the sale stays pending payment until the webhook.
/// </summary>
public sealed record AddSalePaymentCommand(Guid SaleId, Guid PaymentMethodId, decimal Amount, decimal? AmountTendered, string? Reference)
    : IRequest<Result<SaleDto>>;

/// <summary>Cancels a sale with no approved or in-progress payments (it was never charged).</summary>
public sealed record CancelSaleCommand(Guid SaleId) : IRequest<Result<SaleDto>>;

public sealed class CreateSaleValidator : AbstractValidator<CreateSaleCommand>
{
    public CreateSaleValidator()
    {
        RuleFor(x => x.CashRegisterId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("La venta necesita al menos un producto.").Must(l => l.Count <= 200);
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(18, 2, true);
            l.RuleFor(x => x.DiscountAmount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, true);
            l.RuleFor(x => x.DiscountReason).NotEmpty().MaximumLength(200).When(x => x.DiscountAmount > 0)
                .WithMessage("El descuento exige motivo.");
        });
    }
}

public sealed class AddSalePaymentValidator : AbstractValidator<AddSalePaymentCommand>
{
    public AddSalePaymentValidator()
    {
        RuleFor(x => x.PaymentMethodId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.AmountTendered).GreaterThan(0).PrecisionScale(18, 2, true).When(x => x.AmountTendered is not null);
        RuleFor(x => x.Reference).MaximumLength(100);
    }
}

public sealed class SalesCommandsHandler :
    IRequestHandler<CreateSaleCommand, Result<SaleDto>>,
    IRequestHandler<SetSaleCustomerCommand, Result<SaleDto>>,
    IRequestHandler<AuthorizeSaleStockCommand, Result<SaleDto>>,
    IRequestHandler<AddSalePaymentCommand, Result<SaleDto>>,
    IRequestHandler<CancelSaleCommand, Result<SaleDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICashSessionDirectory _sessions;
    private readonly IProductDirectory _products;
    private readonly IInventoryPosting _inventory;
    private readonly ISalePayments _payments;
    private readonly SaleConfirmation _confirmation;
    private readonly ISender _mediator;
    private readonly TimeProvider _clock;

    public SalesCommandsHandler(IAppDbContext db, ITenantContext tenant, ICashSessionDirectory sessions, IProductDirectory products,
        IInventoryPosting inventory, ISalePayments payments, SaleConfirmation confirmation, ISender mediator, TimeProvider clock)
    {
        _db = db;
        _tenant = tenant;
        _sessions = sessions;
        _products = products;
        _inventory = inventory;
        _payments = payments;
        _confirmation = confirmation;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<Result<SaleDto>> Handle(CreateSaleCommand request, CancellationToken ct)
    {
        var session = await _sessions.FindOpenForRegisterAsync(request.CashRegisterId, ct);
        if (session is null)
            return Result.Failure<SaleDto>(Error.CashSessionClosed with { Message = "La caja no tiene una sesión abierta." });
        if (!_tenant.CanAccessBranch(session.BranchId))
            return Result.Failure<SaleDto>(Error.NoCurrentAssignment);
        if (request.Lines.Any(l => l.DiscountAmount > 0) && !_tenant.HasPermission(Permissions.SalesDiscount))
            return Result.Failure<SaleDto>(Error.PermissionDenied with { Message = "Aplicar descuentos requiere el permiso ventas.descuento." });

        var now = _clock.GetUtcNow().UtcDateTime;
        var products = await _products.PricedAsync(request.Lines.Select(l => l.ProductId).ToList(), session.BranchId, now, ct);
        var sale = Sale.Start(_tenant.RequiredCompanyId, session.BranchId, session.CashRegisterId, session.Id,
            _tenant.UserId ?? throw new InvalidOperationException("Selling requires a user in session."), request.CustomerIdentification);
        foreach (var line in request.Lines)
        {
            if (!products.TryGetValue(line.ProductId, out var p))
                return Result.Failure<SaleDto>(Error.NotFound("Producto", line.ProductId));
            if (!p.IsActive || !p.IsAvailable)
                return Result.Failure<SaleDto>(Error.Validation($"{p.Name} no está disponible en esta sucursal."));
            if (p.Price is not { } price)
                return Result.Failure<SaleDto>(Error.Validation($"{p.Name} no tiene precio vigente."));
            if (!p.AllowsDecimals && decimal.Truncate(line.Quantity) != line.Quantity)
                return Result.Failure<SaleDto>(Error.Validation($"{p.Name} se vende por unidades."));
            sale.AddLine(p.Id, p.Sku, p.Name, p.UnitCode, line.Quantity, price, p.PriceIncludesTax,
                line.DiscountAmount, line.DiscountReason, p.TaxId, p.TaxRate);
        }
        _db.Sales.Add(sale);
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetSaleQuery(sale.Id), ct);
    }

    public async Task<Result<SaleDto>> Handle(SetSaleCustomerCommand request, CancellationToken ct)
    {
        var sale = await FindAsync(request.SaleId, ct);
        if (sale.IsFailure) return Result.Failure<SaleDto>(sale.Error);
        sale.Value.SetCustomer(request.CustomerIdentification);
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetSaleQuery(request.SaleId), ct);
    }

    public async Task<Result<SaleDto>> Handle(AuthorizeSaleStockCommand request, CancellationToken ct)
    {
        if (!_tenant.HasPermission(Permissions.InventoryAdjust))
            return Result.Failure<SaleDto>(Error.PermissionDenied with { Message = "Autorizar vender sin existencias requiere el permiso inventario.ajustar." });
        var sale = await FindAsync(request.SaleId, ct);
        if (sale.IsFailure) return Result.Failure<SaleDto>(sale.Error);
        sale.Value.AuthorizeStock(_tenant.UserId!.Value);
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetSaleQuery(request.SaleId), ct);
    }

    public async Task<Result<SaleDto>> Handle(AddSalePaymentCommand request, CancellationToken ct)
    {
        var found = await FindAsync(request.SaleId, ct);
        if (found.IsFailure) return Result.Failure<SaleDto>(found.Error);
        var sale = found.Value;
        if (!sale.IsOpen)
            return Result.Failure<SaleDto>(Error.Validation("La venta ya no admite pagos."));
        var session = await _sessions.FindAsync(sale.CashSessionId, ct);
        if (session is null || !session.IsOpen)
            return Result.Failure<SaleDto>(Error.CashSessionClosed);

        var existing = await _payments.ForSaleAsync(sale.Id, ct);
        if (existing.Any(p => p.Status == "pending"))
            return Result.Failure<SaleDto>(Error.PaymentInProgress with { Message = "Hay un cobro en curso en el datáfono: espera su resultado." });
        var remaining = sale.Total - existing.Where(p => p.Status == "approved").Sum(p => p.Amount);
        if (request.Amount > remaining)
            return Result.Failure<SaleDto>(Error.Validation($"El monto supera lo que falta por pagar ({remaining:N2}). El excedente en efectivo va en 'recibido'."));

        // D-06: before charging, "block" products are checked; without authorization it is not charged.
        if (sale.StockAuthorizedBy is null)
        {
            var shortages = await _inventory.ShortagesAsync(sale.BranchId, sale.Lines.Select(l => (l.ProductId, l.Quantity)).ToList(), ct);
            if (shortages.Count > 0)
                return Result.Failure<SaleDto>(Error.InsufficientStock with
                {
                    Message = "Sin existencias suficientes: " + string.Join("; ", shortages.Select(s => $"{s.Name} (hay {s.OnHand:0.##}, se piden {s.Requested:0.##})"))
                        + ". Pide autorización a un superior.",
                });
        }

        var kind = await _payments.MethodKindAsync(request.PaymentMethodId, ct);
        if (kind is null)
            return Result.Failure<SaleDto>(Error.Validation("El medio de pago no existe o está desactivado."));

        sale.MarkPendingPayment(sale.Total - remaining);
        await _db.SaveChangesAsync(ct);

        var payment = kind == "gateway"
            ? await _payments.StartGatewayAsync(sale.Id, session, request.PaymentMethodId, request.Amount, $"Venta {sale.Id:N}", ct)
            : await _payments.RecordImmediateAsync(sale.Id, session, request.PaymentMethodId, request.Amount, request.AmountTendered, request.Reference, ct);
        if (payment.IsFailure)
            return Result.Failure<SaleDto>(payment.Error);

        var confirmed = await _confirmation.TryConfirmAsync(sale, ct);
        if (confirmed.IsFailure)
            return Result.Failure<SaleDto>(confirmed.Error);
        return await _mediator.Send(new GetSaleQuery(sale.Id), ct);
    }

    public async Task<Result<SaleDto>> Handle(CancelSaleCommand request, CancellationToken ct)
    {
        var sale = await FindAsync(request.SaleId, ct);
        if (sale.IsFailure) return Result.Failure<SaleDto>(sale.Error);
        var payments = await _payments.ForSaleAsync(request.SaleId, ct);
        if (payments.Any(p => p.Status is "approved" or "pending"))
            return Result.Failure<SaleDto>(Error.Validation(
                "La venta tiene pagos aprobados o en curso: no se cancela. Resuelve el pago pendiente o anúlala cuando esté confirmada."));
        sale.Value.Cancel(_clock.GetUtcNow().UtcDateTime);
        await _db.SaveChangesAsync(ct);
        return await _mediator.Send(new GetSaleQuery(request.SaleId), ct);
    }

    private async Task<Result<Sale>> FindAsync(Guid id, CancellationToken ct)
    {
        var sale = await _db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sale is null)
            return Result.Failure<Sale>(Error.NotFound("Venta", id));
        return _tenant.CanAccessBranch(sale.BranchId) ? Result.Success(sale) : Result.Failure<Sale>(Error.NoCurrentAssignment);
    }
}

/// <summary>
/// A sale payment was resolved (Bold webhook or manual resolution): confirming the sale is attempted in the
/// background, within the message's company. Idempotent.
/// </summary>
public sealed class SalePaymentResolvedHandler : IOutboxHandler
{
    private readonly IAppDbContext _db;
    private readonly IBackgroundTenant _tenant;
    private readonly SaleConfirmation _confirmation;

    public SalePaymentResolvedHandler(IAppDbContext db, IBackgroundTenant tenant, SaleConfirmation confirmation)
    {
        _db = db;
        _tenant = tenant;
        _confirmation = confirmation;
    }

    public string Type => SaleMessages.PaymentResolved;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<SalePaymentResolved>(message.Payload, JsonSerializerOptions.Web)!;
        _tenant.Enter(payload.CompanyId);
        var sale = await _db.Sales.Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == payload.SaleId, ct);
        if (sale is null)
            return;
        var result = await _confirmation.TryConfirmAsync(sale, ct);
        if (result.IsFailure)
            throw new InvalidOperationException($"Sale {sale.Id} could not be confirmed: {result.Error.Code} {result.Error.Message}");
    }
}
