using Tahona.BakeryPOS.Domain.Exceptions;
using Tahona.BakeryPOS.Domain.Payments;
using Tahona.BakeryPOS.Domain.Sales;

namespace Tahona.BakeryPOS.UnitTests.Sales;

public class SaleTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static Sale NewSale() =>
        Sale.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

    [Fact]
    public void Tax_included_price_splits_base_and_tax()
    {
        var sale = NewSale();
        var line = sale.AddLine(Guid.NewGuid(), "PAN", "Pan", "UND", 2, 5_950m, true, 0, null, Guid.NewGuid(), 19m);

        Assert.Equal(11_900m, line.LineTotal);
        Assert.Equal(10_000m, line.TaxableBase);
        Assert.Equal(1_900m, line.TaxAmount);
        Assert.Equal(11_900m, sale.Total);
        Assert.Equal(1_900m, sale.TaxTotal);
    }

    [Fact]
    public void Tax_excluded_price_adds_tax_and_discount_needs_reason()
    {
        var sale = NewSale();
        Assert.Throws<DomainException>(() => sale.AddLine(Guid.NewGuid(), "X", "X", "UND", 1, 1_000m, false, 100m, null, Guid.NewGuid(), 19m));
        var line = sale.AddLine(Guid.NewGuid(), "X", "X", "UND", 1, 1_000m, false, 100m, "Cliente frecuente", Guid.NewGuid(), 19m);

        Assert.Equal(900m, line.TaxableBase);
        Assert.Equal(171m, line.TaxAmount);
        Assert.Equal(1_071m, line.LineTotal);
    }

    [Fact]
    public void Quantity_admits_two_decimals_only()
    {
        var sale = NewSale();
        sale.AddLine(Guid.NewGuid(), "TORTA", "Torta", "KG", 0.25m, 40_000m, true, 0, null, Guid.NewGuid(), 0m);
        Assert.Throws<DomainException>(() => sale.AddLine(Guid.NewGuid(), "TORTA", "Torta", "KG", 0.333m, 40_000m, true, 0, null, Guid.NewGuid(), 0m));
    }

    [Fact]
    public void Confirms_only_with_exact_approved_total_and_once()
    {
        var sale = NewSale();
        sale.AddLine(Guid.NewGuid(), "PAN", "Pan", "UND", 1, 3_000m, true, 0, null, Guid.NewGuid(), 0m);

        Assert.Throws<DomainException>(() => sale.Confirm(2_000m, 1, Now));
        Assert.True(sale.Confirm(3_000m, 1, Now));
        Assert.False(sale.Confirm(3_000m, 2, Now));
        Assert.Equal(1, sale.Number);
        Assert.Throws<DomainException>(() => sale.AddLine(Guid.NewGuid(), "X", "X", "UND", 1, 1m, true, 0, null, Guid.NewGuid(), 0m));
    }

    [Fact]
    public void Cash_payment_computes_change_and_only_cash_admits_excess()
    {
        var company = Guid.NewGuid();
        var cash = PaymentMethod.Create(company, PaymentMethod.CashCode, "Efectivo", PaymentMethodKind.Cash, null, false, 0);
        var transfer = PaymentMethod.Create(company, "TRANSFERENCIA", "Transferencia", PaymentMethodKind.TransferManual, null, true, 1);

        var p = Payment.RecordImmediate(company, Guid.NewGuid(), Guid.NewGuid(), cash, 7_500m, 10_000m, null, Now);
        Assert.Equal(PaymentStatus.Approved, p.Status);
        Assert.Equal(2_500m, p.ChangeAmount);

        Assert.Throws<DomainException>(() => Payment.RecordImmediate(company, Guid.NewGuid(), Guid.NewGuid(), cash, 7_500m, 5_000m, null, Now));
        Assert.Throws<DomainException>(() => Payment.RecordImmediate(company, Guid.NewGuid(), Guid.NewGuid(), transfer, 7_500m, null, null, Now));
        Assert.Throws<DomainException>(() => Payment.RecordImmediate(company, Guid.NewGuid(), Guid.NewGuid(), transfer, 7_500m, 8_000m, "ref", Now));
    }
}
