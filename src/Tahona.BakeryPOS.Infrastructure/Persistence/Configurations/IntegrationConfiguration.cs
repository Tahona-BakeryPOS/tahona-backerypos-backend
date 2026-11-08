using Tahona.BakeryPOS.Infrastructure.Persistence.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tahona.BakeryPOS.Infrastructure.Persistence.Configurations;

/// <summary>Tablas transversales — esquemas <c>aud</c> e <c>int</c>.</summary>
internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs", "aud");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Entity).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(64).IsUnicode(false);
        builder.HasIndex(e => new { e.CompanyId, e.Entity, e.EntityId, e.OccurredAt });
        builder.HasIndex(e => new { e.CompanyId, e.OccurredAt });
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", "int", t =>
            t.HasCheckConstraint("ck_outbox_messages_status", "status IN ('pending','processing','done','failed')"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasMaxLength(100).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(2000);
        builder.HasIndex(e => new { e.Status, e.NextAttemptAt });
    }
}

internal sealed class InboundEventConfiguration : IEntityTypeConfiguration<InboundEvent>
{
    public void Configure(EntityTypeBuilder<InboundEvent> builder)
    {
        builder.ToTable("inbound_events", "int", t =>
            t.HasCheckConstraint("ck_inbound_events_status", "status IN ('received','processed','ignored','failed')"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Provider).HasMaxLength(40).IsUnicode(false).IsRequired();
        builder.Property(e => e.ExternalEventId).HasMaxLength(200).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(2000);
        // Deduplicación de webhooks: el mismo evento del mismo proveedor solo se registra una vez.
        builder.HasIndex(e => new { e.Provider, e.ExternalEventId }).IsUnique();
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_requests", "int", t =>
            t.HasCheckConstraint("ck_idempotency_requests_status", "status IN ('in_progress','completed')"));
        builder.HasKey(e => new { e.CompanyId, e.Key, e.Endpoint });
        builder.Property(e => e.Key).HasMaxLength(64).IsUnicode(false);
        builder.Property(e => e.Endpoint).HasMaxLength(200).IsUnicode(false);
        builder.Property(e => e.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasMaxLength(20).IsUnicode(false).IsRequired();
    }
}
