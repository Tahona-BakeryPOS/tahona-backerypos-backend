using Tahona.BakeryPOS.SharedKernel.Results;

namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Versión de una fila para ETag/If-Match: el rowversion de SQL Server en base64. Los DTO de datos
/// maestros la exponen como <c>version</c> y los PUT la exigen.
/// </summary>
public static class RowVersions
{
    public static string Encode(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    /// <summary>Valida la versión enviada por el cliente: ausente → 428, ilegible → 412.</summary>
    public static Result<byte[]> Decode(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return Result.Failure<byte[]>(Error.VersionRequerida);
        try
        {
            return Result.Success(Convert.FromBase64String(version));
        }
        catch (FormatException)
        {
            return Result.Failure<byte[]>(Error.ConflictoVersion);
        }
    }
}
