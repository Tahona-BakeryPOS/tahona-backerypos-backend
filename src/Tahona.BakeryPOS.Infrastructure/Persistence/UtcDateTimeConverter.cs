using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>Every date is stored and read as UTC (PostgreSQL's timestamptz rejects Kind=Unspecified).</summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
