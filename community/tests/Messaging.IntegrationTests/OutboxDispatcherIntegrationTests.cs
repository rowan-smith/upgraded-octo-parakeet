using ForgeDeck.Contracts.Events;
using ForgeDeck.Git.Contracts.Events;
using ForgeDeck.Messaging.Bus;
using ForgeDeck.Messaging.Dispatch;
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

namespace Messaging.IntegrationTests;

[Trait("Category", "Integration")]
[Trait("Category", "Messaging")]
public sealed class OutboxDispatcherIntegrationTests
{
    [Fact]
    public async Task Dispatcher_delivers_and_marks_outbox()
    {
        await using var db = await TestDb.CreateAsync();
        var handler = new CaptureHandler();
        db.Subscriber.Subscribe<GitRepositoryPushEvent>("forgedeck.build.git-push", handler);

        await db.Publisher.PublishAsync(new GitRepositoryPushEvent(Guid.NewGuid(), "ATL", "org/repo", "main", "abc123"));
        var dispatcher = new OutboxDispatcherHostedService(
            db.Outbox, db.Serializer, db.Bus,
            Options.Create(new OutboxDispatcherOptions { BatchSize = 10, Enabled = true }),
            NullLogger<OutboxDispatcherHostedService>.Instance);

        await dispatcher.DispatchPendingOnceAsync();

        Assert.Single(handler.Received);
        var row = Assert.Single(await db.Outbox.ListRecentAsync(10));
        Assert.Equal(OutboxDispatchState.Dispatched, row.DispatchState);
    }

    [Fact]
    public async Task Restart_eventually_delivers_pending_outbox()
    {
        await using var db = await TestDb.CreateAsync();
        var handler = new CaptureHandler();
        db.Subscriber.Subscribe<GitRepositoryPushEvent>("consumer", handler);
        await db.Publisher.PublishAsync(new GitRepositoryPushEvent(Guid.NewGuid(), "ATL", "org/repo", "main", "abc123"));

        // Simulate restart: new dispatcher instance, same DB
        var dispatcher = new OutboxDispatcherHostedService(
            db.Outbox, db.Serializer, db.Bus,
            Options.Create(new OutboxDispatcherOptions()),
            NullLogger<OutboxDispatcherHostedService>.Instance);
        await dispatcher.DispatchPendingOnceAsync();
        Assert.Single(handler.Received);
    }

    [Fact]
    public async Task Duplicate_dispatch_is_idempotent_per_consumer()
    {
        await using var db = await TestDb.CreateAsync();
        var handler = new CaptureHandler();
        db.Subscriber.Subscribe<GitRepositoryPushEvent>("consumer", handler);
        await db.Publisher.PublishAsync(new GitRepositoryPushEvent(Guid.NewGuid(), "ATL", "org/repo", "main", "abc123"));
        var row = Assert.Single(await db.Outbox.ListRecentAsync(1));
        var envelope = db.Serializer.DeserializeEnvelope(row.EnvelopeJson);
        await db.Bus.DispatchAsync(envelope);
        await db.Bus.DispatchAsync(envelope);
        Assert.Single(handler.Received);
    }

    [Fact]
    public async Task Zero_subscribers_publication_succeeds()
    {
        await using var db = await TestDb.CreateAsync();
        await db.Publisher.PublishAsync(new GitRepositoryPushEvent(Guid.NewGuid(), "ATL", "org/repo", "main", "abc123"));
        var dispatcher = new OutboxDispatcherHostedService(
            db.Outbox, db.Serializer, db.Bus,
            Options.Create(new OutboxDispatcherOptions()),
            NullLogger<OutboxDispatcherHostedService>.Instance);
        await dispatcher.DispatchPendingOnceAsync();
        var row = Assert.Single(await db.Outbox.ListRecentAsync(10));
        Assert.Equal(OutboxDispatchState.Dispatched, row.DispatchState);
    }

    [Fact]
    public async Task Correlation_propagates_across_handler_publish()
    {
        await using var db = await TestDb.CreateAsync();
        db.Registry.Register(new EventContract<FollowUpEvent>("forgedeck.test.follow-up", 1));
        var followUps = new List<EventEnvelope<FollowUpEvent>>();
        db.Subscriber.Subscribe<GitRepositoryPushEvent>("producer", new PublishingHandler(db.Publisher));
        db.Subscriber.Subscribe<FollowUpEvent>("consumer", new FollowUpCapture(followUps));

        await db.Publisher.PublishAsync(new GitRepositoryPushEvent(Guid.NewGuid(), "ATL", "org/repo", "main", "abc123"));
        var dispatcher = new OutboxDispatcherHostedService(
            db.Outbox, db.Serializer, db.Bus,
            Options.Create(new OutboxDispatcherOptions()),
            NullLogger<OutboxDispatcherHostedService>.Instance);
        await dispatcher.DispatchPendingOnceAsync();
        await dispatcher.DispatchPendingOnceAsync();

        Assert.NotEmpty(followUps);
        var push = db.Serializer.DeserializeEnvelope((await db.Outbox.ListRecentAsync(10)).Last(r => r.EventType.Contains("repository-push")).EnvelopeJson);
        Assert.Equal(push.CorrelationId, followUps[0].CorrelationId);
        Assert.Equal(push.Id, followUps[0].CausationId);
    }
}

file sealed record FollowUpEvent(string Note);

file sealed class CaptureHandler : IEventHandler<GitRepositoryPushEvent>
{
    public string ConsumerId => "capture";
    public List<GitRepositoryPushEvent> Received { get; } = [];
    public Task HandleAsync(EventEnvelope<GitRepositoryPushEvent> envelope, CancellationToken cancellationToken = default)
    {
        Received.Add(envelope.Data);
        return Task.CompletedTask;
    }
}

file sealed class PublishingHandler(IEventPublisher publisher) : IEventHandler<GitRepositoryPushEvent>
{
    public string ConsumerId => "producer";
    public Task HandleAsync(EventEnvelope<GitRepositoryPushEvent> envelope, CancellationToken cancellationToken = default) =>
        publisher.PublishAsync(new FollowUpEvent("next"), cancellationToken: cancellationToken);
}

file sealed class FollowUpCapture(List<EventEnvelope<FollowUpEvent>> sink) : IEventHandler<FollowUpEvent>
{
    public string ConsumerId => "consumer";
    public Task HandleAsync(EventEnvelope<FollowUpEvent> envelope, CancellationToken cancellationToken = default)
    {
        sink.Add(envelope);
        return Task.CompletedTask;
    }
}

file sealed class TestDb : IAsyncDisposable
{
    private readonly string _path;

    private TestDb(
        string path,
        EventRegistry registry,
        IEventPublisher publisher,
        IEventOutbox outbox,
        IEventBus bus,
        EventSubscriber subscriber,
        IEventSerializer serializer)
    {
        _path = path;
        Registry = registry;
        Publisher = publisher;
        Outbox = outbox;
        Bus = bus;
        Subscriber = subscriber;
        Serializer = serializer;
    }

    public EventRegistry Registry { get; }
    public IEventPublisher Publisher { get; }
    public IEventOutbox Outbox { get; }
    public IEventBus Bus { get; }
    public EventSubscriber Subscriber { get; }
    public IEventSerializer Serializer { get; }

    public static Task<TestDb> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fd-msg-int-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<MessagingDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var dbFactory = new MessagingDbContextFactory(options);
        using (var db = dbFactory.CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        var serializer = new JsonEventSerializer();
        var registry = new EventRegistry();
        foreach (var contract in GitEventContracts.All())
        {
            registry.Register(contract);
        }

        var outbox = new EfEventOutbox(dbFactory, serializer);
        var inbox = new EfEventInbox(dbFactory);
        var subscriber = new EventSubscriber();
        var bus = new EventBus(registry, serializer, subscriber, inbox,
            Options.Create(new EventRetryOptions { MaxAttempts = 3 }),
            NullLogger<EventBus>.Instance);
        var publisher = new EventPublisher(registry, serializer, outbox, bus);
        return Task.FromResult(new TestDb(path, registry, publisher, outbox, bus, subscriber, serializer));
    }

    public ValueTask DisposeAsync()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
        return ValueTask.CompletedTask;
    }
}

file sealed class MessagingDbContextFactory(DbContextOptions<MessagingDbContext> options)
    : IDbContextFactory<MessagingDbContext>
{
    public MessagingDbContext CreateDbContext() => new(options);
}
