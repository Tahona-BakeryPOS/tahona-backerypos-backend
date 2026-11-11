using Tahona.BakeryPOS.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Extensions;

/// <summary>
/// ETag/If-Match for master data (docs/api-contracts.md): GET and PUT return <c>ETag</c> with the
/// row version; PUT requires <c>If-Match</c> (428 if missing, 412 if it changed).
/// </summary>
public static class ETags
{
    /// <summary>Version sent in If-Match, without quotes or weak prefix.</summary>
    public static string? IfMatch(this HttpRequest request)
    {
        var value = request.Headers.IfMatch.ToString().Trim();
        if (value.Length == 0) return null;
        if (value.StartsWith("W/", StringComparison.Ordinal)) value = value[2..];
        return value.Trim('"');
    }

    public static IActionResult ToVersionedResult<T>(this Result<T> result, HttpContext http, Func<T, string> version)
    {
        if (result.IsSuccess)
            http.Response.Headers.ETag = $"\"{version(result.Value)}\"";
        return result.ToActionResult(http);
    }
}
