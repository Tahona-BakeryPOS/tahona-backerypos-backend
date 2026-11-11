using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Payments.Gateway;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Tahona.BakeryPOS.Infrastructure.Payments;

/// <summary>Bold configuration (the "Bold" section). The base URL is the same in test and production.</summary>
public sealed class BoldOptions
{
    public const string Section = "Bold";

    public string BaseUrl { get; set; } = "https://integrations.api.bold.co";

    /// <summary>Maximum time to create the charge; if it runs out, the attempt is left "unknown".</summary>
    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>
/// Client of Bold's card-terminal API (docs/payment-integration.md §11). Keys are never logged.
/// The amount names follow the documentation's example (<c>total_amount</c>, <c>tip_amount</c>); the
/// table on the same page says <c>total</c>/<c>tip</c>: if the sandbox answers AP005/AP006, it is changed here.
/// </summary>
internal sealed class BoldGateway : IBoldGateway
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly HttpClient _http;
    private readonly ILogger<BoldGateway> _logger;

    public BoldGateway(HttpClient http, ILogger<BoldGateway> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<BoldCheckoutResult> CreateCheckoutAsync(string identityKey, BoldCheckoutRequest request, CancellationToken ct)
    {
        var body = new
        {
            amount = new
            {
                currency = "COP",
                total_amount = request.Total,
                taxes = request.Taxes.Select(t => new { type = t.Type, @base = t.Base, value = t.Value }).ToList(),
                tip_amount = 0m,
            },
            user_email = request.SellerEmail,
            payment_method = request.PaymentMethod,
            terminal_model = request.TerminalModel,
            terminal_serial = request.TerminalSerial,
            reference = request.Reference,
            description = request.Description,
        };

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "payments/app-checkout") { Content = JsonContent.Create(body, options: Json) };
            Authorize(message, identityKey);
            using var response = await _http.SendAsync(message, ct);
            var envelope = await ReadAsync(response, ct);

            if (response.IsSuccessStatusCode)
                return new BoldCheckoutResult(BoldCheckoutOutcome.Sent, envelope?.Payload?.GetPropertyOrNull("integration_id"), null, null);

            var (code, text) = envelope?.FirstError() ?? ($"HTTP{(int)response.StatusCode}", response.ReasonPhrase ?? "Error");
            _logger.LogWarning("Bold rechazó el cobro {Reference}: {Status} {Code} {Message}", request.Reference, (int)response.StatusCode, code, text);
            // A 5xx (AP001 included) does not guarantee the charge was not created: it is treated as unknown.
            return (int)response.StatusCode >= 500
                ? new BoldCheckoutResult(BoldCheckoutOutcome.Unknown, null, code, text)
                : new BoldCheckoutResult(BoldCheckoutOutcome.Rejected, null, code, text);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(e, "Sin respuesta de Bold al crear el cobro {Reference}", request.Reference);
            return new BoldCheckoutResult(BoldCheckoutOutcome.Unknown, null, null, "Sin respuesta de Bold.");
        }
    }

    public async Task<IReadOnlyList<BoldTerminal>> ListTerminalsAsync(string identityKey, CancellationToken ct)
    {
        var payload = await GetPayloadAsync("payments/binded-terminals", identityKey, ct);
        if (payload is not { } p || !p.TryGetProperty("available_terminals", out var list)) return [];
        return list.EnumerateArray().Select(t => new BoldTerminal(t.GetPropertyOrNull("terminal_model") ?? "", t.GetPropertyOrNull("terminal_serial") ?? "",
            t.GetPropertyOrNull("status") ?? "", t.GetPropertyOrNull("name") ?? "")).ToList();
    }

    public async Task<IReadOnlyList<BoldMethodStatus>> ListMethodsAsync(string identityKey, CancellationToken ct)
    {
        var payload = await GetPayloadAsync("payments/payment-methods", identityKey, ct);
        if (payload is not { } p || !p.TryGetProperty("payment_methods", out var list)) return [];
        return list.EnumerateArray().Select(m => new BoldMethodStatus(m.GetPropertyOrNull("name") ?? "",
            m.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True)).ToList();
    }

    private async Task<JsonElement?> GetPayloadAsync(string path, string identityKey, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        Authorize(message, identityKey);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new GatewayUnavailableException("Bold no respondió.");
        }
        using var _ = response;
        var envelope = await ReadAsync(response, ct);
        if (!response.IsSuccessStatusCode)
        {
            var (code, text) = envelope?.FirstError() ?? ($"HTTP{(int)response.StatusCode}", response.ReasonPhrase ?? "Error");
            throw new GatewayUnavailableException($"Bold respondió {(int)response.StatusCode} {code}: {text}");
        }
        return envelope?.Payload;
    }

    /// <summary>Encabezado documentado: <c>Authorization: x-api-key &lt;llave_de_identidad&gt;</c>.</summary>
    private static void Authorize(HttpRequestMessage message, string identityKey) =>
        message.Headers.TryAddWithoutValidation("Authorization", $"x-api-key {identityKey}");

    private static async Task<Envelope?> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement.Clone();
            return new Envelope(root.TryGetProperty("payload", out var p) ? p : null, root.TryGetProperty("errors", out var e) ? e : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Envelope(JsonElement? Payload, JsonElement? Errors)
    {
        public (string Code, string Message)? FirstError()
        {
            if (Errors is not { ValueKind: JsonValueKind.Array } errors || errors.GetArrayLength() == 0) return null;
            var first = errors[0];
            return first.ValueKind == JsonValueKind.Object
                ? (first.GetPropertyOrNull("code") ?? "ERROR", first.GetPropertyOrNull("message") ?? first.ToString())
                : ("ERROR", first.ToString());
        }
    }
}

internal static class JsonElementExtensions
{
    public static string? GetPropertyOrNull(this JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e ? e.GetPropertyOrNull(name) : null;

    public static string? GetPropertyOrNull(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : null;
}

/// <summary>Encrypts the gateway keys with ASP.NET Core Data Protection (its own purpose).</summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("BakeryPOS.Payments.GatewayKeys.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
