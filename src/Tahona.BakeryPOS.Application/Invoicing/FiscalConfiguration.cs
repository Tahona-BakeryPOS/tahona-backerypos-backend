using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Invoicing.Siigo;
using Tahona.BakeryPOS.Domain.Invoicing;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Invoicing;

/// <summary>The company's Siigo configuration; it never includes the user or the key in clear text.</summary>
public sealed record FiscalSettingsDto(string Provider, bool HasCredentials, long? InvoiceDocumentTypeId, long? CreditNoteDocumentTypeId,
    long? SellerId, long? ProductAccountGroupId, string ConsumerFinalIdentification, int RetentionYears, bool IsComplete, bool IsActive);

public sealed record SiigoCatalogsDto(IReadOnlyList<SiigoCatalogItem> InvoiceTypes, IReadOnlyList<SiigoCatalogItem> CreditNoteTypes,
    IReadOnlyList<SiigoCatalogItem> Users, IReadOnlyList<SiigoCatalogItem> Taxes, IReadOnlyList<SiigoCatalogItem> PaymentTypes);

/// <param name="Kind"><c>tax</c> o <c>payment_method</c>.</param>
public sealed record FiscalMappingDto(string Kind, Guid LocalId, string ExternalId);

public sealed record GetFiscalSettingsQuery : IRequest<Result<FiscalSettingsDto>>;

/// <summary>
/// Saves the configuration. <paramref name="Username"/>/<paramref name="AccessKey"/> null keep the stored ones.
/// Activating requires a complete configuration.
/// </summary>
public sealed record SaveFiscalSettingsCommand(string? Username, string? AccessKey, long? InvoiceDocumentTypeId, long? CreditNoteDocumentTypeId,
    long? SellerId, long? ProductAccountGroupId, string ConsumerFinalIdentification, int RetentionYears, bool Activate)
    : IRequest<Result<FiscalSettingsDto>>;

/// <summary>Live Siigo catalogs to configure and pair (document types, sellers, taxes, payment methods).</summary>
public sealed record GetSiigoCatalogsQuery : IRequest<Result<SiigoCatalogsDto>>;

public sealed record GetFiscalMappingsQuery : IRequest<Result<IReadOnlyList<FiscalMappingDto>>>;

/// <summary>Replaces the tax and payment-method pairings (the product ones are created by the submission).</summary>
public sealed record SaveFiscalMappingsCommand(IReadOnlyList<FiscalMappingDto> Mappings) : IRequest<Result<IReadOnlyList<FiscalMappingDto>>>;

public sealed class SaveFiscalSettingsValidator : AbstractValidator<SaveFiscalSettingsCommand>
{
    public SaveFiscalSettingsValidator()
    {
        RuleFor(x => x.Username).MaximumLength(200);
        RuleFor(x => x.AccessKey).MaximumLength(500);
        RuleFor(x => x.ConsumerFinalIdentification).NotEmpty().MaximumLength(20).Matches("^[0-9]+$");
        RuleFor(x => x.RetentionYears).InclusiveBetween(1, 50);
    }
}

public sealed class SaveFiscalMappingsValidator : AbstractValidator<SaveFiscalMappingsCommand>
{
    public SaveFiscalMappingsValidator()
    {
        RuleForEach(x => x.Mappings).ChildRules(m =>
        {
            m.RuleFor(x => x.Kind).Must(k => k is "tax" or "payment_method").WithMessage("Tipo: tax o payment_method.");
            m.RuleFor(x => x.LocalId).NotEmpty();
            m.RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(100).Matches("^[0-9]+$").WithMessage("El id de Siigo es numérico.");
        });
    }
}

public sealed class FiscalConfigurationHandler :
    IRequestHandler<GetFiscalSettingsQuery, Result<FiscalSettingsDto>>,
    IRequestHandler<SaveFiscalSettingsCommand, Result<FiscalSettingsDto>>,
    IRequestHandler<GetSiigoCatalogsQuery, Result<SiigoCatalogsDto>>,
    IRequestHandler<GetFiscalMappingsQuery, Result<IReadOnlyList<FiscalMappingDto>>>,
    IRequestHandler<SaveFiscalMappingsCommand, Result<IReadOnlyList<FiscalMappingDto>>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _secrets;
    private readonly ISiigoClient _siigo;

    public FiscalConfigurationHandler(IAppDbContext db, ITenantContext tenant, ISecretProtector secrets, ISiigoClient siigo)
    {
        _db = db;
        _tenant = tenant;
        _secrets = secrets;
        _siigo = siigo;
    }

    public async Task<Result<FiscalSettingsDto>> Handle(GetFiscalSettingsQuery request, CancellationToken ct)
    {
        var settings = await _db.FiscalSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return Result.Success(settings is null
            ? new FiscalSettingsDto(FiscalDocument.Siigo, false, null, null, null, null, FiscalSettings.DefaultConsumerFinal,
                FiscalSettings.DefaultRetentionYears, false, false)
            : ToDto(settings));
    }

    public async Task<Result<FiscalSettingsDto>> Handle(SaveFiscalSettingsCommand request, CancellationToken ct)
    {
        var settings = await _db.FiscalSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
            _db.FiscalSettings.Add(settings = FiscalSettings.Create(_tenant.RequiredCompanyId));

        if (!string.IsNullOrWhiteSpace(request.Username) || !string.IsNullOrWhiteSpace(request.AccessKey))
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.AccessKey))
                return Result.Failure<FiscalSettingsDto>(Error.Validation("Escribe juntos el usuario y la llave de acceso de Siigo."));
            settings.SetCredentials(_secrets.Protect(request.Username.Trim()), _secrets.Protect(request.AccessKey.Trim()));
        }
        settings.Configure(request.InvoiceDocumentTypeId, request.CreditNoteDocumentTypeId, request.SellerId, request.ProductAccountGroupId,
            request.ConsumerFinalIdentification, request.RetentionYears);
        settings.SetActive(request.Activate);
        await _db.SaveChangesAsync(ct);
        return Result.Success(ToDto(settings));
    }

    public async Task<Result<SiigoCatalogsDto>> Handle(GetSiigoCatalogsQuery request, CancellationToken ct)
    {
        var settings = await _db.FiscalSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings is null || settings.EncryptedUsername.Length == 0)
            return Result.Failure<SiigoCatalogsDto>(Error.Validation("Guarda primero el usuario y la llave de acceso de Siigo."));
        var credentials = new SiigoCredentials(settings.CompanyId, _secrets.Unprotect(settings.EncryptedUsername), _secrets.Unprotect(settings.EncryptedAccessKey));
        return Result.Success(new SiigoCatalogsDto(
            await _siigo.DocumentTypesAsync(credentials, "FV", ct),
            await _siigo.DocumentTypesAsync(credentials, "NC", ct),
            await _siigo.UsersAsync(credentials, ct),
            await _siigo.TaxesAsync(credentials, ct),
            await _siigo.PaymentTypesAsync(credentials, ct)));
    }

    public async Task<Result<IReadOnlyList<FiscalMappingDto>>> Handle(GetFiscalMappingsQuery request, CancellationToken ct)
    {
        var mappings = await _db.FiscalMappings.AsNoTracking().Where(m => m.Kind != FiscalMappingKind.Product).ToListAsync(ct);
        IReadOnlyList<FiscalMappingDto> list = mappings.Select(ToDto).ToList();
        return Result.Success(list);
    }

    public async Task<Result<IReadOnlyList<FiscalMappingDto>>> Handle(SaveFiscalMappingsCommand request, CancellationToken ct)
    {
        var existing = await _db.FiscalMappings.Where(m => m.Kind != FiscalMappingKind.Product).ToListAsync(ct);
        foreach (var input in request.Mappings.DistinctBy(m => (m.Kind, m.LocalId)))
        {
            var kind = input.Kind == "tax" ? FiscalMappingKind.Tax : FiscalMappingKind.PaymentMethod;
            var row = existing.FirstOrDefault(m => m.Kind == kind && m.LocalId == input.LocalId);
            if (row is null)
                _db.FiscalMappings.Add(FiscalMapping.Create(_tenant.RequiredCompanyId, kind, input.LocalId, input.ExternalId));
            else
                row.Set(input.ExternalId);
        }
        await _db.SaveChangesAsync(ct);
        return await Handle(new GetFiscalMappingsQuery(), ct);
    }

    private static FiscalSettingsDto ToDto(FiscalSettings s) => new(s.Provider, s.EncryptedUsername.Length > 0, s.InvoiceDocumentTypeId,
        s.CreditNoteDocumentTypeId, s.SellerId, s.ProductAccountGroupId, s.ConsumerFinalIdentification, s.RetentionYears, s.IsComplete, s.IsActive);

    private static FiscalMappingDto ToDto(FiscalMapping m) =>
        new(m.Kind == FiscalMappingKind.Tax ? "tax" : "payment_method", m.LocalId, m.ExternalId);
}
