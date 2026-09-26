using ForgeDeck.Contracts.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ForgeDeck.Core.Api;

public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/search", (string? q, int? limit, ISearchService search) =>
        {
            var hits = search.Search(q, limit ?? 20);
            return Results.Ok(new { hits, query = q ?? string.Empty });
        });
    }
}
