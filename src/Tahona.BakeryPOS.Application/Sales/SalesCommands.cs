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

/// <param name="DiscountAmount">Descuento en dinero de la línea (requiere motivo y <c>ventas.descuento</c>).</param>
public sealed record SaleLineInput(Guid ProductId, decimal Quantity, decimal DiscountAmount = 0, string? DiscountReason = null);

/// <summary>
/// [1] Crea la venta en borrador en la caja indicada (debe tener sesión abierta). Precios e impuestos los
/// pone el servidor con el precio vigente de la sucursal (D-07) y quedan congelados en la venta.
/// </summary>
public sealed record CreateSaleCommand(Guid CashRegisterId, string? CustomerIdentification, IReadOnlyList<SaleLineInput> Lines)
    : IRequest<Result<SaleDto>>;

/// <summary>Identifica al cliente (NIT o cédula para la factura) o lo deja como consumidor final (null).</summary>
public sealed record SetSaleCustomerCommand(Guid SaleId, string? CustomerIdentification) : IRequest<Result<SaleDto>>;

/// <summary>
/// Autoriza vender productos con política "bloquear" sin existencias suficientes (D-06). Lo envía el superior
/// que autoriza con su propio token (permiso <c>inventario.ajustar</c>); queda en la venta y en el movimiento.
/// </summary>
public sealed record AuthorizeSaleStockCommand(Guid SaleId) : IRequest<Result<SaleDto>>;

/// <summary>
/// [2] Agrega un pago. Efectivo (con recibido y cambio), transferencia o datáfono externo se aprueban al
/// registrarse; si con eso se cubre el total, la venta se confirma en la misma petición. Bold envía el cobro al
/// datáfono y la venta queda pendiente de pago hasta el webhook.
/// </summary>
public sealed record AddSalePaymentCommand(Guid SaleId, Guid PaymentMethodId, decimal Amount, decimal? AmountTendered, string? Reference)
    : IRequest<Result<SaleDto>>;

/// <summary>Cancela una venta sin pagos aprobados ni en curso (nunca se cobró).</summary>
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
            return Result.Failure<SaleDto>(Error.CajaCerrada with { Message = "La caja no tiene una sesión abierta." });
        if (!_tenant.CanAccessBranch(session.BranchId))
            return Result.Failure<SaleDto>(Error.SinAsignacionVigente);
        if (request.Lines.Any(l => l.DiscountAmount > 0) && !_tenant.HasPermission(Permissions.VentasDescuento))
            return Result.Failure<SaleDto>(Error.SinPermiso with { Message = "Aplicar descuentos requiere el permiso ventas.descuento." });

        var now = _clock.GetUtcNow().UtcDateTime;
        var products = await _products.PricedAsync(request.Lines.Select(l => l.ProductId).ToList(), session.BranchId, now, ct);
        var sale = Sale.Start(_tenant.RequiredCompanyId, session.BranchId, session.CashRegisterId, session.Id,
            _tenant.UserId ?? throw new InvalidOperationException("Vender requiere un usuario en sesión."), request.CustomerIdentification);
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
        if (!_tenant.HasPermission(Permissions.InventarioAjustar))
            return Result.Failure<SaleDto>(Error.SinPermiso with { Message = "Autorizar vender sin existencias requiere el permiso inventario.ajustar." });
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
            return Result.Failure<SaleDto>(Error.CajaCerrada);

        var existing = await _payments.ForSaleAsync(sale.Id, ct);
        if (existing.Any(p => p.Status == "pending"))
            return Result.Failure<SaleDto>(Error.PagoEnCurso with { Message = "Hay un cobro en curso en el datáfono: espera su resultado." });
        var remaining = sale.Total - existing.Where(p => p.Status == "approved").Sum(p => p.Amount);
        if (request.Amount > remaining)
            return Result.Failure<SaleDto>(Error.Validation($"El monto supera lo que falta por pagar ({remaining:N2}). El excedente en efectivo va en 'recibido'."));

        // D-06: antes de cobrar se revisan los productos "bloquear"; sin autorización no se cobra.
        if (sale.StockAuthorizedBy is null)
        {
            var shortages = await _inventory.ShortagesAsync(sale.BranchId, sale.Lines.Select(l => (l.ProductId, l.Quantity)).ToList(), ct);
            if (shortages.Count > 0)
                return Result.Failure<SaleDto>(Error.ExistenciasInsuficientes with
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
        return _tenant.CanAccessBranch(sale.BranchId) ? Result.Success(sale) : Result.Failure<Sale>(Error.SinAsignacionVigente);
    }
}

/// <summary>
/// Un pago de venta se resolvió (webhook de Bold o resolución manual): se intenta confirmar la venta en
/// segundo plano, dentro de la empresa del mensaje. Idempotente.
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
            throw new InvalidOperationException($"No se pudo confirmar la venta {sale.Id}: {result.Error.Code} {result.Error.Message}");
    }
}
