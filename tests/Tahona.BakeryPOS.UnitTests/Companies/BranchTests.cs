using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.Domain.Exceptions;

namespace Tahona.BakeryPOS.UnitTests.Companies;

public class BranchTests
{
    [Fact]
    public void Normalizes_code_and_trims_fields()
    {
        var branch = Branch.Create(Guid.NewGuid(), " sede-1 ", " Sede Centro ", "  ");
        Assert.Equal("SEDE-1", branch.Code);
        Assert.Equal("Sede Centro", branch.Name);
        Assert.Null(branch.Address);
        Assert.True(branch.IsActive);
    }

    [Fact]
    public void Requires_a_company()
    {
        Assert.Throws<DomainException>(() => Branch.Create(Guid.Empty, "S1", "Sede", null));
    }

    [Fact]
    public void Ids_are_uuid_v7_generated_by_the_application()
    {
        var branch = Branch.Create(Guid.NewGuid(), "A", "A", null);
        Assert.Equal(7, branch.Id.Version);
    }

    [Fact]
    public void Company_requires_legal_name_and_defaults_to_Bogota_time()
    {
        Assert.Throws<DomainException>(() => Company.Create(" ", Nit.Create("800197268", 4), "48", ["R-99-PN"], "a@b.co"));
        var company = Company.Create("Panadería La Espiga S.A.S.", Nit.Create("800197268", 4), "48", ["r-99-pn"], " Info@Espiga.co ");
        Assert.Equal("America/Bogota", company.TimeZone);
        Assert.Equal("COP", company.Currency);
        Assert.Equal(["R-99-PN"], company.TaxResponsibilities);
        Assert.Equal("info@espiga.co", company.Email);
    }

    [Theory]
    [InlineData("50", "R-99-PN")]
    [InlineData("48", "X-1")]
    public void Company_rejects_unknown_tax_codes(string regime, string responsibility)
    {
        Assert.Throws<DomainException>(() =>
            Company.Create("Panadería", Nit.Create("800197268", 4), regime, [responsibility], "a@b.co"));
    }

    [Fact]
    public void Branch_update_keeps_the_code()
    {
        var branch = Branch.Create(Guid.NewGuid(), "s1", "Sede", null);
        branch.Update(" Sede Norte ", "Calle 1", "11001", null);
        Assert.Equal("S1", branch.Code);
        Assert.Equal("Sede Norte", branch.Name);
        Assert.Equal("11001", branch.CityCode);
    }
}
