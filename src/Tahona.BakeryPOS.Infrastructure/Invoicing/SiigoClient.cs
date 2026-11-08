using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tahona.BakeryPOS.Application.Invoicing.Siigo;
using Tahona.BakeryPOS.Application.Payments.Gateway;
using Microsoft.Extensions.Logging;

namespace Tahona.BakeryPOS.Infrastructure.Invoicing;

/// <summary>Configuración de Siigo (sección "Siigo").</summary>
public sealed class SiigoOptions
{
    public const string Section = "Siigo";

    public string BaseUrl { get; set; } = "https://api.siigo.com";

    /// <summary>Nombre de la aplicación en el encabezado Partner-Id (el mismo para todas las empresas).</summary>
    public string PartnerId { get; set; } = "BakeryPOS";

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Cliente de la API de Siigo (docs/electronic-invoicing.md §9): token por empresa (24 h, se renueva antes),
/// encabezados <c>Authorization: Bearer</c> y <c>Partner-Id</c>. Las credenciales y el token nunca se registran.
/// No reintenta las creaciones: el reintento lo hace el outbox con la misma Idempotency-Key.
/// </summary>
internal sealed class SiigoClient : ISiigoClient
{
    private static readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresAt)> Tokens = new();

    private readonly HttpClient _http;
    private readonly SiigoOptions _options;
    private readonly ILogger<SiigoClient> _logger;

    public SiigoClient(HttpClient http, SiigoOptions options, ILogger<SiigoClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public Task<IReadOnlyList<SiigoCatalogItem>> DocumentTypesAsync(SiigoCredentials c, string type, CancellationToken ct) =>
        CatalogAsync(c, $"v1/document-types?type={Uri.EscapeDataString(type)}", e => new SiigoCatalogItem(
            Str(e, "id") ?? "", Str(e, "name") ?? "", Str(e, "code"), $"{Str(e, "prefix")} · {Str(e, "electronic_type")}", Bool(e, "active")), ct);

    public Task<IReadOnlyList<SiigoCatalogItem>> UsersAsync(SiigoCredentials c, CancellationToken ct) =>
        CatalogAsync(c, "v1/users", e => new SiigoCatalogItem(Str(e, "id") ?? "",
            $"{Str(e, "first_name")} {Str(e, "last_name")}".Trim(), Str(e, "username"), Str(e, "email"), Bool(e, "active")), ct);

    public Task<IReadOnlyList<SiigoCatalogItem>> TaxesAsync(SiigoCredentials c, CancellationToken ct) =>
        CatalogAsync(c, "v1/taxes", e => new SiigoCatalogItem(Str(e, "id") ?? "", Str(e, "name") ?? "", Str(e, "type"),
            Str(e, "percentage"), Bool(e, "active")), ct);

    public Task<IReadOnlyList<SiigoCatalogItem>> PaymentTypesAsync(SiigoCredentials c, CancellationToken ct) =>
        CatalogAsync(c, "v1/payment-types?document_type=FV", e => new SiigoCatalogItem(Str(e, "id") ?? "", Str(e, "name") ?? "", Str(e, "type"),
            null, Bool(e, "active")), ct);

    public async Task<(bool Ok, string? Error)> EnsureProductAsync(SiigoCredentials c, string code, string name, long accountGroupId,
        IReadOnlyList<long> taxIds, CancellationToken ct)
    {
        var body = new { code, name, account_group = accountGroupId, type = "Product", taxes = taxIds.Select(id => new { id }).ToList() };
        using var response = await SendAsync(c, HttpMethod.Post, "v1/products", body, null, ct);
        if (response.IsSuccessStatusCode)
            return (true, null);
        var errors = await ErrorsAsync(response, ct);
        // Ya existía con ese código: sirve igual (por verificar el código exacto de error de Siigo).
        if (response.StatusCode == HttpStatusCode.BadRequest
            && (errors.Contains("already", StringComparison.OrdinalIgnoreCase) || errors.Contains("existe", StringComparison.OrdinalIgnoreCase)))
            return (true, null);
        return (false, errors);
    }

    public async Task<SiigoInvoiceResult> CreateInvoiceAsync(SiigoCredentials c, SiigoInvoice invoice, string idempotencyKey, CancellationToken ct)
    {
        var body = new
        {
            document = new { id = invoice.DocumentTypeId },
            date = invoice.Date.ToString("yyyy-MM-dd"),
            customer = new { identification = invoice.CustomerIdentification, branch_office = 0 },
            seller = invoice.SellerId,
            stamp = new { send = true },
            mail = new { send = false },
            observations = invoice.Observations,
            items = invoice.Items.Select(i => new
            {
                code = i.Code, description = i.Description, quantity = i.Quantity, taxed_price = i.TaxedPrice,
                discount = i.Discount > 0 ? i.Discount : (decimal?)null,
                taxes = i.TaxIds.Select(id => new { id }).ToList(),
            }).ToList(),
            payments = invoice.Payments.Select(p => new { id = p.Id, value = p.Value }).ToList(),
        };
        return await InvoiceCallAsync(c, HttpMethod.Post, "v1/invoices", body, idempotencyKey, ct);
    }

    public Task<SiigoInvoiceResult> GetInvoiceAsync(SiigoCredentials c, string id, CancellationToken ct) =>
        InvoiceCallAsync(c, HttpMethod.Get, $"v1/invoices/{Uri.EscapeDataString(id)}", null, null, ct);

    public Task<SiigoInvoiceResult> StampInvoiceAsync(SiigoCredentials c, string id, CancellationToken ct) =>
        InvoiceCallAsync(c, HttpMethod.Post, $"v1/invoices/{Uri.EscapeDataString(id)}/stamp", new { }, null, ct);

    public async Task<string?> StampErrorsAsync(SiigoCredentials c, string id, CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync(c, HttpMethod.Get, $"v1/invoices/{Uri.EscapeDataString(id)}/stamp/errors", null, null, ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<SiigoInvoiceResult> CreateCreditNoteAsync(SiigoCredentials c, SiigoCreditNote note, string idempotencyKey, CancellationToken ct)
    {
        // Campos según developers.siigo.com (credit-note/1-create-credit-note). taxed_price aparece en el ejemplo del
        // spec y no en la guía de Colombia: por verificar en habilitación, igual que el descuento.
        var body = new
        {
            document = new { id = note.DocumentTypeId },
            date = note.Date.ToString("yyyy-MM-dd"),
            invoice = note.InvoiceId,
            reason = note.Reason,
            stamp = new { send = true },
            mail = new { send = false },
            observations = note.Observations,
            items = note.Items.Select(i => new
            {
                code = i.Code, description = i.Description, quantity = i.Quantity, taxed_price = i.TaxedPrice,
                discount = i.Discount > 0 ? i.Discount : (decimal?)null,
                taxes = i.TaxIds.Select(id => new { id }).ToList(),
            }).ToList(),
            payments = note.Payments.Select(p => new { id = p.Id, value = p.Value }).ToList(),
        };
        return await InvoiceCallAsync(c, HttpMethod.Post, "v1/credit-notes", body, idempotencyKey, ct);
    }

    public Task<SiigoInvoiceResult> GetCreditNoteAsync(SiigoCredentials c, string id, CancellationToken ct) =>
        InvoiceCallAsync(c, HttpMethod.Get, $"v1/credit-notes/{Uri.EscapeDataString(id)}", null, null, ct);

    public Task<byte[]?> InvoicePdfAsync(SiigoCredentials c, string id, CancellationToken ct) => PdfAsync(c, $"v1/invoices/{Uri.EscapeDataString(id)}/pdf", ct);

    public Task<byte[]?> CreditNotePdfAsync(SiigoCredentials c, string id, CancellationToken ct) =>
        PdfAsync(c, $"v1/credit-notes/{Uri.EscapeDataString(id)}/pdf", ct);

    private async Task<byte[]?> PdfAsync(SiigoCredentials c, string path, CancellationToken ct)
    {
        using var response = await SendAsync(c, HttpMethod.Get, path, null, null, ct);
        if (!response.IsSuccessStatusCode)
            return null;
        if (response.Content.Headers.ContentType?.MediaType == "application/pdf")
            return await response.Content.ReadAsByteArrayAsync(ct);
        // Formato por verificar: si viene como JSON, se toma el campo base64.
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return Str(doc.RootElement, "base64") is { } b64 ? Convert.FromBase64String(b64) : null;
    }

    private async Task<SiigoInvoiceResult> InvoiceCallAsync(SiigoCredentials c, HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync(c, method, path, body, idempotencyKey, ct);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                var stamp = root.TryGetProperty("stamp", out var s) ? s : default;
                return new SiigoInvoiceResult(SiigoOutcome.Ok, status, Str(root, "id"), Str(root, "prefix"),
                    long.TryParse(Str(root, "number"), out var n) ? n : null,
                    stamp.ValueKind == JsonValueKind.Object ? Str(stamp, "status") : null,
                    stamp.ValueKind == JsonValueKind.Object ? Str(stamp, "cufe") ?? Str(stamp, "cude") : null,
                    stamp.ValueKind == JsonValueKind.Object ? NonEmpty(Str(stamp, "errors")) : null);
            }
            var errors = await ErrorsAsync(response, ct);
            _logger.LogWarning("Siigo respondió {Status} en {Method} {Path}", status, method, path);
            return status >= 500 || response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Unauthorized
                ? new SiigoInvoiceResult(SiigoOutcome.Unknown, status, null, null, null, null, null, errors)
                : new SiigoInvoiceResult(SiigoOutcome.Invalid, status, null, null, null, null, null, errors);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Sin respuesta de Siigo en {Method} {Path}", method, path);
            return new SiigoInvoiceResult(SiigoOutcome.Unknown, null, null, null, null, null, null, "Sin respuesta de Siigo.");
        }
    }

    private async Task<IReadOnlyList<SiigoCatalogItem>> CatalogAsync(SiigoCredentials c, string path, Func<JsonElement, SiigoCatalogItem> map, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await SendAsync(c, HttpMethod.Get, path, null, null, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new GatewayUnavailableException("Siigo no respondió.");
        }
        using var _ = response;
        if (!response.IsSuccessStatusCode)
            throw new GatewayUnavailableException($"Siigo respondió {(int)response.StatusCode}: {await ErrorsAsync(response, ct)}");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        // Algunos catálogos vienen paginados ({ results: [...] }) y otros como arreglo.
        var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement
            : doc.RootElement.TryGetProperty("results", out var r) ? r : default;
        return list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(map).ToList() : [];
    }

    private async Task<HttpResponseMessage> SendAsync(SiigoCredentials c, HttpMethod method, string path, object? body, string? idempotencyKey, CancellationToken ct)
    {
        var token = await TokenAsync(c, ct);
        var message = new HttpRequestMessage(method, path);
        message.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        message.Headers.TryAddWithoutValidation("Partner-Id", _options.PartnerId);
        if (idempotencyKey is not null) message.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        if (body is not null) message.Content = JsonContent.Create(body);
        var response = await _http.SendAsync(message, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            Tokens.TryRemove(Key(c), out _);
        return response;
    }

    /// <summary><c>POST /auth</c> con usuario y llave de acceso; el token dura 24 h y se renueva una hora antes.</summary>
    private async Task<string> TokenAsync(SiigoCredentials c, CancellationToken ct)
    {
        if (Tokens.TryGetValue(Key(c), out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            return cached.Token;
        using var message = new HttpRequestMessage(HttpMethod.Post, "auth") { Content = JsonContent.Create(new { username = c.Username, access_key = c.AccessKey }) };
        message.Headers.TryAddWithoutValidation("Partner-Id", _options.PartnerId);
        using var response = await _http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
            throw new GatewayUnavailableException($"Siigo rechazó las credenciales (HTTP {(int)response.StatusCode}). Revisa el usuario y la llave de acceso.");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = Str(doc.RootElement, "access_token") ?? throw new GatewayUnavailableException("Siigo no devolvió el token.");
        Tokens[Key(c)] = (token, DateTime.UtcNow.AddHours(23));
        return token;
    }

    private static string Key(SiigoCredentials c) => $"{c.CompanyId:N}:{c.Username}";

    private static async Task<string> ErrorsAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        return string.IsNullOrWhiteSpace(text) ? $"HTTP {(int)response.StatusCode}" : text.Length > 1000 ? text[..1000] : text;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;

    private static bool Bool(JsonElement e, string name) =>
        !(e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.False);

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) || s is "[]" or "\"\"" ? null : s;
}
