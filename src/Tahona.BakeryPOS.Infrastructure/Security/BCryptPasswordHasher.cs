using Tahona.BakeryPOS.Application.Common.Abstractions;

namespace Tahona.BakeryPOS.Infrastructure.Security;

/// <summary>BCrypt con factor de trabajo 11 para contraseñas y PIN.</summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    // Hash de un valor aleatorio, solo para gastar el tiempo de una verificación cuando el usuario no existe.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), WorkFactor);

    public string Hash(string secret) => BCrypt.Net.BCrypt.HashPassword(secret, WorkFactor);

    public bool Verify(string secret, string hash) => BCrypt.Net.BCrypt.Verify(secret, hash);

    public void VerifyDummy(string secret) => BCrypt.Net.BCrypt.Verify(secret, DummyHash);
}
