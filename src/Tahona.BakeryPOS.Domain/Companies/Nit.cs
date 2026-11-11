using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.Domain.Companies;

/// <summary>
/// Colombian NIT with its check digit (DV), computed with the DIAN's modulus-11 algorithm:
/// each digit, from right to left, is multiplied by 3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47,
/// 53, 59, 67, 71; DV = remainder (sum mod 11) if it is 0 or 1, otherwise 11 − remainder.
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

    /// <summary>Validates number and DV; the number arrives without dots or hyphen.</summary>
    public static Nit Create(string number, int checkDigit)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 6 or > MaxDigits)
            throw new DomainException("INVALID_NIT", "El NIT debe tener entre 6 y 15 dígitos.");
        if (ComputeCheckDigit(digits) != checkDigit)
            throw new DomainException("INVALID_NIT", "El dígito de verificación no corresponde al NIT.");
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
