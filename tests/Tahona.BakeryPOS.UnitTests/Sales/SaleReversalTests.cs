using Tahona.BakeryPOS.Domain.Exceptions;
using Tahona.BakeryPOS.Domain.Sales;

namespace Tahona.BakeryPOS.UnitTests.Sales;

public class SaleReversalTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static (Sale Sale, SaleLine Line) ConfirmedSale(decimal quantity = 3, decimal price = 1_000m)
    {
        var sale = Sale.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        var line = sale.AddLine(Guid.NewGuid(), "PAN", "Pan", "UND", quantity, price, true, 0, null, Guid.NewGuid(), 19m);
        sale.Confirm(sale.Total, 1, Now);
        sale.MarkInventoryApplied();
        return (sale, line);
    }

    [Fact]
    public void Void_refunds_everything_and_reverses_inventory()
    {
        var (sale, _) = ConfirmedSale();

        sale.Void("Error de digitación", Guid.NewGuid(), Now);

        Assert.Equal(SaleStatus.Voided, sale.Status);
        Assert.Equal(SalePaymentStatus.Refunded, sale.PaymentStatus);
        Assert.Equal(SaleInventoryStatus.Reversed, sale.InventoryStatus);
        Assert.Equal(sale.Total, sale.RefundedTotal);
    }

    [Fact]
    public void Void_requires_reason_and_no_previous_returns()
    {
        var (sale, _) = ConfirmedSale();
        Assert.Throws<DomainException>(() => sale.Void(" ", Guid.NewGuid(), Now));

        sale.RegisterReturn(1_000m);
        var e = Assert.Throws<DomainException>(() => sale.Void("Cliente desistió", Guid.NewGuid(), Now));
        Assert.Equal("ANULACION_NO_PERMITIDA", e.ErrorCode);
    }

    [Fact]
    public void Return_amount_is_proportional_and_the_last_unit_takes_the_rest()
    {
        // 3 × 333,33 = 999,99: un tercio redondea a 333,33 y lo último devuelve el resto exacto.
        var (sale, line) = ConfirmedSale(quantity: 3, price: 333.33m);
        var first = SaleReturn.Create(sale, Guid.NewGuid(), "Pan quemado", Guid.NewGuid(), Now);
        var a = first.AddLine(line, 1, (0, 0), restock: false);
        Assert.Equal(333.33m, a.Amount);

        var second = SaleReturn.Create(sale, Guid.NewGuid(), "Pan quemado", Guid.NewGuid(), Now);
        var b = second.AddLine(line, 2, (1, a.Amount), restock: true);
        Assert.Equal(line.LineTotal, a.Amount + b.Amount);
    }

    [Fact]
    public void Cannot_return_more_than_sold_or_more_than_charged()
    {
        var (sale, line) = ConfirmedSale(quantity: 2);
        var r = SaleReturn.Create(sale, Guid.NewGuid(), "Motivo", Guid.NewGuid(), Now);
        Assert.Throws<DomainException>(() => r.AddLine(line, 3, (0, 0), true));
        Assert.Throws<DomainException>(() => r.AddLine(line, 1, (2, line.LineTotal), true));

        sale.RegisterReturn(sale.Total);
        Assert.Equal(SalePaymentStatus.Refunded, sale.PaymentStatus);
        Assert.Throws<DomainException>(() => sale.RegisterReturn(1m));
    }
}
