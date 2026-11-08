using Asp.Versioning;
using Tahona.BakeryPOS.Application.Payments;
using Tahona.BakeryPOS.Application.Payments.Gateway;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Controllers;

/// <summary>
/// Webhooks de terceros. Anónimos: la autenticidad la da la firma (<c>x-bold-signature</c>), validada sobre
/// el cuerpo crudo. Bold espera 200 en menos de 2 s; una firma inválida responde 401 y queda registrada.
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
