namespace BakeryPOS.Domain.ValueObjects;

/// <summary>
/// Value object for monetary amounts. Uses decimal(18,2) precision.
/// </summary>
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = "COP")
    {
        if (amount < 0)
            throw new ArgumentException("El monto no puede ser negativo.", nameof(amount));

        Amount = Math.Round(amount, 2);
        Currency = currency;
    }

    public static Money Zero(string currency = "COP") => new(0, currency);
    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, a.Currency);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount, a.Currency);
    public static Money operator *(Money a, decimal factor) => new(a.Amount * factor, a.Currency);
    public static bool operator >(Money a, Money b) => a.Amount > b.Amount;
    public static bool operator <(Money a, Money b) => a.Amount < b.Amount;
    public static bool operator >=(Money a, Money b) => a.Amount >= b.Amount;
    public static bool operator <=(Money a, Money b) => a.Amount <= b.Amount;
}
