using Asp.Versioning;
using Tahona.BakeryPOS.Application.Payments;
using Tahona.BakeryPOS.Application.Payments.Gateway;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>
/// Third-party webhooks. Anonymous: authenticity comes from the signature (<c>x-bold-signature</c>), validated over
/// the raw body. Bold expects 200 in under 2 s; an invalid signature answers 401 and is recorded.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/webhooks/pagos")]
[ApiVersion("1.0")]
[AllowAnonymous]
public sealed class WebhooksController : ControllerBase
{
    private readonly ISender _mediator;

    public WebhooksController(ISender mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("bold")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Bold(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        var outcome = await _mediator.Send(new ProcessBoldWebhookCommand(raw, Request.Headers[BoldSignature.Header].ToString()), ct);
        return outcome == WebhookOutcome.InvalidSignature ? Unauthorized() : Ok();
    }
}
