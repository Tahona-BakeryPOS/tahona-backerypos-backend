using Tahona.BakeryPOS.Domain.Catalog;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.UnitTests.Catalog;

public class ProductPriceTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();

    [Fact]
    public void New_price_closes_the_open_one_where_it_starts()
    {
        var first = ProductPrice.Open(Company, Product, null, 3000m, true, Now, []);
        var second = ProductPrice.Open(Company, Product, null, 3500m, true, Now.AddDays(1), [first]);

        Assert.Equal(Now.AddDays(1), first.ValidTo);
        Assert.Null(second.ValidTo);
        Assert.True(first.IsValidAt(Now.AddHours(23)));
        Assert.False(first.IsValidAt(Now.AddDays(1)));
        Assert.True(second.IsValidAt(Now.AddDays(1)));
    }

    [Fact]
    public void A_price_cannot_start_before_or_at_the_latest_one()
    {
        var future = ProductPrice.Open(Company, Product, null, 3000m, true, Now.AddDays(2), []);

        Assert.Throws<DomainException>(() => ProductPrice.Open(Company, Product, null, 2900m, true, Now.AddDays(1), [future]));
        Assert.Throws<DomainException>(() => ProductPrice.Open(Company, Product, null, 2900m, true, Now.AddDays(2), [future]));
    }

    [Fact]
    public void Negative_or_fractional_cents_are_rejected()
    {
        Assert.Throws<DomainException>(() => ProductPrice.Open(Company, Product, null, -1m, true, Now, []));
        Assert.Throws<DomainException>(() => ProductPrice.Open(Company, Product, null, 10.555m, true, Now, []));
    }

    [Fact]
    public void Branch_price_wins_over_base_and_base_applies_elsewhere()
    {
        var basePrice = ProductPrice.Open(Company, Product, null, 3000m, true, Now, []);
        var branchPrice = ProductPrice.Open(Company, Product, Branch, 3200m, true, Now, []);
        var prices = new[] { basePrice, branchPrice };

        Assert.Equal(3200m, ProductPrice.EffectiveAt(prices, Branch, Now.AddMinutes(1))!.Amount);
        Assert.Equal(3000m, ProductPrice.EffectiveAt(prices, Guid.NewGuid(), Now.AddMinutes(1))!.Amount);
        Assert.Equal(3000m, ProductPrice.EffectiveAt(prices, null, Now.AddMinutes(1))!.Amount);
        Assert.Null(ProductPrice.EffectiveAt(prices, Branch, Now.AddMinutes(-1)));
    }

    [Fact]
    public void Expired_branch_price_falls_back_to_base()
    {
        var basePrice = ProductPrice.Open(Company, Product, null, 3000m, true, Now, []);
        var branchPrice = ProductPrice.Open(Company, Product, Branch, 3200m, true, Now, []);
        var nextBranch = ProductPrice.Open(Company, Product, Branch, 3300m, true, Now.AddDays(3), [branchPrice]);

        Assert.Equal(3200m, ProductPrice.EffectiveAt([basePrice, branchPrice, nextBranch], Branch, Now.AddDays(1))!.Amount);
        Assert.Equal(3300m, ProductPrice.EffectiveAt([basePrice, branchPrice, nextBranch], Branch, Now.AddDays(3))!.Amount);
    }

    [Fact]
    public void Tax_none_must_have_zero_rate()
    {
        Assert.Throws<DomainException>(() => Tax.Create(Company, "X", "Excluido", TaxKind.None, 5m));
        Assert.Equal("ZZ", Tax.Create(Company, "EXC", "Excluido", TaxKind.None, 0m).DianCode);
        Assert.Equal("01", Tax.Create(Company, "IVA19", "IVA 19 %", TaxKind.Iva, 19m).DianCode);
    }
}
