using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Identity;

namespace Core.Tests;

public sealed class OrganisationPermissionsTests
{
    [Fact]
    public void Owner_has_destructive_and_licensing_permissions()
    {
        var permissions = OrganisationPermissions.ForRole(OrganisationRole.Owner);
        Assert.Contains(OrganisationPermissions.OrganisationDestroy, permissions);
        Assert.Contains(OrganisationPermissions.LicensingManage, permissions);
        Assert.Contains(OrganisationPermissions.ModulesManage, permissions);
        Assert.Contains(OrganisationPermissions.TeamsManage, permissions);
        Assert.Contains("review.manage", permissions);
    }

    [Fact]
    public void Admin_has_management_but_not_owner_only_actions()
    {
        var permissions = OrganisationPermissions.ForRole(OrganisationRole.Admin);
        Assert.Contains(OrganisationPermissions.UsersManage, permissions);
        Assert.Contains(OrganisationPermissions.TeamsManage, permissions);
        Assert.Contains(OrganisationPermissions.ProjectsCreate, permissions);
        Assert.DoesNotContain(OrganisationPermissions.OrganisationDestroy, permissions);
        Assert.DoesNotContain(OrganisationPermissions.LicensingManage, permissions);
    }

    [Fact]
    public void Member_has_minimal_organisation_access_only()
    {
        var permissions = OrganisationPermissions.ForRole(OrganisationRole.Member);
        Assert.Contains(OrganisationPermissions.OrganisationRead, permissions);
        Assert.Contains(OrganisationPermissions.UsersRead, permissions);
        Assert.Contains(OrganisationPermissions.ProjectsReadAccessible, permissions);
        Assert.DoesNotContain("review.read", permissions);
        Assert.DoesNotContain(OrganisationPermissions.UsersManage, permissions);
        Assert.DoesNotContain(OrganisationPermissions.TeamsManage, permissions);
        Assert.DoesNotContain(OrganisationPermissions.ProjectsCreate, permissions);
    }
}

public sealed class SlugRulesTests
{
    [Theory]
    [InlineData("atlas", true)]
    [InlineData("my-project", true)]
    [InlineData("a1", true)]
    [InlineData("-invalid", false)]
    [InlineData("invalid-", false)]
    [InlineData("invalid_", false)]
    [InlineData("a", false)]
    [InlineData("", false)]
    public void Project_and_team_slug_validation(string slug, bool expected) =>
        Assert.Equal(expected, SlugRules.IsValid(slug));

    [Theory]
    [InlineData("rowan", true)]
    [InlineData("rowan_smith", true)]
    [InlineData("r", false)]
    [InlineData("-rowan", false)]
    [InlineData("rowan@x", false)]
    public void Username_validation(string username, bool expected) =>
        Assert.Equal(expected, SlugRules.IsValidUsername(username));

    [Fact]
    public void Normalize_produces_url_safe_slug()
    {
        Assert.Equal("northstar-engineering", SlugRules.Normalize("Northstar Engineering"));
        Assert.Equal("ATL", SlugRules.KeyFromName("Atlas"));
    }
}
