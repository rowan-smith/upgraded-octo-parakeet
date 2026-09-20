using Microsoft.AspNetCore.Identity;
using Platform.Core.Application;
using Platform.Core.Domain;
using Platform.Core.Persistence;

namespace Tests.Unit;

internal sealed class TenancyFixture : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"forgedeck-{Guid.NewGuid():N}.db");

    public TenancyFixture()
    {
        var connections = new SqliteConnectionFactory($"Data Source={_path};Pooling=False");
        Store = new SqliteTenancyStore(connections, new CoreSchemaInitializer(connections));
        Passwords = new PasswordHasher<UserAccount>();
        Setup = new SetupService(Store, Passwords);
        Memberships = new MembershipService(Store, Passwords);
        Projects = new ProjectService(Store);
        Access = new ProjectAccessService(Store);
        Teams = new TeamService(Store);
        Invitations = new InvitationService(Store, Passwords);
        Auth = new AuthService(Store, Passwords);
    }

    public ITenancyStore Store { get; }
    public IPasswordHasher<UserAccount> Passwords { get; }
    public SetupService Setup { get; }
    public MembershipService Memberships { get; }
    public ProjectService Projects { get; }
    public ProjectAccessService Access { get; }
    public TeamService Teams { get; }
    public InvitationService Invitations { get; }
    public AuthService Auth { get; }

    public UserAccount Bootstrap(string email = "maya@northstar.dev", string username = "maya", string name = "Maya Chen") =>
        Setup.Bootstrap(new SetupRequest("Northstar Labs", "Engineering", name, username, email, "password123"));

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
