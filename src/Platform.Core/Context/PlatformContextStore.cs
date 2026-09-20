using Platform.Core.Domain;
using Platform.Core.Identity;

namespace Platform.Core.Context;

public sealed class PlatformContextStore
{
    public Organisation Organisation { get; } = new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Northstar Labs", "northstar");
    public Project Project { get; } = new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Atlas", "ATL", "github.com/northstar/atlas");
    public PlatformUser User { get; } = new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "Maya Chen", "maya@northstar.dev",
        new HashSet<string>(["core.integration.manage", "source.repository.read", "source.repository.connect", "review.read", "review.comment", "review.request", "review.approve", "review.merge", "review.manage"]));
}
