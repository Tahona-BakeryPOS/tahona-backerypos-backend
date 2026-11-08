using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Invoicing.Siigo;
using Tahona.BakeryPOS.Domain.Invoicing;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Invoicing;

/// <param name="Status"><c>pending</c>, <c>submitting</c>, <c>accepted</c>, <c>rejected</c>, <c>temporary_error</c>, <c>unknown</c>, <c>cancelled</c>.</param>
public sealed record FiscalDocumentDto(Guid Id, Guid SaleId, string Kind, string Status, string? Prefix, long? Number, string? Cufe,
    decimal Total, string CustomerIdentification, string? Errors, int Attempts, DateTime CreatedAt, DateTime? AcceptedAt, DateTime? RetainUntil);

public sealed record FiscalAttemptDto(int Number, string Operation, string Outcome, int? HttpStatus, string? Detail, DateTime OccurredAt);

public sealed record GetFiscalDocumentsQuery(DateTime? From, DateTime? To, string? Status) : IRequest<Result<IReadOnlyList<FiscalDocumentDto>>>;

public sealed record GetFiscalDocumentAttemptsQuery(Guid Id) : IRequest<Result<IReadOnlyList<FiscalAttemptDto>>>;

/// <summary>Representación gráfica (PDF) de Siigo; solo de documentos ya creados allí.</summary>
public sealed record GetFiscalDocumentPdfQuery(Guid Id) : IRequest<Result<byte[]>>;

/// <summary>Vuelve a encolar un documento rechazado, con error o desconocido (tras corregir la causa).</summary>
public sealed record RetryFiscalDocumentCommand(Guid Id) : IRequest<Result<FiscalDocumentDto>>;

/// <summary>
/// Factura de prueba (sin venta, fase 7): 1 línea a consumidor final. Es una factura real ante la DIAN en el
/// ambiente de la empresa en Siigo, por eso exige confirmar que ese ambiente es de habilitación (pruebas).
/// </summary>
public sealed record IssueTestInvoiceCommand(Guid ProductId, decimal Quantity, Guid PaymentMethodId, bool ConfirmTestEnvironment)
    : IRequest<Result<FiscalDocumentDto>>;

public sealed class IssueTestInvoiceValidator : AbstractValidator<IssueTestInvoiceCommand>
{
    public IssueTestInvoiceValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(18, 2, true);
        RuleFor(x => x.ConfirmTestEnvironment).Equal(true)
            .WithMessage("Confirma que la empresa en Siigo está en habilitación: la factura de prueba se envía a la DIAN.");
    }
}

public sealed class FiscalDocumentsHandler :
    IRequestHandler<GetFiscalDocumentsQuery, Result<IReadOnlyList<FiscalDocumentDto>>>,
    IRequestHandler<GetFiscalDocumentAttemptsQuery, Result<IReadOnlyList<FiscalAttemptDto>>>,
    IRequestHandler<GetFiscalDocumentPdfQuery, Result<byte[]>>,
    IRequestHandler<RetryFiscalDocumentCommand, Result<FiscalDocumentDto>>,
    IRequestHandler<IssueTestInvoiceCommand, Result<FiscalDocumentDto>>
{
    private readonly IAppDbContext _db;
    private readonly IOutbox _outbox;
    private readonly ISecretProtector _secrets;
    private readonly ISiigoClient _siigo;
    private readonly IProductDirectory _products;
    private readonly IFiscalDocumentIssuer _issuer;
    private readonly TimeProvider _clock;

    public FiscalDocumentsHandler(IAppDbContext db, IOutbox outbox, ISecretProtector secrets, ISiigoClient siigo, IProductDirectory products,
        IFiscalDocumentIssuer issuer, TimeProvider clock)
    {
        _db = db;
        _outbox = outbox;
        _secrets = secrets;
        _siigo = siigo;
        _products = products;
        _issuer = issuer;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<FiscalDocumentDto>>> Handle(GetFiscalDocumentsQuery request, CancellationToken ct)
    {
        var query = _db.FiscalDocuments.AsNoTracking();
        if (request.From is { } from) query = query.Where(d => d.CreatedAt >= from.ToUniversalTime());
        if (request.To is { } to) query = query.Where(d => d.CreatedAt <= to.ToUniversalTime());
        if (request.Status is not null && Enum.TryParse<FiscalDocumentStatus>(request.Status.Replace("_", ""), true, out var status))
            query = query.Where(d => d.Status == status);
        var docs = await query.OrderByDescending(d => d.CreatedAt).Take(500).ToListAsync(ct);
        IReadOnlyList<FiscalDocumentDto> list = docs.Select(ToDto).ToList();
        return Result.Success(list);
    }

    public async Task<Result<IReadOnlyList<FiscalAttemptDto>>> Handle(GetFiscalDocumentAttemptsQuery request, CancellationToken ct)
    {
        if (!await _db.FiscalDocuments.AnyAsync(d => d.Id == request.Id, ct))
            return Result.Failure<IReadOnlyList<FiscalAttemptDto>>(Error.NotFound("Documento fiscal", request.Id));
        IReadOnlyList<FiscalAttemptDto> list = await _db.FiscalSubmissionAttempts.AsNoTracking()
            .Where(a => a.FiscalDocumentId == request.Id).OrderBy(a => a.OccurredAt)
            .Select(a => new FiscalAttemptDto(a.Number, a.Operation, a.Outcome, a.HttpStatus, a.Detail, a.OccurredAt)).ToListAsync(ct);
        return Result.Success(list);
    }

    public async Task<Result<byte[]>> Handle(GetFiscalDocumentPdfQuery request, CancellationToken ct)
    {
        var doc = await _db.FiscalDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.Id, ct);
        if (doc is null)
            return Result.Failure<byte[]>(Error.NotFound("Documento fiscal", request.Id));
        if (doc.ExternalId is null)
            return Result.Failure<byte[]>(Error.Validation("El documento aún no existe en Siigo."));
        var settings = await _db.FiscalSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || settings.EncryptedUsername.Length == 0)
            return Result.Failure<byte[]>(Error.FacturacionNoConfigurada);
        var credentials = new SiigoCredentials(settings.CompanyId, _secrets.Unprotect(settings.EncryptedUsername),
            _secrets.Unprotect(settings.EncryptedAccessKey));
        var pdf = doc.Kind == FiscalDocumentKind.CreditNote
            ? await _siigo.CreditNotePdfAsync(credentials, doc.ExternalId, ct)
            : await _siigo.InvoicePdfAsync(credentials, doc.ExternalId, ct);
        return pdf is null ? Result.Failure<byte[]>(Error.NoEncontrado with { Message = "Siigo no devolvió el PDF." }) : Result.Success(pdf);
    }

    public async Task<Result<FiscalDocumentDto>> Handle(RetryFiscalDocumentCommand request, CancellationToken ct)
    {
        var doc = await _db.FiscalDocuments.FirstOrDefaultAsync(d => d.Id == request.Id, ct);
        if (doc is null)
            return Result.Failure<FiscalDocumentDto>(Error.NotFound("Documento fiscal", request.Id));
        doc.Retry();
        _outbox.Enqueue(FiscalDocumentIssuer.SubmitMessage, new FiscalSubmitMessage(doc.Id));
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(doc));
    }

    public async Task<Result<FiscalDocumentDto>> Handle(IssueTestInvoiceCommand request, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var products = await _products.PricedAsync([request.ProductId], null, now, ct);
        if (!products.TryGetValue(request.ProductId, out var product))
            return Result.Failure<FiscalDocumentDto>(Error.NotFound("Producto", request.ProductId));
        if (product.Price is not { } price || price <= 0)
            return Result.Failure<FiscalDocumentDto>(Error.Validation($"{product.Name} no tiene precio base vigente."));

        // D-07: Siigo recibe el precio con impuesto incluido.
        var unit = product.PriceIncludesTax ? price : decimal.Round(price * (1 + product.TaxRate / 100m), 2);
        var total = decimal.Round(unit * request.Quantity, 2);
        var saleId = Guid.CreateVersion7();
        var id = await _issuer.EnqueueSaleInvoiceAsync(new SaleFiscalSnapshot(saleId, now, null,
            [new SaleFiscalLine(product.Id, product.Sku, product.Name, request.Quantity, unit, product.TaxId)],
            [new SaleFiscalPayment(request.PaymentMethodId, total)], total), ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(await _db.FiscalDocuments.AsNoTracking().FirstAsync(d => d.Id == id, ct)));
    }

    private static FiscalDocumentDto ToDto(FiscalDocument d) => new(d.Id, d.SaleId, d.Kind == FiscalDocumentKind.Invoice ? "invoice" : "credit_note",
        StatusName(d.Status), d.Prefix, d.Number, d.Cufe, d.Total, d.CustomerIdentification, d.Errors, d.Attempts, d.CreatedAt, d.AcceptedAt, d.RetainUntil);

    private static string StatusName(FiscalDocumentStatus s) => s == FiscalDocumentStatus.TemporaryError ? "temporary_error" : s.ToString().ToLowerInvariant();
}
