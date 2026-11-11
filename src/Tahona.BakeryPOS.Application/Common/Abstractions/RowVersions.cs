using Tahona.BakeryPOS.SharedKernel.Results;

namespace Tahona.BakeryPOS.Application.Common.Abstractions;

/// <summary>
/// Row version for ETag/If-Match: the row's version bytes (<c>row_version</c>) in base64. Master-data DTOs
/// expose it as <c>version</c> and PUTs require it.
/// </summary>
public static class RowVersions
{
    public static string Encode(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    /// <summary>Validates the version sent by the client: missing → 428, unreadable → 412.</summary>
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
