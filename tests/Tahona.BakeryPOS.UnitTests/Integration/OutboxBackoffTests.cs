using Tahona.BakeryPOS.Infrastructure.Integration;

namespace Tahona.BakeryPOS.UnitTests.Integration;

public class OutboxBackoffTests
{
    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(20, 3600)] // tope de 1 hora
    public void Backoff_is_exponential_with_a_cap(int attempt, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), OutboxProcessor.Backoff(attempt));
    }
}
