using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Domain.Payments;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Payments;

/// <param name="Kind"><c>cash</c>, <c>gateway</c>, <c>external_card</c> o <c>transfer_manual</c>.</param>
/// <param name="GatewayMethod">With <c>gateway</c>: <c>POS</c>, <c>NEQUI</c> or <c>PAY_BY_QR_BOLD</c>.</param>
public sealed record PaymentMethodDto(Guid Id, string Code, string Name, string Kind, string? GatewayMethod, bool RequiresReference,
    int DisplayOrder, bool IsActive, string Version)
{
    public static PaymentMethodDto From(PaymentMethod m) => new(m.Id, m.Code, m.Name, PaymentKinds.Name(m.Kind), m.GatewayMethod,
        m.RequiresReference, m.DisplayOrder, m.IsActive, RowVersions.Encode(m.RowVersion));
}

public sealed record GetPaymentMethodsQuery : IRequest<Result<IReadOnlyList<PaymentMethodDto>>>;

/// <summary>Creates a method. Cash already exists; the gateway only accepts Bold's methods.</summary>
public sealed record CreatePaymentMethodCommand(string Code, string Name, string Kind, string? GatewayMethod, bool RequiresReference, int DisplayOrder)
    : IRequest<Result<PaymentMethodDto>>;

/// <summary>Name, reference, order and status. Requires the version read (If-Match).</summary>
public sealed record UpdatePaymentMethodCommand(Guid Id, string? ExpectedVersion, string Name, bool RequiresReference, int DisplayOrder, bool IsActive)
    : IRequest<Result<PaymentMethodDto>>;

public static class PaymentKinds
{
    public static string Name(PaymentMethodKind kind) => kind switch
    {
        PaymentMethodKind.Cash => "cash",
        PaymentMethodKind.Gateway => "gateway",
        PaymentMethodKind.ExternalCard => "external_card",
        _ => "transfer_manual",
    };

    public static bool TryParse(string value, out PaymentMethodKind kind) =>
        Enum.TryParse(value.Replace("_", ""), true, out kind) && Enum.IsDefined(kind);
}

public sealed class CreatePaymentMethodValidator : AbstractValidator<CreatePaymentMethodCommand>
{
    public CreatePaymentMethodValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("El código es obligatorio: letras, números, guion o guion bajo (máx. 30).");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Kind).Must(k => PaymentKinds.TryParse(k, out var kind) && kind != PaymentMethodKind.Cash)
            .WithMessage("Tipo: gateway, external_card o transfer_manual (el efectivo ya existe).");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdatePaymentMethodValidator : AbstractValidator<UpdatePaymentMethodCommand>
{
    public UpdatePaymentMethodValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class PaymentMethodsHandler :
    IRequestHandler<GetPaymentMethodsQuery, Result<IReadOnlyList<PaymentMethodDto>>>,
    IRequestHandler<CreatePaymentMethodCommand, Result<PaymentMethodDto>>,
    IRequestHandler<UpdatePaymentMethodCommand, Result<PaymentMethodDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public PaymentMethodsHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Result<IReadOnlyList<PaymentMethodDto>>> Handle(GetPaymentMethodsQuery request, CancellationToken ct)
    {
        var methods = await _db.PaymentMethods.AsNoTracking().OrderBy(m => m.DisplayOrder).ThenBy(m => m.Name).ToListAsync(ct);
        IReadOnlyList<PaymentMethodDto> list = methods.Select(PaymentMethodDto.From).ToList();
        return Result.Success(list);
    }

    public async Task<Result<PaymentMethodDto>> Handle(CreatePaymentMethodCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.PaymentMethods.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(m => m.Code == code, ct))
            return Result.Failure<PaymentMethodDto>(Error.CodigoDuplicado);

        PaymentKinds.TryParse(request.Kind, out var kind);
        var method = PaymentMethod.Create(_tenant.RequiredCompanyId, code, request.Name, kind,
            kind == PaymentMethodKind.Gateway ? request.GatewayMethod : null, request.RequiresReference, request.DisplayOrder);
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(ct);
        return Result.Success(PaymentMethodDto.From(method));
    }

    public async Task<Result<PaymentMethodDto>> Handle(UpdatePaymentMethodCommand request, CancellationToken ct)
    {
        var version = RowVersions.Decode(request.ExpectedVersion);
        if (version.IsFailure)
            return Result.Failure<PaymentMethodDto>(version.Error);
        var method = await _db.PaymentMethods.FirstOrDefaultAsync(m => m.Id == request.Id, ct);
        if (method is null)
            return Result.Failure<PaymentMethodDto>(Error.NotFound("Medio de pago", request.Id));
        if (request.IsActive && method.Kind == PaymentMethodKind.Gateway
            && !await _db.GatewayAccounts.AnyAsync(a => a.IsActive, ct))
            return Result.Failure<PaymentMethodDto>(Error.PasarelaNoConfigurada with
            { Message = "Configura y activa la integración con Bold antes de activar este medio." });

        _db.ExpectVersion(method, version.Value);
        method.Update(request.Name, request.RequiresReference, request.DisplayOrder, request.IsActive);
        await _db.SaveChangesAsync(ct);
        return Result.Success(PaymentMethodDto.From(method));
    }
}

/// <summary>Payment methods each company is born with (I-23).</summary>
public sealed class PaymentsProvisioning : IPaymentsProvisioning
{
    private readonly IAppDbContext _db;

    public PaymentsProvisioning(IAppDbContext db)
    {
        _db = db;
    }

    public void ProvisionCompany(Guid companyId)
    {
        var order = 0;
        foreach (var (code, name, kind, gatewayMethod, active) in DefaultPaymentMethods.All)
        {
            var method = PaymentMethod.Create(companyId, code, name, kind, gatewayMethod, requiresReference: false, order++);
            if (!active) method.Update(name, method.RequiresReference, method.DisplayOrder, isActive: false);
            _db.PaymentMethods.Add(method);
        }
    }
}
