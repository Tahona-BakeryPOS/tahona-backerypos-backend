using System.Security.Cryptography;
using System.Text;

namespace Tahona.BakeryPOS.Application.Common.Security;

/// <summary>
/// Secretos aleatorios (refresh tokens, tokens de dispositivo, códigos de vinculación, contraseñas de
/// un solo uso). Los que la base guarda se guardan como SHA-256: son aleatorios y largos, así que no
/// necesitan un hash lento como las contraseñas.
/// </summary>
public static class SecretTokens
{
    // Sin 0/O, 1/I/L: se leen y dictan sin confusión.
    private const string ReadableAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const string PasswordAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    /// <summary>Token opaco de 256 bits en base64url.</summary>
    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string NewPairingCode() => RandomNumberGenerator.GetString(ReadableAlphabet, 8);

    /// <summary>Contraseña de un solo uso: 14 caracteres con letras y números.</summary>
    public static string NewTemporaryPassword() => RandomNumberGenerator.GetString(PasswordAlphabet, 14);

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string NormalizePairingCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
