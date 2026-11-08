using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Companies;

/// <summary>
/// NIT colombiano con su dígito de verificación (DV), calculado con el algoritmo módulo 11 de la DIAN:
/// cada dígito, de derecha a izquierda, se multiplica por 3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47,
/// 53, 59, 67, 71; DV = residuo (suma mod 11) si es 0 o 1, si no 11 − residuo.
/// </summary>
public sealed record Nit
{
    private static readonly int[] Weights = [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];
    private const int MaxDigits = 15;

    public string Number { get; }
    public int CheckDigit { get; }

    private Nit(string number, int checkDigit)
    {
        Number = number;
        CheckDigit = checkDigit;
    }

    /// <summary>Valida número y DV; el número llega sin puntos ni guion.</summary>
    public static Nit Create(string number, int checkDigit)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 6 or > MaxDigits)
            throw new DomainException("NIT_INVALIDO", "El NIT debe tener entre 6 y 15 dígitos.");
        if (ComputeCheckDigit(digits) != checkDigit)
            throw new DomainException("NIT_INVALIDO", "El dígito de verificación no corresponde al NIT.");
        return new Nit(digits, checkDigit);
    }

    public static int ComputeCheckDigit(string digits)
    {
        var sum = digits.Reverse().Select((c, i) => (c - '0') * Weights[i]).Sum();
        var remainder = sum % 11;
        return remainder > 1 ? 11 - remainder : remainder;
    }

    public override string ToString() => $"{Number}-{CheckDigit}";
}
