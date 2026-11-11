using Tahona.BakeryPOS.Application.Common.Abstractions;
using Tahona.BakeryPOS.Domain.Companies;
using Tahona.BakeryPOS.SharedKernel.Results;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Tahona.BakeryPOS.Application.Companies.Profile;

public sealed record CompanyDto(
    Guid Id,
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
    string TimeZone,
    string Currency,
    string Status,
    string Version)
{
    public static CompanyDto From(Company c) => new(
        c.Id, c.LegalName, c.TradeName, c.NitNumber, c.NitCheckDigit, c.TaxRegime, c.TaxResponsibilities,
        c.Email, c.Phone, c.Address, c.CityCode, c.TimeZone, c.Currency, c.Status.ToString().ToLowerInvariant(),
        RowVersions.Encode(c.RowVersion));
}

/// <summary>The token's company (US-1.2).</summary>
public sealed record GetMyCompanyQuery : IRequest<Result<CompanyDto>>;

/// <summary>Contact and time zone; the tax data is changed by the platform (it affects invoicing).</summary>
public sealed record UpdateMyCompanyCommand(
    string? ExpectedVersion,
    string? TradeName,
    string Email,
    string? Phone,
    string? Address,
    string? CityCode,
    string? TimeZone) : IRequest<Result<CompanyDto>>;

public sealed class UpdateMyCompanyValidator : AbstractValidator<UpdateMyCompanyCommand>
{
    public UpdateMyCompanyValidator()
    {
        RuleFor(x => x.TradeName).MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Address).MaximumLength(200);
        RuleFor(x => x.CityCode).Matches("^[0-9]{5}$").When(x => x.CityCode is not null)
            .WithMessage("El código de ciudad es el código DANE de 5 dígitos.");
        RuleFor(x => x.TimeZone).Must(BeAKnownTimeZone!).When(x => x.TimeZone is not null)
            .WithMessage("Zona horaria desconocida; usa un identificador IANA como America/Bogota.");
    }

    internal static bool BeAKnownTimeZone(string id) => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}

public sealed class CompanyProfileHandler :
    IRequestHandler<GetMyCompanyQuery, Result<CompanyDto>>,
    IRequestHandler<UpdateMyCompanyCommand, Result<CompanyDto>>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public CompanyProfileHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Result<CompanyDto>> Handle(GetMyCompanyQuery request, CancellationToken ct)
    {
        var companyId = _tenant.RequiredCompanyId;
        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
        return company is null ? Result.Failure<CompanyDto>(Error.ResourceNotFound) : Result.Success(CompanyDto.From(company));
    }

    public async Task<Result<CompanyDto>> Handle(UpdateMyCompanyCommand request, CancellationToken ct)
    {
        var version = RowVersions.Decode(request.ExpectedVersion);
        if (version.IsFailure)
            return Result.Failure<CompanyDto>(version.Error);

        // Company is not an ITenantEntity (it is the tenant): it is always looked up by the token's company.
        var companyId = _tenant.RequiredCompanyId;
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null)
            return Result.Failure<CompanyDto>(Error.ResourceNotFound);

        _db.ExpectVersion(company, version.Value);
        company.UpdateContact(request.TradeName, request.Email, request.Phone, request.Address, request.CityCode, request.TimeZone);
        await _db.SaveChangesAsync(ct);
        return Result.Success(CompanyDto.From(company));
    }
}
