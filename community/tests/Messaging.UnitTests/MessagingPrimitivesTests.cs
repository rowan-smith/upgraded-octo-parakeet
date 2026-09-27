using System.Text.Json;
using ForgeDeck.Build.Contracts.Events;
using ForgeDeck.Contracts.Events;
using ForgeDeck.Messaging.Bus;
using ForgeDeck.Messaging.Inbox;
using ForgeDeck.Messaging.Outbox;
using ForgeDeck.Messaging.Persistence;
using ForgeDeck.Messaging.Publishing;
using ForgeDeck.Messaging.Registry;
using ForgeDeck.Messaging.Serialization;
using ForgeDeck.Messaging.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Messaging.UnitTests;

public sealed record SamplePayload(string Name);

[Trait("Category", "Unit")]
[Trait("Category", "Messaging")]
public sealed class EventEnvelopeTests
{
    [Fact]
    public async Task Publish_without_correlation_creates_new_chain()
    {
        await using var harness = await MessagingHarness.CreateAsync();
        await harness.Publisher.PublishAsync(new SamplePayload("a"), new PublishOptions
        {
            Actor = new EventActor(ActorType.User, "rowan", "Rowan"),
            Publisher = "test",
            OrganisationId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid()
        });

        var rows = await harness.Outbox.ListRecentAsync(10);
        var row = Assert.Single(rows);
        var envelope = harness.Serializer.DeserializeEnvelope(row.EnvelopeJson);
        Assert.NotEqual(Guid.Empty, envelope.Id);
        Assert.Equal(envelope.Id, envelope.CorrelationId);
        Assert.Null(envelope.CausationId);
        Assert.Equal(ActorType.User, envelope.Actor.Type);
        Assert.Equal("rowan", envelope.Actor.Id);
        Assert.NotNull(envelope.OrganisationId);
        Assert.NotNull(envelope.ProjectId);
        Assert.Equal(SampleContract.Type, envelope.Type);
        Assert.Equal(1, envelope.Version);
    }

    [Fact]
    public async Task Child_event_inherits_correlation_and_sets_causation()
    {
        await using var harness = await MessagingHarness.CreateAsync();
        var parent = new EventEnvelope(
            Guid.NewGuid(), SampleContract.Type, 1, DateTimeOffset.UtcNow,
            new EventActor(ActorType.System, "sys", "sys"), Guid.NewGuid(), null, null, null, "test",
            JsonDocument.Parse("{}").RootElement);

        using (CorrelationContext.Push(parent))
        {
            await harness.Publisher.PublishAsync(new SamplePayload("child"));
        }

        var row = Assert.Single(await harness.Outbox.ListRecentAsync(10));
        var child = harness.Serializer.DeserializeEnvelope(row.EnvelopeJson);
        Assert.Equal(parent.CorrelationId, child.CorrelationId);
        Assert.Equal(parent.Id, child.CausationId);
    }

    [Fact]
    public async Task Nested_causation_chain_A_B_C()
    {
        await using var harness = await MessagingHarness.CreateAsync();
        var aId = Guid.NewGuid();
        var correlation = Guid.NewGuid();
        var a = new EventEnvelope(aId, SampleContract.Type, 1, DateTimeOffset.UtcNow,
            new EventActor(ActorType.System, "s", "s"), correlation, null, null, null, "t",
            JsonDocument.Parse("{}").RootElement);

        using (CorrelationContext.Push(a))
        {
            await harness.Publisher.PublishAsync(new SamplePayload("b"));
        }

        var bRow = Assert.Single(await harness.Outbox.ListRecentAsync(10));
        var b = harness.Serializer.DeserializeEnvelope(bRow.EnvelopeJson);
        Assert.Equal(correlation, b.CorrelationId);
        Assert.Equal(aId, b.CausationId);

        using (CorrelationContext.Push(b))
        {
            await harness.Publisher.PublishAsync(new SamplePayload("c"));
        }

        var rows = await harness.Outbox.ListRecentAsync(10);
        Assert.Equal(2, rows.Count);
        var c = harness.Serializer.DeserializeEnvelope(rows[0].EnvelopeJson);
        Assert.Equal(correlation, c.CorrelationId);
        Assert.Equal(b.Id, c.CausationId);
    }
}

[Trait("Category", "Unit")]
[Trait("Category", "Messaging")]
public sealed class EventBusTests
{
    [Fact]
    public async Task Zero_subscribers_succeeds()
    {
        await using var harness = await MessagingHarness.CreateAsync();
        var envelope = await PublishDirectAsync(harness, new SamplePayload("x"));
        await harness.Bus.DispatchAsync(envelope);
    }

    [Fact]
    public async Task One_subscriber_receives_once()
    {
        await using var harness = await MessagingHarness.CreateAsync();
        var handler = new CountingHandler("c1");
        harness.Subscriber.Subscribe<SamplePayload>("c1", handler);
        await harness.Bus.DispatchAsync(await PublishDirectAsync(harness, new SamplePayload("x")));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Multiple_subscribers_all_receive()
    {
        await using var harness = await MessagingHarness.CreateAsync();
        var a = new CountingHandler("a");
        var b = new CountingHandler("b");
        var c = new CountingHandler("c");
        harness.Subscriber.Subscribe<SamplePayload>("a", a);
        harness.Subscriber.Subscribe<SamplePayload>("b", b);
        harness.Subscriber.Subscribe<SamplePayload>("c", c);
        await harness.Bus.DispatchAsync(await PublishDirectAsync(harness, new SamplePayload("x")));
        Assert.Equal(1, a.Count);
        Assert.Equal(1, b.Count);
        Assert.Equal(1, c.Count);
    }

    [Fact]
    public async Task Subscriber_failure_isolates_others()
    {
        await using var harness = await MessagingHarness.CreateAsync(maxAttempts: 1);
        var a = new CountingHandler("a");
        var b = new FailingHandler("b");
        var c = new CountingHandler("c");
        harness.Subscriber.Subscribe<SamplePayload>("a", a);
        harness.Subscriber.Subscribe<SamplePayload>("b", b);
        harness.Subscriber.Subscribe<SamplePayload>("c", c);
        await harness.Bus.DispatchAsync(await PublishDirectAsync(harness, new SamplePayload("x")));
        Assert.Equal(1, a.Count);
        Assert.Equal(1, c.Count);
        var failed = await harness.Inbox.ListFailedAsync(10);
        Assert.Contains(failed, f => f.ConsumerId == "b");
    }

    [Fact]
    public async Task Inbox_deduplicates_same_consumer()
    {
        await using var harness = await MessagingHarness.CreateAsync();
        var handler = new CountingHandler("c1");
        harness.Subscriber.Subscribe<SamplePayload>("c1", handler);
        var envelope = await PublishDirectAsync(harness, new SamplePayload("x"));
        await harness.Bus.DispatchAsync(envelope);
        await harness.Bus.DispatchAsync(envelope);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task Different_consumers_both_process()
    {
        await using var harness = await MessagingHarness.CreateAsync();
        var a = new CountingHandler("a");
        var b = new CountingHandler("b");
        harness.Subscriber.Subscribe<SamplePayload>("a", a);
        harness.Subscriber.Subscribe<SamplePayload>("b", b);
        var envelope = await PublishDirectAsync(harness, new SamplePayload("x"));
        await harness.Bus.DispatchAsync(envelope);
        Assert.Equal(1, a.Count);
        Assert.Equal(1, b.Count);
    }

    private static async Task<EventEnvelope> PublishDirectAsync(MessagingHarness harness, SamplePayload payload)
    {
        await harness.Publisher.PublishAsync(payload, new PublishOptions { Durable = true });
        var row = Assert.Single(await harness.Outbox.ListRecentAsync(1));
        return harness.Serializer.DeserializeEnvelope(row.EnvelopeJson);
    }
}

[Trait("Category", "Unit")]
[Trait("Category", "Messaging")]
public sealed class RegistryAndSerializationTests
{
    [Fact]
    public void Registry_requires_type_and_version()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventContract<SamplePayload>("x", 0));
        Assert.Throws<ArgumentException>(() => new EventContract<SamplePayload>("", 1));
    }

    [Fact]
    public void Duplicate_registration_throws()
    {
        var registry = new EventRegistry();
        registry.Register(SampleContract.Create());
        Assert.Throws<InvalidOperationException>(() => registry.Register(SampleContract.Create()));
    }

    [Fact]
    public void Different_versions_coexist()
    {
        var registry = new EventRegistry();
        registry.Register(new EventContract<SamplePayload>("forgedeck.test.sample", 1));
        registry.Register(new EventContract<SamplePayloadV2>("forgedeck.test.sample", 2));
        Assert.NotNull(registry.TryGet("forgedeck.test.sample", 1));
        Assert.NotNull(registry.TryGet("forgedeck.test.sample", 2));
    }

    [Fact]
    public void Serialize_deserialize_roundtrip()
    {
        var serializer = new JsonEventSerializer();
        var payload = new BuildPipelineRunStartedEvent(Guid.NewGuid(), Guid.NewGuid(), "ci", "abc");
        var element = serializer.SerializePayload(payload);
        var back = serializer.DeserializePayload<BuildPipelineRunStartedEvent>(element);
        Assert.Equal(payload.RunId, back.RunId);
        Assert.Equal(payload.PipelineName, back.PipelineName);
    }

    [Fact]
    public void Additive_unknown_fields_are_ignored()
    {
        var serializer = new JsonEventSerializer();
        var json = """{"name":"ok","extra":"field"}""";
        var element = JsonDocument.Parse(json).RootElement;
        var payload = serializer.DeserializePayload<SamplePayload>(element);
        Assert.Equal("ok", payload.Name);
    }
}

[Trait("Category", "Unit")]
[Trait("Category", "Messaging")]
public sealed class OutboxTransactionTests
{
    [Fact]
    public async Task Outbox_created_in_pending_state()
    {
        await using var harness = await MessagingHarness.CreateAsync();
        await harness.Publisher.PublishAsync(new SamplePayload("x"));
        var row = Assert.Single(await harness.Outbox.ListRecentAsync(10));
        Assert.Equal(OutboxDispatchState.Pending, row.DispatchState);
        Assert.Equal(SampleContract.Type, row.EventType);
        Assert.Equal(1, row.Version);
    }

    [Fact]
    public async Task Rollback_removes_outbox_event()
    {
        await using var harness = await MessagingHarness.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await harness.UnitOfWork.ExecuteAsync(async _ =>
            {
                await harness.Publisher.PublishAsync(new SamplePayload("x"));
                throw new InvalidOperationException("boom");
            });
        });

        Assert.Empty(await harness.Outbox.ListRecentAsync(10));
    }

    [Fact]
    public async Task Successful_retry_marks_processed()
    {
        await using var harness = await MessagingHarness.CreateAsync(maxAttempts: 5);
        var handler = new FlakyHandler("flaky", failTimes: 2);
        harness.Subscriber.Subscribe<SamplePayload>("flaky", handler);
        var envelope = (await PublishAndGet(harness)).envelope;
        await harness.Bus.DispatchAsync(envelope);
        Assert.Equal(DeliveryState.Failed, (await harness.Inbox.GetAsync(envelope.Id, "flaky"))!.State);
        await harness.Inbox.ResetForRetryAsync(envelope.Id, "flaky");
        await harness.Bus.DispatchAsync(envelope);
        await harness.Inbox.ResetForRetryAsync(envelope.Id, "flaky");
        await harness.Bus.DispatchAsync(envelope);
        Assert.Equal(DeliveryState.Processed, (await harness.Inbox.GetAsync(envelope.Id, "flaky"))!.State);
        Assert.Equal(3, handler.Attempts);
    }

    private static async Task<(EventEnvelope envelope, OutboxRecord row)> PublishAndGet(MessagingHarness harness)
    {
        await harness.Publisher.PublishAsync(new SamplePayload("x"));
        var row = Assert.Single(await harness.Outbox.ListRecentAsync(1));
        return (harness.Serializer.DeserializeEnvelope(row.EnvelopeJson), row);
    }
}

file static class SampleContract
{
    public const string Type = "forgedeck.test.sample";
    public static EventContract<SamplePayload> Create() => new(Type, 1);
}

file sealed record SamplePayloadV2(string Name, int Count);

file sealed class CountingHandler(string id) : IEventHandler<SamplePayload>
{
    public string ConsumerId => id;
    public int Count { get; private set; }
    public Task HandleAsync(EventEnvelope<SamplePayload> envelope, CancellationToken cancellationToken = default)
    {
        Count++;
        return Task.CompletedTask;
    }
}

file sealed class FailingHandler(string id) : IEventHandler<SamplePayload>
{
    public string ConsumerId => id;
    public Task HandleAsync(EventEnvelope<SamplePayload> envelope, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("fail");
}

file sealed class FlakyHandler(string id, int failTimes) : IEventHandler<SamplePayload>
{
    public string ConsumerId => id;
    public int Attempts { get; private set; }
    public Task HandleAsync(EventEnvelope<SamplePayload> envelope, CancellationToken cancellationToken = default)
    {
        Attempts++;
        if (Attempts <= failTimes)
        {
            throw new InvalidOperationException("transient");
        }

        return Task.CompletedTask;
    }
}

internal sealed class MessagingHarness : IAsyncDisposable
{
    private readonly string _path;

    private MessagingHarness(
        string path,
        IEventPublisher publisher,
        IEventOutbox outbox,
        IEventInbox inbox,
        IEventBus bus,
        EventSubscriber subscriber,
        IEventSerializer serializer,
        IUnitOfWork unitOfWork)
    {
        _path = path;
        Publisher = publisher;
        Outbox = outbox;
        Inbox = inbox;
        Bus = bus;
        Subscriber = subscriber;
        Serializer = serializer;
        UnitOfWork = unitOfWork;
    }

    public IEventPublisher Publisher { get; }
    public IEventOutbox Outbox { get; }
    public IEventInbox Inbox { get; }
    public IEventBus Bus { get; }
    public EventSubscriber Subscriber { get; }
    public IEventSerializer Serializer { get; }
    public IUnitOfWork UnitOfWork { get; }

    public static Task<MessagingHarness> CreateAsync(int maxAttempts = 5)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fd-msg-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var dbFactory = new MessagingDbContextFactory(options);
        using (var db = dbFactory.CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        var serializer = new JsonEventSerializer();
        var registry = new EventRegistry();
        registry.Register(SampleContract.Create());
        var outbox = new EfEventOutbox(dbFactory, serializer);
        var inbox = new EfEventInbox(dbFactory);
        var subscriber = new EventSubscriber();
        var retry = Options.Create(new EventRetryOptions { MaxAttempts = maxAttempts, InitialBackoffMilliseconds = 1 });
        var bus = new EventBus(registry, serializer, subscriber, inbox, retry, NullLogger<EventBus>.Instance);
        var publisher = new EventPublisher(registry, serializer, outbox, bus);
        var uow = new EfUnitOfWork(dbFactory);
        return Task.FromResult(new MessagingHarness(path, publisher, outbox, inbox, bus, subscriber, serializer, uow));
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
            // best-effort cleanup
        }

        return ValueTask.CompletedTask;
    }
}

file sealed class MessagingDbContextFactory(DbContextOptions<MessagingDbContext> options)
    : IDbContextFactory<MessagingDbContext>
{
    public MessagingDbContext CreateDbContext() => new(options);
}
