using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.UnitTests.Companies;

public class NitTests
{
    [Theory]
    [InlineData("800197268", 4)] // DIAN
    [InlineData("890903938", 8)] // Bancolombia
    [InlineData("860002964", 4)] // Banco de Bogotá
    public void Computes_the_DIAN_check_digit(string number, int expected)
    {
        Assert.Equal(expected, Nit.ComputeCheckDigit(number));
    }

    [Fact]
    public void Accepts_dots_and_dash_and_keeps_only_digits()
    {
        var nit = Nit.Create("800.197.268", 4);
        Assert.Equal("800197268", nit.Number);
        Assert.Equal("800197268-4", nit.ToString());
    }

    [Fact]
    public void Rejects_a_wrong_check_digit()
    {
        var ex = Assert.Throws<DomainException>(() => Nit.Create("800197268", 5));
        Assert.Equal("NIT_INVALIDO", ex.ErrorCode);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567890123456")]
    public void Rejects_an_invalid_length(string number)
    {
        Assert.Throws<DomainException>(() => Nit.Create(number, Nit.ComputeCheckDigit(number.Length > 15 ? number[..15] : number)));
    }
}
