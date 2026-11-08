using Tahona.BakeryPOS.Domain.Exceptions;
using Tahona.BakeryPOS.Domain.Inventory;

namespace Tahona.BakeryPOS.UnitTests.Inventory;

public class StockLedgerTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Location = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();

    private static StockMovement Move(decimal qty, StockMovementKind kind, Guid? authorizedBy = null) =>
        StockMovement.Create(Company, Location, Product, qty, kind, StockSources.Adjustment, Guid.NewGuid(), Guid.Empty, Now, Guid.NewGuid(),
            authorizedBy: authorizedBy);

    [Fact]
    public void Balance_is_the_sum_of_its_movements()
    {
        var balance = StockBalance.Empty(Company, Location, Product);
        balance.Apply(Move(10m, StockMovementKind.Initial), allowNegative: false);
        balance.Apply(Move(-3m, StockMovementKind.Sale), allowNegative: false);
        balance.Apply(Move(2.5m, StockMovementKind.AdjustmentIn), allowNegative: false);

        Assert.Equal(9.5m, balance.OnHand);
    }

    [Fact]
    public void Block_policy_rejects_going_negative_without_authorization()
    {
        var balance = StockBalance.Empty(Company, Location, Product);
        balance.Apply(Move(1m, StockMovementKind.Initial), allowNegative: false);

        var e = Assert.Throws<DomainException>(() => balance.Apply(Move(-2m, StockMovementKind.Sale), allowNegative: false));
        Assert.Equal("EXISTENCIAS_INSUFICIENTES", e.ErrorCode);
        Assert.Equal(1m, balance.OnHand);
    }

    [Fact]
    public void Block_policy_allows_negative_with_an_authorizer()
    {
        var balance = StockBalance.Empty(Company, Location, Product);
        balance.Apply(Move(-2m, StockMovementKind.Sale, authorizedBy: Guid.NewGuid()), allowNegative: false);

        Assert.Equal(-2m, balance.OnHand);
    }

    [Fact]
    public void Allow_policy_goes_negative()
    {
        var balance = StockBalance.Empty(Company, Location, Product);
        balance.Apply(Move(-5m, StockMovementKind.Sale), allowNegative: true);

        Assert.Equal(-5m, balance.OnHand);
    }

    [Fact]
    public void Movement_sign_must_match_its_kind()
    {
        Assert.Throws<DomainException>(() => Move(3m, StockMovementKind.Sale));
        Assert.Throws<DomainException>(() => Move(-3m, StockMovementKind.Initial));
        Assert.Throws<DomainException>(() => Move(0m, StockMovementKind.AdjustmentIn));
    }

    [Fact]
    public void Reason_direction_limits_the_sign()
    {
        var merma = AdjustmentReason.Create(Company, "MERMA", "Merma", AdjustmentDirection.Out);
        var conteo = AdjustmentReason.Create(Company, "CONTEO", "Conteo", AdjustmentDirection.Both);

        Assert.True(merma.Allows(-1m));
        Assert.False(merma.Allows(1m));
        Assert.True(conteo.Allows(1m) && conteo.Allows(-1m));
        Assert.False(conteo.Allows(0m));
    }
}
