using Tahona.BakeryPOS.SharedKernel.Results;
using Microsoft.AspNetCore.Mvc;

namespace Tahona.BakeryPOS.Api.Extensions;

/// <summary>
/// ETag/If-Match de datos maestros (docs/api-contracts.md): el GET y el PUT devuelven <c>ETag</c> con la
/// versión de la fila; el PUT exige <c>If-Match</c> (428 si falta, 412 si cambió).
/// </summary>
public static class ETags
{
    /// <summary>Versión enviada en If-Match, sin comillas ni prefijo débil.</summary>
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
