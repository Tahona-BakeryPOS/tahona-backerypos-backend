using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tahona.BakeryPOS.Infrastructure.Persistence;

/// <summary>Toda fecha se guarda y se lee como UTC (timestamptz de PostgreSQL rechaza Kind=Unspecified).</summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
