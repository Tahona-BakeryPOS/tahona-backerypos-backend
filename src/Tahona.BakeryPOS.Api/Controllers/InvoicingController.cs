using Asp.Versioning;
using Tahona.BakeryPOS.Api.Extensions;
using Tahona.BakeryPOS.Api.Filters;
using Tahona.BakeryPOS.Application.Common.Security;
using Tahona.BakeryPOS.Application.Invoicing;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>
/// Electronic invoicing with Siigo (docs/electronic-invoicing.md): configuration, pairings and
/// fiscal documents. Credentials are received here and never returned. Sending is done by the outbox.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}")]
[ApiVersion("1.0")]
[Authorize]
public sealed class InvoicingController : ControllerBase
{
    private readonly ISender _mediator;

    public InvoicingController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("invoicing/settings")]
    [RequirePermission(Permissions.FiscalManage)]
    [ProducesResponseType<FiscalSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSettings(CancellationToken ct) =>
        (await _mediator.Send(new GetFiscalSettingsQuery(), ct)).ToActionResult(HttpContext);

    /// <summary>A null user and key = keep the stored ones.</summary>
    [HttpPut("invoicing/settings")]
    [RequirePermission(Permissions.FiscalManage)]
    [ProducesResponseType<FiscalSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSettings([FromBody] SaveFiscalSettingsCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToActionResult(HttpContext);

    /// <summary>Live Siigo catalogs (503 PROVIDER_UNAVAILABLE if it does not respond).</summary>
    [HttpGet("invoicing/siigo/catalogs")]
    [RequirePermission(Permissions.FiscalManage)]
    [ProducesResponseType<SiigoCatalogsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCatalogs(CancellationToken ct) =>
        (await _mediator.Send(new GetSiigoCatalogsQuery(), ct)).ToActionResult(HttpContext);

    [HttpGet("invoicing/mappings")]
    [RequirePermission(Permissions.FiscalManage)]
    [ProducesResponseType<IReadOnlyList<FiscalMappingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMappings(CancellationToken ct) =>
        (await _mediator.Send(new GetFiscalMappingsQuery(), ct)).ToActionResult(HttpContext);

    [HttpPut("invoicing/mappings")]
    [RequirePermission(Permissions.FiscalManage)]
    [ProducesResponseType<IReadOnlyList<FiscalMappingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveMappings([FromBody] SaveFiscalMappingsCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToActionResult(HttpContext);

    /// <summary>Test invoice to a final consumer; requires confirming that Siigo is in the test environment.</summary>
    [HttpPost("invoicing/test")]
    [Idempotent]
    [RequirePermission(Permissions.FiscalManage)]
    [ProducesResponseType<FiscalDocumentDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> TestInvoice([FromBody] IssueTestInvoiceCommand command, CancellationToken ct) =>
        (await _mediator.Send(command, ct)).ToCreatedResult(HttpContext);

    /// <summary><c>status</c>: pending, submitting, accepted, rejected, temporary_error, unknown, cancelled.</summary>
    [HttpGet("fiscal-documents")]
    [RequirePermission(Permissions.SalesView)]
    [ProducesResponseType<IReadOnlyList<FiscalDocumentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDocuments([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? status, CancellationToken ct) =>
        (await _mediator.Send(new GetFiscalDocumentsQuery(from, to, status), ct)).ToActionResult(HttpContext);

    [HttpGet("fiscal-documents/{id:guid}/attempts")]
    [RequirePermission(Permissions.FiscalManage)]
    [ProducesResponseType<IReadOnlyList<FiscalAttemptDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAttempts(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new GetFiscalDocumentAttemptsQuery(id), ct)).ToActionResult(HttpContext);

    [HttpGet("fiscal-documents/{id:guid}/pdf")]
    [RequirePermission(Permissions.SalesView)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPdf(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetFiscalDocumentPdfQuery(id), ct);
        return result.IsSuccess ? File(result.Value, "application/pdf", $"factura-{id:N}.pdf") : result.ToActionResult(HttpContext);
    }

    [HttpPost("fiscal-documents/{id:guid}/retry")]
    [RequirePermission(Permissions.FiscalManage)]
    [ProducesResponseType<FiscalDocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct) =>
        (await _mediator.Send(new RetryFiscalDocumentCommand(id), ct)).ToActionResult(HttpContext);
}
