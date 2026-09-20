namespace Platform.Core.Identity;

public sealed record PlatformUser(Guid Id, string Name, string Email, IReadOnlySet<string> Permissions);
