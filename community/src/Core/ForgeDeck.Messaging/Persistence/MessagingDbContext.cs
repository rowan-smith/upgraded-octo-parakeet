using ForgeDeck.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ForgeDeck.Messaging.Persistence;

public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<DateTimeOffset, string> DateTimeOffsetConverter = new(
        v => v.ToUniversalTime().ToString("O"),
        v => DateTimeOffset.Parse(v).ToUniversalTime());

    private static readonly ValueConverter<DateTimeOffset?, string?> NullableDateTimeOffsetConverter = new(
        v => v.HasValue ? v.Value.ToUniversalTime().ToString("O") : null,
        v => v == null ? null : DateTimeOffset.Parse(v).ToUniversalTime());

    public DbSet<OutboxEntity> Outbox => Set<OutboxEntity>();
    public DbSet<InboxEntity> Inbox => Set<InboxEntity>();
    public DbSet<DeliveryEntity> Deliveries => Set<DeliveryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxEntity>(e =>
        {
            e.ToTable("core_event_outbox");
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.EventType).HasColumnName("event_type").IsRequired();
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.EnvelopeJson).HasColumnName("envelope_json").IsRequired();
            e.Property(x => x.PayloadJson).HasColumnName("payload_json").IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
            e.Property(x => x.DispatchState).HasColumnName("dispatch_state").HasConversion<string>().IsRequired();
            e.Property(x => x.Attempts).HasColumnName("attempts");
            e.Property(x => x.LastAttemptAt).HasColumnName("last_attempt_at").HasConversion(NullableDateTimeOffsetConverter);
            e.Property(x => x.LastError).HasColumnName("last_error");
            e.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at").HasConversion(NullableDateTimeOffsetConverter);
            e.HasIndex(x => new { x.DispatchState, x.NextAttemptAt, x.CreatedAt })
                .HasDatabaseName("ix_core_event_outbox_pending");
        });

        modelBuilder.Entity<InboxEntity>(e =>
        {
            e.ToTable("core_event_inbox");
            e.HasKey(x => new { x.EventId, x.ConsumerId });
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.ConsumerId).HasColumnName("consumer_id").IsRequired();
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at").HasConversion(DateTimeOffsetConverter);
        });

        modelBuilder.Entity<DeliveryEntity>(e =>
        {
            e.ToTable("core_event_deliveries");
            e.HasKey(x => new { x.EventId, x.ConsumerId });
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.ConsumerId).HasColumnName("consumer_id").IsRequired();
            e.Property(x => x.EventType).HasColumnName("event_type").IsRequired();
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.State).HasColumnName("state").HasConversion<string>().IsRequired();
            e.Property(x => x.Attempts).HasColumnName("attempts");
            e.Property(x => x.LastAttemptAt).HasColumnName("last_attempt_at").HasConversion(NullableDateTimeOffsetConverter);
            e.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at").HasConversion(NullableDateTimeOffsetConverter);
            e.Property(x => x.LastError).HasColumnName("last_error");
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at").HasConversion(DateTimeOffsetConverter);
            e.Property(x => x.EnvelopeJson).HasColumnName("envelope_json").IsRequired();
            e.HasIndex(x => new { x.State, x.LastAttemptAt })
                .HasDatabaseName("ix_core_event_deliveries_failed");
            e.HasIndex(x => x.CorrelationId)
                .HasDatabaseName("ix_core_event_deliveries_correlation");
        });
    }
}

public sealed class OutboxEntity
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
    public int Version { get; set; }
    public string EnvelopeJson { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public OutboxDispatchState DispatchState { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}

public sealed class InboxEntity
{
    public Guid EventId { get; set; }
    public string ConsumerId { get; set; } = "";
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class DeliveryEntity
{
    public Guid EventId { get; set; }
    public string ConsumerId { get; set; } = "";
    public string EventType { get; set; } = "";
    public int Version { get; set; }
    public DeliveryState State { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string EnvelopeJson { get; set; } = "{}";
}
