namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Cifra y descifra secretos guardados en la base (llaves de Bold, credenciales de Siigo). Nunca se
/// registran en logs ni se devuelven por la API.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
