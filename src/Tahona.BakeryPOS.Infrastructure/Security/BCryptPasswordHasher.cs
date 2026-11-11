using Tahona.BakeryPOS.Application.Common.Abstractions;

namespace Tahona.BakeryPOS.Infrastructure.Security;

/// <summary>BCrypt with work factor 11 for passwords and PINs.</summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    // Hash of a random value, only to spend the time of a verification when the user does not exist.
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), WorkFactor);

    public string Hash(string secret) => BCrypt.Net.BCrypt.HashPassword(secret, WorkFactor);

    public bool Verify(string secret, string hash) => BCrypt.Net.BCrypt.Verify(secret, hash);

    public void VerifyDummy(string secret) => BCrypt.Net.BCrypt.Verify(secret, DummyHash);
}
