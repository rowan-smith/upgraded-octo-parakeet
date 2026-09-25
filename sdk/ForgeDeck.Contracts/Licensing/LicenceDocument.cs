using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeDeck.Contracts.Licensing;

/// <summary>
/// Signed offline licence document. Prefer module entitlement tiers; capability lists are optional overrides.
/// Signing today uses RSA-SHA256 PKCS#1 (embedded public key). Ed25519 is a planned migration.
/// </summary>
public sealed class LicenceDocument
{
    public int Version { get; set; } = 1;
    public string? LicenceId { get; set; }
    public string? Customer { get; set; }
    public Guid OrganisationId { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    /// <summary>Hard expiry for the commercial grant. Null = perpetual rights (see Maintenance).</summary>
    public DateTimeOffset? ValidUntil { get; set; }
    /// <summary>When set, the licence is bound to this ForgeDeck instance and will not activate elsewhere.</summary>
    public Guid? InstanceId { get; set; }
    public LicenceCapacityDocument? Capacity { get; set; }
    public LicenceMaintenanceDocument? Maintenance { get; set; }
    public LicenceProductVersionDocument? ProductVersion { get; set; }
    public Dictionary<string, bool>? Features { get; set; }
    public Dictionary<string, LicenceModuleDocument> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Signature { get; set; }
}

public sealed class LicenceCapacityDocument
{
    public int? Users { get; set; }
}

public sealed class LicenceMaintenanceDocument
{
    public DateTimeOffset? Until { get; set; }
    public DateTimeOffset? SecurityUntil { get; set; }
    public DateTimeOffset? SupportUntil { get; set; }
}

public sealed class LicenceProductVersionDocument
{
    public int? MaxMajor { get; set; }
}

/// <summary>
/// Per-module grant. JSON may be a string ("team") or an object { edition, capabilities, expiresAt }.
/// </summary>
[JsonConverter(typeof(LicenceModuleDocumentJsonConverter))]
public sealed class LicenceModuleDocument
{
    public string Edition { get; set; } = "Community";
    public List<string> Capabilities { get; set; } = [];
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class LicenceModuleDocumentJsonConverter : JsonConverter<LicenceModuleDocument>
{
    public override LicenceModuleDocument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new LicenceModuleDocument { Edition = reader.GetString() ?? "Community" };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected string or object for module entitlement.");
        }

        var doc = new LicenceModuleDocument();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();
            switch (name?.ToLowerInvariant())
            {
                case "edition":
                    doc.Edition = reader.GetString() ?? "Community";
                    break;
                case "capabilities":
                    if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                doc.Capabilities.Add(reader.GetString()!);
                            }
                        }
                    }
                    break;
                case "expiresat":
                    if (reader.TokenType != JsonTokenType.Null)
                    {
                        doc.ExpiresAt = reader.GetDateTimeOffset();
                    }

                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return doc;
    }

    public override void Write(Utf8JsonWriter writer, LicenceModuleDocument value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("edition", value.Edition);
        writer.WritePropertyName("capabilities");
        writer.WriteStartArray();
        foreach (var cap in value.Capabilities)
        {
            writer.WriteStringValue(cap);
        }

        writer.WriteEndArray();
        if (value.ExpiresAt is { } expires)
        {
            writer.WriteString("expiresAt", expires);
        }

        writer.WriteEndObject();
    }
}
