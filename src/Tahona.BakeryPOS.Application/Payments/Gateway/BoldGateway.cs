using System.Security.Cryptography;
using System.Text;

namespace Tahona.BakeryPOS.Application.Payments.Gateway;

/// <summary>Una línea de impuesto del cobro (Bold: <c>IVA_19</c>, <c>IVA_5</c>, <c>IAC_8</c>, o <c>VAT</c>/<c>CONSUMPTION</c> por valor).</summary>
public sealed record BoldTax(string Type, decimal? Base, decimal? Value);

/// <param name="Reference">Id del intento: Bold lo devuelve en <c>data.metadata.reference</c> del webhook.</param>
public sealed record BoldCheckoutRequest(decimal Total, IReadOnlyList<BoldTax> Taxes, string PaymentMethod, string TerminalModel,
    string TerminalSerial, string Reference, string SellerEmail, string? Description);

public enum BoldCheckoutOutcome
{
    /// <summary>201: el cobro llegó al datáfono; el resultado vendrá por webhook.</summary>
    Sent,
    /// <summary>4xx/5xx con código AP00x: el cobro no se creó.</summary>
    Rejected,
    /// <summary>Timeout o error de red: no se sabe si se creó.</summary>
    Unknown,
}

public sealed record BoldCheckoutResult(BoldCheckoutOutcome Outcome, string? IntegrationId, string? ErrorCode, string? ErrorMessage);

public sealed record BoldTerminal(string Model, string Serial, string Status, string Name);

public sealed record BoldMethodStatus(string Name, bool Enabled);

/// <summary>
/// Cliente de la API de datáfono de Bold (payment-integration.md §11). Se llama siempre fuera de una
/// transacción SQL. No reintenta la creación: un cobro repetido llegaría dos veces al datáfono.
/// </summary>
public interface IBoldGateway
{
    Task<BoldCheckoutResult> CreateCheckoutAsync(string identityKey, BoldCheckoutRequest request, CancellationToken ct);

    Task<IReadOnlyList<BoldTerminal>> ListTerminalsAsync(string identityKey, CancellationToken ct);

    Task<IReadOnlyList<BoldMethodStatus>> ListMethodsAsync(string identityKey, CancellationToken ct);
}

/// <summary>La pasarela no respondió o respondió con error en una consulta (nunca en un cobro): 503.</summary>
public sealed class GatewayUnavailableException(string message) : Exception(message);

/// <summary>Firma del webhook de Bold: HMAC-SHA256 en hexadecimal del cuerpo en Base64, con la llave secreta.</summary>
public static class BoldSignature
{
    public const string Header = "x-bold-signature";

    /// <param name="secretKey">Llave secreta; en pruebas Bold firma con llave vacía.</param>
    public static string Compute(string rawBody, string? secretKey)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawBody));
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey ?? string.Empty), Encoding.UTF8.GetBytes(base64));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Comparación en tiempo constante.</summary>
    public static bool IsValid(string rawBody, string? secretKey, string? received)
    {
        if (string.IsNullOrWhiteSpace(received))
            return false;
        var expected = Encoding.ASCII.GetBytes(Compute(rawBody, secretKey));
        var actual = Encoding.ASCII.GetBytes(received.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
