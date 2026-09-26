namespace ForgeDeck.Core.Search;

/// <summary>Visibility rules for project context switcher chrome.</summary>
public static class ShellChromeRules
{
    public static bool ShowProjectSwitcher(bool isOrgRoute) => !isOrgRoute;

    public static bool ShowSwitcherChevron(bool isOrgRoute, bool switcherEnabled = true) =>
        !isOrgRoute && switcherEnabled;
}
