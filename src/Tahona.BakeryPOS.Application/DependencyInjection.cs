using System.Reflection;
using Tahona.BakeryPOS.Application.Cash.Registers;
using Tahona.BakeryPOS.Application.Cash.Sessions;
using Tahona.BakeryPOS.Application.Catalog;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Behaviors;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Companies;
using Tahona.BakeryPOS.Application.Identity;
using Tahona.BakeryPOS.Application.Identity.Auth;
using Tahona.BakeryPOS.Application.Inventory;
using Tahona.BakeryPOS.Application.Invoicing;
using Tahona.BakeryPOS.Application.Payments;
using Tahona.BakeryPOS.Application.Sales;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tahona.BakeryPOS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));

        services.AddValidatorsFromAssembly(assembly);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        // Contracts between modules (Common/Contracts), each implemented by the owning module.
        services.AddScoped<CompanyDirectory>();
        services.AddScoped<ICompanyDirectory>(sp => sp.GetRequiredService<CompanyDirectory>());
        services.AddScoped<IBranchDirectory>(sp => sp.GetRequiredService<CompanyDirectory>());
        services.AddScoped<ICashRegisterDirectory, CashRegisterDirectory>();
        services.AddScoped<IIdentityProvisioning, IdentityProvisioning>();
        services.AddScoped<ICatalogProvisioning, CatalogProvisioning>();
        services.AddScoped<IProductDirectory, ProductDirectory>();
        services.AddScoped<IInventoryProvisioning, InventoryProvisioning>();
        services.AddScoped<InventoryLedger>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<ICashProvisioning, CashProvisioning>();
        services.AddScoped<ICashSessionDirectory, CashSessionDirectory>();
        services.AddScoped<IPendingPaymentsProbe, PendingPaymentsProbe>();
        services.AddScoped<IPaymentsProvisioning, PaymentsProvisioning>();
        services.AddScoped<GatewayPaymentService>();
        services.AddScoped<IFiscalDocumentIssuer, FiscalDocumentIssuer>();
        services.AddScoped<IOutboxHandler, FiscalSubmitHandler>();
        services.AddScoped<IInventoryPosting, InventoryPosting>();
        services.AddScoped<ICashDrawer, CashDrawer>();
        services.AddScoped<ISalePayments, SalePayments>();
        services.AddScoped<SaleConfirmation>();
        services.AddScoped<IOutboxHandler, SalePaymentResolvedHandler>();

        services.AddScoped<SessionIssuer>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
