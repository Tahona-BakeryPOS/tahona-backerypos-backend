using System.Reflection;
using Tahona.BakeryPOS.Api.Controllers;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Common;
using Tahona.BakeryPOS.Infrastructure.Persistence;
using NetArchTest.Rules;

namespace Tahona.BakeryPOS.ArchitectureTests;

/// <summary>
/// Reglas de dependencia del monolito modular (docs/architecture.md, D-14): capas hacia adentro y
/// módulos que no se conocen entre sí. Si una regla falla, el mensaje lista los tipos culpables.
/// </summary>
public class LayeringTests
{
    private static readonly Assembly Domain = typeof(AuditableEntity).Assembly;
    private static readonly Assembly Application = typeof(ITenantContext).Assembly;
    private static readonly Assembly Infrastructure = typeof(AppDbContext).Assembly;
    private static readonly Assembly Api = typeof(BranchesController).Assembly;

    /// <summary>Módulos del diseño; los que aún no existen pasan la regla sin tipos.</summary>
    private static readonly string[] ModuleNames =
        ["Companies", "Identity", "Catalog", "Inventory", "Cash", "Sales", "Payments", "Invoicing"];

    public static readonly TheoryData<string> Modules = new(ModuleNames);

    [Fact]
    public void Domain_depends_on_nothing_but_itself_and_the_shared_kernel()
    {
        AssertNoDependency(Types.InAssembly(Domain), "Tahona.BakeryPOS.Application", "Tahona.BakeryPOS.Infrastructure", "Tahona.BakeryPOS.Api", "Microsoft.EntityFrameworkCore");
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_api()
    {
        AssertNoDependency(Types.InAssembly(Application), "Tahona.BakeryPOS.Infrastructure", "Tahona.BakeryPOS.Api", "Npgsql");
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        AssertNoDependency(Types.InAssembly(Infrastructure), "Tahona.BakeryPOS.Api");
    }

    [Fact]
    public void Controllers_only_talk_to_the_application_layer()
    {
        AssertNoDependency(Types.InAssembly(Api).That().ResideInNamespace("Tahona.BakeryPOS.Api.Controllers"), "Tahona.BakeryPOS.Infrastructure", "Microsoft.EntityFrameworkCore");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Domain_modules_do_not_reference_each_other(string module)
    {
        AssertModuleIsolated(Domain, "Tahona.BakeryPOS.Domain", module);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Application_modules_do_not_reference_each_other(string module)
    {
        AssertModuleIsolated(Application, "Tahona.BakeryPOS.Application", module);
        // Un módulo de aplicación tampoco usa entidades de dominio de otro módulo.
        AssertModuleIsolated(Application, "Tahona.BakeryPOS.Domain", module, $"Tahona.BakeryPOS.Application.{module}");
    }

    private static void AssertModuleIsolated(Assembly assembly, string root, string module, string? ownerNamespace = null)
    {
        var others = ModuleNames.Where(m => m != module).Select(m => $"{root}.{m}").ToArray();
        var types = Types.InAssembly(assembly).That().ResideInNamespace(ownerNamespace ?? $"{root}.{module}");
        AssertNoDependency(types, others);
    }

    private static void AssertNoDependency(PredicateList types, params string[] forbidden)
    {
        var result = types.ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        Assert.True(result.IsSuccessful,
            "Dependencias prohibidas en: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static void AssertNoDependency(Types types, params string[] forbidden) =>
        AssertNoDependency(types.That().HaveNameMatching(".*"), forbidden);
}
