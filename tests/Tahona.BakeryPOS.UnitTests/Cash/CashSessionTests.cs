using Tahona.BakeryPOS.Domain.Cash;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.UnitTests.Cash;

public class CashSessionTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid User = Guid.NewGuid();

    private static CashSession NewSession(decimal opening = 100_000m) =>
        CashSession.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), User, opening, Now);

    [Fact]
    public void Expected_is_opening_plus_net_cash_sales_minus_refunds_plus_incomes_minus_expenses()
    {
        var s = NewSession();
        var reason = Guid.NewGuid();
        var movements = new[]
        {
            CashMovement.Create(s, CashMovementKind.Opening, 100_000m, Now, User),
            CashMovement.Create(s, CashMovementKind.SaleCash, 50_000m, Now, User),
            CashMovement.Create(s, CashMovementKind.ChangeGiven, 2_000m, Now, User),
            CashMovement.Create(s, CashMovementKind.RefundCash, 5_000m, Now, User),
            CashMovement.Create(s, CashMovementKind.Income, 10_000m, Now, User, reason),
            CashMovement.Create(s, CashMovementKind.Expense, 30_000m, Now, User, reason),
        };

        Assert.Equal(123_000m, CashMovement.Expected(movements));
    }

    [Fact]
    public void Closing_stores_expected_counted_and_difference()
    {
        var s = NewSession();
        s.Close(expected: 123_000m, counted: 120_000m, User, Now.AddHours(8), "faltante");

        Assert.False(s.IsOpen);
        Assert.Equal(-3_000m, s.Difference);
        Assert.Equal("faltante", s.Notes);
    }

    [Fact]
    public void A_closed_session_accepts_no_movements_nor_a_second_close()
    {
        var s = NewSession();
        s.Close(100_000m, 100_000m, User, Now, null);

        var e = Assert.Throws<DomainException>(() => CashMovement.Create(s, CashMovementKind.Income, 1m, Now, User, Guid.NewGuid()));
        Assert.Equal("CAJA_CERRADA", e.ErrorCode);
        Assert.Throws<DomainException>(() => s.Close(1m, 1m, User, Now, null));
    }

    [Fact]
    public void Amounts_must_be_positive_with_two_decimals_and_income_needs_a_reason()
    {
        var s = NewSession();
        Assert.Throws<DomainException>(() => NewSession(-1m));
        Assert.Throws<DomainException>(() => CashMovement.Create(s, CashMovementKind.SaleCash, 0m, Now, User));
        Assert.Throws<DomainException>(() => CashMovement.Create(s, CashMovementKind.SaleCash, 1.005m, Now, User));
        Assert.Throws<DomainException>(() => CashMovement.Create(s, CashMovementKind.Income, 1m, Now, User));
    }
}
