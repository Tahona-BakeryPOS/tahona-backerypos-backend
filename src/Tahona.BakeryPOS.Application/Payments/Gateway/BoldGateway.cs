using System.Security.Cryptography;
using System.Text;

namespace Tahona.BakeryPOS.Application.Payments.Gateway;

/// <summary>A tax line of the charge (Bold: <c>IVA_19</c>, <c>IVA_5</c>, <c>IAC_8</c>, or <c>VAT</c>/<c>CONSUMPTION</c> by value).</summary>
public sealed record BoldTax(string Type, decimal? Base, decimal? Value);

/// <param name="Reference">The attempt's id: Bold returns it in the webhook's <c>data.metadata.reference</c>.</param>
public sealed record BoldCheckoutRequest(decimal Total, IReadOnlyList<BoldTax> Taxes, string PaymentMethod, string TerminalModel,
    string TerminalSerial, string Reference, string SellerEmail, string? Description);

public enum BoldCheckoutOutcome
{
    /// <summary>201: the charge reached the card terminal; the result will come by webhook.</summary>
    Sent,
    /// <summary>4xx/5xx with an AP00x code: the charge was not created.</summary>
    Rejected,
    /// <summary>Timeout or network error: it is unknown whether it was created.</summary>
    Unknown,
}

public sealed record BoldCheckoutResult(BoldCheckoutOutcome Outcome, string? IntegrationId, string? ErrorCode, string? ErrorMessage);

public sealed record BoldTerminal(string Model, string Serial, string Status, string Name);

public sealed record BoldMethodStatus(string Name, bool Enabled);

/// <summary>
/// Client of Bold's card-terminal API (payment-integration.md §11). It is always called outside a
/// SQL transaction. It does not retry the creation: a repeated charge would reach the card terminal twice.
/// </summary>
public interface IBoldGateway
{
    Task<BoldCheckoutResult> CreateCheckoutAsync(string identityKey, BoldCheckoutRequest request, CancellationToken ct);

    Task<IReadOnlyList<BoldTerminal>> ListTerminalsAsync(string identityKey, CancellationToken ct);

    Task<IReadOnlyList<BoldMethodStatus>> ListMethodsAsync(string identityKey, CancellationToken ct);
}

/// <summary>The gateway did not respond or responded with an error on a query (never on a charge): 503.</summary>
public sealed class GatewayUnavailableException(string message) : Exception(message);

/// <summary>Bold webhook signature: hexadecimal HMAC-SHA256 of the Base64 body, with the secret key.</summary>
public static class BoldSignature
{
    public const string Header = "x-bold-signature";

    /// <param name="secretKey">Secret key; in tests Bold signs with an empty key.</param>
    public static string Compute(string rawBody, string? secretKey)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawBody));
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey ?? string.Empty), Encoding.UTF8.GetBytes(base64));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Constant-time comparison.</summary>
    public static bool IsValid(string rawBody, string? secretKey, string? received)
    {
        if (string.IsNullOrWhiteSpace(received))
            return false;
        var expected = Encoding.ASCII.GetBytes(Compute(rawBody, secretKey));
        var actual = Encoding.ASCII.GetBytes(received.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
