using System.Text.Json;
using System.Text.Json.Nodes;
using ForgeDeck.Review.Domain;

namespace ForgeDeck.Review.Application;

public static class ChangeResponse
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static object Create(Change change, ApprovalPolicyResult mergePolicy)
    {
        var node = JsonSerializer.SerializeToNode(change, Options)!.AsObject();
        node["mergePolicy"] = JsonSerializer.SerializeToNode(mergePolicy, Options);
        node["canMerge"] = change.Status is not ("Merged" or "Closed") && mergePolicy.Satisfied;
        return node;
    }
}
