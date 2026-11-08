using System.Text.Json;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Invoicing.Siigo;
using Tahona.BakeryPOS.Domain.Invoicing;
using Tahona.BakeryPOS.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Tahona.BakeryPOS.Application.Invoicing;

/// <summary>Mensaje de outbox que dispara el envío de un documento fiscal.</summary>
public sealed record FiscalSubmitMessage(Guid FiscalDocumentId);

/// <summary>
/// Crea la factura electrónica de una venta en la transacción del llamador y encola su envío (D-05:
/// factura en cada venta, con el cliente o como consumidor final).
/// </summary>
public sealed class FiscalDocumentIssuer : IFiscalDocumentIssuer
{
    public const string SubmitMessage = "fiscal.submit";

    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IOutbox _outbox;

    public FiscalDocumentIssuer(IAppDbContext db, ITenantContext tenant, IOutbox outbox)
    {
        _db = db;
        _tenant = tenant;
        _outbox = outbox;
    }

    public Task<bool> IsActiveAsync(CancellationToken ct) => _db.FiscalSettings.AnyAsync(s => s.IsActive, ct);

    public async Task<IReadOnlyDictionary<Guid, FiscalDocumentSummary>> ForSalesAsync(IReadOnlyCollection<Guid> saleIds, CancellationToken ct)
    {
        var ids = saleIds.Distinct().ToList();
        var docs = await _db.FiscalDocuments.AsNoTracking().Where(d => ids.Contains(d.SaleId) && d.Kind == FiscalDocumentKind.Invoice).ToListAsync(ct);
        return docs.ToDictionary(d => d.SaleId, d => new FiscalDocumentSummary(d.Id, d.SaleId,
            d.Status == FiscalDocumentStatus.TemporaryError ? "temporary_error" : d.Status.ToString().ToLowerInvariant(), d.Prefix, d.Number, d.Cufe, d.Errors));
    }

    public async Task<Guid> EnqueueSaleInvoiceAsync(SaleFiscalSnapshot sale, CancellationToken ct)
    {
        var settings = await _db.FiscalSettings.AsNoTracking().FirstOrDefaultAsync(s => s.IsActive, ct)
            ?? throw new Domain.Exceptions.DomainException(Error.FacturacionNoConfigurada.Code, Error.FacturacionNoConfigurada.Message);
        var customer = string.IsNullOrWhiteSpace(sale.CustomerIdentification) ? settings.ConsumerFinalIdentification : sale.CustomerIdentification.Trim();
        var doc = FiscalDocument.Create(_tenant.RequiredCompanyId, sale.SaleId, FiscalDocumentKind.Invoice,
            JsonSerializer.Serialize(sale), sale.Total, customer);
        _db.FiscalDocuments.Add(doc);
        _outbox.Enqueue(SubmitMessage, new FiscalSubmitMessage(doc.Id));
        return doc.Id;
    }

    public async Task<Guid?> EnqueueCreditNoteAsync(Guid saleId, Guid sourceId, bool isVoid, IReadOnlyList<SaleFiscalLine>? lines,
        IReadOnlyList<SaleFiscalPayment>? payments, decimal total, CancellationToken ct)
    {
        var invoice = await _db.FiscalDocuments.FirstOrDefaultAsync(d => d.SaleId == saleId && d.Kind == FiscalDocumentKind.Invoice, ct);
        if (invoice is null || invoice.Status == FiscalDocumentStatus.Cancelled)
            return null;
        var existing = await _db.FiscalDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Kind == FiscalDocumentKind.CreditNote && d.SourceId == sourceId, ct);
        if (existing is not null)
            return existing.Id;
        // Una factura rechazada nunca tuvo validez ante la DIAN: no se corrige con nota. Si la venta se anula, la
        // factura se cancela para que nadie la reintente; en una devolución parcial queda como está (se corrige y reenvía).
        if (invoice.Status == FiscalDocumentStatus.Rejected)
        {
            if (isVoid) invoice.Cancel("Venta anulada antes de que la factura fuera aceptada.");
            return null;
        }

        var invoiceSale = JsonSerializer.Deserialize<SaleFiscalSnapshot>(invoice.Payload)!;
        var snapshot = new SaleFiscalSnapshot(saleId, DateTime.UtcNow, invoiceSale.CustomerIdentification,
            lines ?? invoiceSale.Lines, payments ?? invoiceSale.Payments, isVoid ? invoice.Total : total);
        // Si la factura sigue en trámite, la nota espera en el outbox hasta que sea aceptada (FiscalSubmitHandler).
        var note = FiscalDocument.CreateCreditNote(invoice, sourceId, isVoid ? 2 : 1, JsonSerializer.Serialize(snapshot), snapshot.Total);
        _db.FiscalDocuments.Add(note);
        _outbox.Enqueue(SubmitMessage, new FiscalSubmitMessage(note.Id));
        return note.Id;
    }

    public async Task<IReadOnlyList<CreditNoteSummary>> CreditNotesForSalesAsync(IReadOnlyCollection<Guid> saleIds, CancellationToken ct)
    {
        var ids = saleIds.Distinct().ToList();
        var docs = await _db.FiscalDocuments.AsNoTracking()
            .Where(d => ids.Contains(d.SaleId) && d.Kind == FiscalDocumentKind.CreditNote).OrderBy(d => d.CreatedAt).ToListAsync(ct);
        return docs.Select(d => new CreditNoteSummary(d.Id, d.SaleId, d.SourceId ?? d.SaleId, d.CreditReason ?? 0,
            d.Status == FiscalDocumentStatus.TemporaryError ? "temporary_error" : d.Status.ToString().ToLowerInvariant(),
            d.Prefix, d.Number, d.Cufe, d.Errors, d.Total)).ToList();
    }
}

/// <summary>
/// Envía un documento fiscal a Siigo desde el outbox (fuera de cualquier transacción de negocio). Reglas
/// (electronic-invoicing.md §4 y §9): solo se acepta con CUFE; un resultado desconocido se reenvía con la
/// misma Idempotency-Key, que en Siigo devuelve la factura ya creada (sirve de consulta); un rechazo no se
/// reintenta solo. Cada paso queda en <c>fiscal_submission_attempts</c>. Lanzar una excepción deja el
/// mensaje para reintento con espera (OutboxProcessor).
/// </summary>
public sealed class FiscalSubmitHandler : IOutboxHandler
{
    private readonly IAppDbContext _db;
    private readonly ISiigoClient _siigo;
    private readonly ISecretProtector _secrets;
    private readonly TimeProvider _clock;
    private readonly ILogger<FiscalSubmitHandler> _logger;

    public FiscalSubmitHandler(IAppDbContext db, ISiigoClient siigo, ISecretProtector secrets, TimeProvider clock, ILogger<FiscalSubmitHandler> logger)
    {
        _db = db;
        _siigo = siigo;
        _secrets = secrets;
        _clock = clock;
        _logger = logger;
    }

    public string Type => FiscalDocumentIssuer.SubmitMessage;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken ct)
    {
        // El outbox guarda el mensaje con JsonSerializerOptions.Web (camelCase).
        var id = JsonSerializer.Deserialize<FiscalSubmitMessage>(message.Payload, JsonSerializerOptions.Web)!.FiscalDocumentId;
        // El outbox corre sin sesión de empresa: se filtra a mano por la empresa del mensaje.
        var doc = await _db.FiscalDocuments.IgnoreQueryFilters([QueryFilters.Tenant])
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == message.CompanyId, ct);
        if (doc is null || doc.IsFinal || doc.Status == FiscalDocumentStatus.Rejected)
            return;

        // Nota crédito: necesita la factura aceptada (con su id en Siigo). Mientras siga en trámite, se espera.
        FiscalDocument? creditedInvoice = null;
        if (doc.Kind == FiscalDocumentKind.CreditNote)
        {
            var credited = await _db.FiscalDocuments.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == doc.InvoiceDocumentId && d.CompanyId == doc.CompanyId, ct);
            if (credited is null || credited.Status is FiscalDocumentStatus.Rejected or FiscalDocumentStatus.Cancelled)
            {
                doc.Cancel("La factura de la venta no es válida ante la DIAN: no hay nada que corregir con nota crédito.");
                await _db.SaveChangesAsync(ct);
                return;
            }
            if (credited.Status != FiscalDocumentStatus.Accepted || credited.ExternalId is null)
                throw new InvalidOperationException($"La nota crédito {doc.Id} espera a que la factura sea aceptada.");
            creditedInvoice = credited;
        }

        var settings = await _db.FiscalSettings.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == doc.CompanyId && s.IsActive, ct);
        doc.StartSubmission();
        if (settings is null || !settings.IsComplete)
        {
            await FailTemporarilyAsync(doc, "La facturación electrónica no está configurada y activa.", ct);
            throw new InvalidOperationException("Facturación no configurada; se reintenta.");
        }
        var credentials = new SiigoCredentials(doc.CompanyId, _secrets.Unprotect(settings.EncryptedUsername), _secrets.Unprotect(settings.EncryptedAccessKey));

        if (creditedInvoice is not null)
        {
            await SubmitCreditNoteAsync(doc, creditedInvoice, settings, credentials, ct);
            return;
        }

        // Ya creado antes (desconocido o pendiente de DIAN): consultar, y enviar a la DIAN si quedó en borrador.
        if (doc.ExternalId is not null)
        {
            var current = await _siigo.GetInvoiceAsync(credentials, doc.ExternalId, ct);
            Record(doc, "status", current);
            if (current.Outcome == SiigoOutcome.Ok && IsDraft(current.StampStatus))
            {
                current = await _siigo.StampInvoiceAsync(credentials, doc.ExternalId, ct);
                Record(doc, "stamp", current);
            }
            await ApplyAsync(doc, settings, credentials, current, ct);
            return;
        }

        var sale = JsonSerializer.Deserialize<SaleFiscalSnapshot>(doc.Payload)!;
        var invoice = await BuildAsync(doc, settings, credentials, sale, ct);
        if (invoice is null)
            return;

        var result = await _siigo.CreateInvoiceAsync(credentials, invoice, doc.IdempotencyKey, ct);
        Record(doc, "create", result);
        await ApplyAsync(doc, settings, credentials, result, ct);
    }

    /// <summary>
    /// Nota crédito (<c>POST /v1/credit-notes</c>, electronic-invoicing.md §9): sobre la factura en Siigo, con el motivo DIAN,
    /// lo devuelto y los medios reembolsados. Siigo no documenta reenvío a la DIAN de notas: si queda en borrador se consulta
    /// de nuevo con <c>GET /v1/credit-notes/{id}</c>.
    /// </summary>
    private async Task SubmitCreditNoteAsync(FiscalDocument doc, FiscalDocument invoice, FiscalSettings settings, SiigoCredentials credentials,
        CancellationToken ct)
    {
        if (doc.ExternalId is not null)
        {
            var current = await _siigo.GetCreditNoteAsync(credentials, doc.ExternalId, ct);
            Record(doc, "status", current);
            await ApplyAsync(doc, settings, credentials, current, ct);
            return;
        }
        if (settings.CreditNoteDocumentTypeId is null)
        {
            doc.Reject("Falta elegir el tipo de comprobante de nota crédito en la configuración de facturación.");
            _db.FiscalSubmissionAttempts.Add(FiscalSubmissionAttempt.Record(doc, "create", "rejected", null, doc.Errors, Now));
            await _db.SaveChangesAsync(ct);
            return;
        }
        var mappings = await _db.FiscalMappings.IgnoreQueryFilters([QueryFilters.Tenant]).AsNoTracking()
            .Where(m => m.CompanyId == doc.CompanyId).ToListAsync(ct);
        string? External(FiscalMappingKind kind, Guid local) => mappings.FirstOrDefault(m => m.Kind == kind && m.LocalId == local)?.ExternalId;
        var note = JsonSerializer.Deserialize<SaleFiscalSnapshot>(doc.Payload)!;
        var missing = note.Payments.Select(p => p.PaymentMethodId).Distinct().Where(p => External(FiscalMappingKind.PaymentMethod, p) is null).ToList();
        if (missing.Count > 0)
        {
            doc.Reject($"Falta emparejar con Siigo los medios de pago: {string.Join(", ", missing)}.");
            _db.FiscalSubmissionAttempts.Add(FiscalSubmissionAttempt.Record(doc, "create", "rejected", null, doc.Errors, Now));
            await _db.SaveChangesAsync(ct);
            return;
        }
        var creditNote = new SiigoCreditNote(settings.CreditNoteDocumentTypeId.Value, DateOnly.FromDateTime(ColombiaToday()), invoice.ExternalId!,
            doc.CreditReason ?? 1,
            note.Lines.Select(l => new SiigoInvoiceItem(l.Sku, l.Name, l.Quantity, l.UnitPriceWithTax,
                External(FiscalMappingKind.Tax, l.TaxId) is { } tax ? [long.Parse(tax)] : [], l.Discount)).ToList(),
            note.Payments.GroupBy(p => External(FiscalMappingKind.PaymentMethod, p.PaymentMethodId)!)
                .Select(g => new SiigoPayment(long.Parse(g.Key), g.Sum(p => p.Amount))).ToList(),
            doc.CreditReason == 2 ? $"Anulación de la venta {note.SaleId}" : $"Devolución de la venta {note.SaleId}");
        var result = await _siigo.CreateCreditNoteAsync(credentials, creditNote, doc.IdempotencyKey, ct);
        Record(doc, "create", result);
        await ApplyAsync(doc, settings, credentials, result, ct);
    }

    /// <summary>Arma la factura con los emparejamientos; crea en Siigo los productos que aún no existen allí.</summary>
    private async Task<SiigoInvoice?> BuildAsync(FiscalDocument doc, FiscalSettings settings, SiigoCredentials credentials, SaleFiscalSnapshot sale, CancellationToken ct)
    {
        var mappings = await _db.FiscalMappings.IgnoreQueryFilters([QueryFilters.Tenant])
            .Where(m => m.CompanyId == doc.CompanyId).ToListAsync(ct);
        string? External(FiscalMappingKind kind, Guid local) => mappings.FirstOrDefault(m => m.Kind == kind && m.LocalId == local)?.ExternalId;

        var missing = sale.Lines.Select(l => l.TaxId).Distinct().Where(t => External(FiscalMappingKind.Tax, t) is null)
            .Select(t => $"impuesto {t}")
            .Concat(sale.Payments.Select(p => p.PaymentMethodId).Distinct().Where(p => External(FiscalMappingKind.PaymentMethod, p) is null)
                .Select(p => $"medio de pago {p}"))
            .ToList();
        if (missing.Count > 0)
        {
            // Configuración incompleta: no es un rechazo de la DIAN; se corrige y se reintenta a mano.
            doc.Reject($"Falta emparejar con Siigo: {string.Join(", ", missing)}.");
            _db.FiscalSubmissionAttempts.Add(FiscalSubmissionAttempt.Record(doc, "create", "rejected", null, doc.Errors, Now));
            await _db.SaveChangesAsync(ct);
            return null;
        }

        foreach (var line in sale.Lines.DistinctBy(l => l.ProductId).Where(l => External(FiscalMappingKind.Product, l.ProductId) is null))
        {
            var (ok, error) = await _siigo.EnsureProductAsync(credentials, line.Sku, line.Name, settings.ProductAccountGroupId!.Value,
                [long.Parse(External(FiscalMappingKind.Tax, line.TaxId)!)], ct);
            if (!ok)
            {
                await FailTemporarilyAsync(doc, $"No se pudo crear el producto {line.Sku} en Siigo: {error}", ct);
                throw new InvalidOperationException("Producto no creado en Siigo; se reintenta.");
            }
            var mapping = FiscalMapping.Create(doc.CompanyId, FiscalMappingKind.Product, line.ProductId, line.Sku);
            _db.FiscalMappings.Add(mapping);
            mappings.Add(mapping);
        }
        await _db.SaveChangesAsync(ct);

        return new SiigoInvoice(settings.InvoiceDocumentTypeId!.Value, DateOnly.FromDateTime(ColombiaToday()), doc.CustomerIdentification,
            settings.SellerId!.Value,
            sale.Lines.Select(l => new SiigoInvoiceItem(l.Sku, l.Name, l.Quantity, l.UnitPriceWithTax,
                [long.Parse(External(FiscalMappingKind.Tax, l.TaxId)!)], l.Discount)).ToList(),
            sale.Payments.GroupBy(p => External(FiscalMappingKind.PaymentMethod, p.PaymentMethodId)!)
                .Select(g => new SiigoPayment(long.Parse(g.Key), g.Sum(p => p.Amount))).ToList(),
            $"Venta {sale.SaleId}");
    }

    private async Task ApplyAsync(FiscalDocument doc, FiscalSettings settings, SiigoCredentials credentials, SiigoInvoiceResult result, CancellationToken ct)
    {
        switch (result.Outcome)
        {
            case SiigoOutcome.Ok:
                if (result.Id is not null) doc.RecordCreated(result.Id, result.Prefix, result.Number);
                if (string.Equals(result.StampStatus, "Accepted", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(result.Cufe))
                {
                    doc.Accept(result.Cufe, Now, settings.RetentionYears);
                    await _db.SaveChangesAsync(ct);
                    return;
                }
                if (string.Equals(result.StampStatus, "Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    var errors = result.Errors ?? (result.Id is null || doc.Kind != FiscalDocumentKind.Invoice ? null
                        : await _siigo.StampErrorsAsync(credentials, result.Id, ct));
                    doc.Reject(errors ?? "La DIAN rechazó el documento (sin detalle).");
                    await _db.SaveChangesAsync(ct);
                    _logger.LogWarning("Factura {DocumentId} rechazada por la DIAN", doc.Id);
                    return;
                }
                // Borrador o sin CUFE todavía: queda desconocido y se vuelve a consultar.
                doc.MarkUnknown($"Siigo respondió sin resultado DIAN (estado {result.StampStatus ?? "vacío"}).");
                await _db.SaveChangesAsync(ct);
                throw new InvalidOperationException("Factura sin resultado DIAN; se consulta de nuevo.");
            case SiigoOutcome.Invalid:
                doc.Reject(result.Errors ?? $"Siigo rechazó la petición (HTTP {result.HttpStatus}).");
                await _db.SaveChangesAsync(ct);
                return;
            default:
                doc.MarkUnknown(result.Errors ?? "Sin respuesta de Siigo.");
                await _db.SaveChangesAsync(ct);
                throw new InvalidOperationException("Resultado desconocido; se reintenta con la misma clave.");
        }
    }

    private void Record(FiscalDocument doc, string operation, SiigoInvoiceResult r) =>
        _db.FiscalSubmissionAttempts.Add(FiscalSubmissionAttempt.Record(doc, operation, r.Outcome switch
        {
            SiigoOutcome.Ok => r.StampStatus?.ToLowerInvariant() ?? "draft",
            SiigoOutcome.Invalid => "rejected",
            _ => "unknown",
        }, r.HttpStatus, r.Errors, Now));

    private async Task FailTemporarilyAsync(FiscalDocument doc, string detail, CancellationToken ct)
    {
        doc.MarkTemporaryError(detail);
        _db.FiscalSubmissionAttempts.Add(FiscalSubmissionAttempt.Record(doc, "create", "error", null, detail, Now));
        await _db.SaveChangesAsync(ct);
    }

    private static bool IsDraft(string? status) => status is null || string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase);

    /// <summary>Siigo no acepta fechas anteriores a hoy: se usa la fecha de Colombia (UTC−5, sin horario de verano).</summary>
    private DateTime ColombiaToday() => Now.AddHours(-5).Date;
}
