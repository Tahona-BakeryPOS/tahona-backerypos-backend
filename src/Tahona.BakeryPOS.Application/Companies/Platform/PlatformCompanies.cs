using Tahona.BakeryPOS.Application.Common.Contracts;
using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Application.Companies.Profile;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Companies.Platform;

/// <summary>
/// Company onboarding by the platform administrator (US-1.1, D-09): creates the company, its system
/// roles and the owner, all in one transaction. Default payment methods, taxes and inventory
/// location arrive with their modules (phases 3, 4 and 6).
/// </summary>
public sealed record CreateCompanyCommand(
    string LegalName,
    string? TradeName,
    string Nit,
    int NitCheckDigit,
    string TaxRegime,
    IReadOnlyList<string> TaxResponsibilities,
    string Email,
    string? Phone,
    string? Address,
    string? CityCode,
    string? TimeZone,
    OwnerInput Owner) : IRequest<Result<CreatedCompanyDto>>;

public sealed record OwnerInput(string FullName, string Email);

/// <param name="Owner">The one-time password is only shown in this response; it is not stored in clear text.</param>
public sealed record CreatedCompanyDto(CompanyDto Company, ProvisionedOwner Owner);

/// <summary>Platform view: status of the companies, with no data about their operation.</summary>
public sealed record GetCompaniesQuery : IRequest<Result<IReadOnlyList<PlatformCompanyDto>>>;

public sealed record PlatformCompanyDto(Guid Id, string LegalName, string? TradeName, string Nit, int NitCheckDigit, string Status, DateTime CreatedAt);

public sealed class CreateCompanyValidator : AbstractValidator<CreateCompanyCommand>
{
    public CreateCompanyValidator()
    {
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TradeName).MaximumLength(200);
        RuleFor(x => x.Nit).NotEmpty().Matches("^[0-9.]{6,20}$").WithMessage("El NIT solo lleva números (sin dígito de verificación).");
        RuleFor(x => x.NitCheckDigit).InclusiveBetween(0, 9);
        RuleFor(x => x.TaxRegime).Must(TaxCodes.Regimes.Contains)
            .WithMessage($"Régimen fiscal: {string.Join(", ", TaxCodes.Regimes)}.");
        RuleFor(x => x.TaxResponsibilities).NotEmpty()
            .WithMessage("Indica al menos una responsabilidad fiscal.");
        RuleForEach(x => x.TaxResponsibilities).Must(r => TaxCodes.Responsibilities.Contains(r.Trim().ToUpperInvariant()))
            .WithMessage($"Responsabilidad fiscal: {string.Join(", ", TaxCodes.Responsibilities)}.");
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Address).MaximumLength(200);
        RuleFor(x => x.CityCode).Matches("^[0-9]{5}$").When(x => x.CityCode is not null)
            .WithMessage("El código de ciudad es el código DANE de 5 dígitos.");
        RuleFor(x => x.TimeZone).Must(UpdateMyCompanyValidator.BeAKnownTimeZone!).When(x => x.TimeZone is not null)
            .WithMessage("Zona horaria desconocida; usa un identificador IANA como America/Bogota.");
        RuleFor(x => x.Owner).NotNull();
        RuleFor(x => x.Owner.FullName).NotEmpty().MaximumLength(120).When(x => x.Owner is not null);
        RuleFor(x => x.Owner.Email).NotEmpty().EmailAddress().MaximumLength(200).When(x => x.Owner is not null);
    }
}

public sealed class PlatformCompaniesHandler :
    IRequestHandler<CreateCompanyCommand, Result<CreatedCompanyDto>>,
    IRequestHandler<GetCompaniesQuery, Result<IReadOnlyList<PlatformCompanyDto>>>
{
    private readonly IAppDbContext _db;
    private readonly IIdentityProvisioning _identity;
    private readonly ICatalogProvisioning _catalog;
    private readonly IInventoryProvisioning _inventory;
    private readonly ICashProvisioning _cash;
    private readonly IPaymentsProvisioning _payments;

    public PlatformCompaniesHandler(IAppDbContext db, IIdentityProvisioning identity, ICatalogProvisioning catalog,
        IInventoryProvisioning inventory, ICashProvisioning cash, IPaymentsProvisioning payments)
    {
        _payments = payments;
        _db = db;
        _identity = identity;
        _catalog = catalog;
        _inventory = inventory;
        _cash = cash;
    }

    public async Task<Result<CreatedCompanyDto>> Handle(CreateCompanyCommand request, CancellationToken ct)
    {
        var nit = Domain.Companies.Nit.Create(request.Nit, request.NitCheckDigit);
        if (await _db.Companies.IgnoreQueryFilters([QueryFilters.SoftDelete]).AnyAsync(c => c.NitNumber == nit.Number, ct))
            return Result.Failure<CreatedCompanyDto>(Error.DuplicateNit);

        var company = Company.Create(request.LegalName, nit, request.TaxRegime, request.TaxResponsibilities, request.Email,
            request.TradeName, request.Phone, request.Address, request.CityCode, request.TimeZone);
        _db.Companies.Add(company);

        var owner = await _identity.ProvisionCompanyAsync(company.Id, request.Owner.FullName, request.Owner.Email, ct);
        _catalog.ProvisionCompany(company.Id);
        _inventory.ProvisionCompany(company.Id);
        _cash.ProvisionCompany(company.Id);
        _payments.ProvisionCompany(company.Id);
        await _db.SaveChangesAsync(ct);

        return Result.Success(new CreatedCompanyDto(CompanyDto.From(company), owner));
    }

    public async Task<Result<IReadOnlyList<PlatformCompanyDto>>> Handle(GetCompaniesQuery request, CancellationToken ct)
    {
        var rows = await _db.Companies.AsNoTracking()
            .OrderBy(c => c.LegalName)
            .Select(c => new { c.Id, c.LegalName, c.TradeName, c.NitNumber, c.NitCheckDigit, c.Status, c.CreatedAt })
            .ToListAsync(ct);
        IReadOnlyList<PlatformCompanyDto> companies = rows
            .Select(c => new PlatformCompanyDto(c.Id, c.LegalName, c.TradeName, c.NitNumber, c.NitCheckDigit,
                c.Status.ToString().ToLowerInvariant(), c.CreatedAt))
            .ToList();
        return Result.Success(companies);
    }
}
