using ForgeDeck.Contracts.Search;
using ForgeDeck.Core.Application;
using ForgeDeck.Core.Persistence;

namespace ForgeDeck.Core.Search;

public sealed class SearchService(
    ITenancyStore store,
    ProjectService projects,
    IEnumerable<ISearchContributor> contributors) : ISearchService
{
    private static readonly SearchHit[] Shortcuts =
    [
        new("route", "changes", "Pull requests", "/changes", "Review"),
        new("route", "pipelines", "Pipelines", "/pipelines", "Build"),
        new("route", "projects", "Projects", "/projects", "Organisation"),
        new("route", "settings", "Project settings", "/settings", "Settings")
    ];

    public IReadOnlyList<SearchHit> Search(string? query, int limit = 20)
    {
        limit = Math.Clamp(limit, 1, 50);
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            return Shortcuts.Take(limit).ToArray();
        }

        var hits = new List<SearchHit>(limit);
        foreach (var project in projects.ListProjects())
        {
            if (!Matches(q, project.Name, project.Slug, project.Key, project.Description))
            {
                continue;
            }

            hits.Add(new SearchHit(
                "project",
                project.Id.ToString("D"),
                project.Name,
                "/overview",
                string.IsNullOrWhiteSpace(project.Description) ? $"/{project.Slug}" : project.Description));
            if (hits.Count >= limit)
            {
                return hits;
            }
        }

        foreach (var membership in store.ListMemberships())
        {
            var user = store.FindUser(membership.UserId);
            if (user is null)
            {
                continue;
            }

            var profile = store.GetProfile(membership.UserId);
            var display = profile?.DisplayName ?? user.Username;
            if (!Matches(q, display, user.Username, user.Email))
            {
                continue;
            }

            hits.Add(new SearchHit(
                "person",
                user.Id.ToString("D"),
                display,
                "/people",
                $"@{user.Username} · {membership.Role}"));
            if (hits.Count >= limit)
            {
                return hits;
            }
        }

        var remaining = limit - hits.Count;
        if (remaining <= 0)
        {
            return hits;
        }

        foreach (var contributor in contributors)
        {
            var contributed = contributor.Search(q, remaining);
            foreach (var hit in contributed)
            {
                hits.Add(hit);
                remaining--;
                if (remaining <= 0)
                {
                    return hits;
                }
            }
        }

        return hits;
    }

    private static bool Matches(string query, params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && value.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
