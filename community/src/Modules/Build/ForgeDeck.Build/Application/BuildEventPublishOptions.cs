using System.Security.Cryptography;
using ForgeDeck.Contracts.Events;

namespace ForgeDeck.Build.Application;

internal static class BuildEventPublishOptions
{
    public static PublishOptions Default { get; } = new()
    {
        Actor = new EventActor(ActorType.Extension, "forgedeck.build", "Build"),
        Publisher = "forgedeck.build"
    };
}

internal static class EventIdempotency
{
    public static Guid Combine(Guid left, Guid right)
    {
        Span<byte> bytes = stackalloc byte[32];
        left.TryWriteBytes(bytes);
        right.TryWriteBytes(bytes[16..]);
        var hash = SHA256.HashData(bytes);
        return new Guid(hash.AsSpan(0, 16));
    }
}
