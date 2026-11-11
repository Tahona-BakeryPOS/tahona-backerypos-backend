using System.Security.Cryptography;
using System.Text;

namespace Tahona.BakeryPOS.Application.Common.Security;

/// <summary>
/// Random secrets (refresh tokens, device tokens, pairing codes, one-time
/// passwords). The ones the database stores are stored as SHA-256: they are random and long, so they
/// do not need a slow hash like passwords.
/// </summary>
public static class SecretTokens
{
    // Without 0/O, 1/I/L: they can be read and dictated without confusion.
    private const string ReadableAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const string PasswordAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    /// <summary>256-bit opaque token in base64url.</summary>
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string NewPairingCode() => RandomNumberGenerator.GetString(ReadableAlphabet, 8);

    /// <summary>One-time password: 14 characters with letters and numbers.</summary>
    public static string NewTemporaryPassword() => RandomNumberGenerator.GetString(PasswordAlphabet, 14);

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string NormalizePairingCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
