namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Encrypts and decrypts secrets stored in the database (Bold keys, Siigo credentials). They are never
/// written to logs or returned by the API.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
