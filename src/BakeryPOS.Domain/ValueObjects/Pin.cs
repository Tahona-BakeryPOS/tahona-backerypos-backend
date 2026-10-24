namespace BakeryPOS.Domain.ValueObjects;

/// <summary>
/// Value object for the cashier's 4-6 digit PIN.
/// The actual stored value is a BCrypt hash.
/// </summary>
public sealed record Pin
{
    public string HashedValue { get; }

    private Pin(string hashedValue)
    {
        HashedValue = hashedValue;
    }

    public static Pin FromHash(string hash) => new(hash);

    public static Pin Create(string plainPin)
    {
        if (string.IsNullOrWhiteSpace(plainPin) || plainPin.Length < 4 || plainPin.Length > 6)
            throw new ArgumentException("El PIN debe tener entre 4 y 6 dígitos.");

        if (!plainPin.All(char.IsDigit))
            throw new ArgumentException("El PIN solo puede contener dígitos.");

        var hash = BCrypt.Net.BCrypt.HashPassword(plainPin);
        return new Pin(hash);
    }

    public bool Verify(string plainPin) =>
        BCrypt.Net.BCrypt.Verify(plainPin, HashedValue);
}
