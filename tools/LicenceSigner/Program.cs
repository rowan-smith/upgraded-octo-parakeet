using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Core.Licensing;

if (args.Length >= 1 && args[0].Equals("sku", StringComparison.OrdinalIgnoreCase))
{
    // LicenceSigner sku <SKU> <organisationId> [instanceId] [output.json]
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: LicenceSigner sku <SKU> <organisationId> [instanceId] [output.json]");
        return 1;
    }

    var sku = args[1];
    var orgId = Guid.Parse(args[2]);
    Guid? instanceId = args.Length > 3 && Guid.TryParse(args[3], out var iid) ? iid : null;
    var output = args.Length > 4 ? args[4] : "licence.unsigned.json";
    var document = LicenceSkuCatalog.CreateDocument(sku, orgId, instanceId);
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    await File.WriteAllTextAsync(output, JsonSerializer.Serialize(document, jsonOptions));
    Console.WriteLine($"Wrote unsigned {sku} licence -> {output}");
    return 0;
}

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: LicenceSigner <ed25519-private-key.pem> <licence.json> [output.json]");
    Console.Error.WriteLine("   or: LicenceSigner sku <SKU> <organisationId> [instanceId] [output.json]");
    Console.Error.WriteLine("Signs the licence document with Ed25519 (or writes output.json).");
    return 1;
}

var privateKeyPath = args[0];
var licencePath = args[1];
var outputPath = args.Length > 2 ? args[2] : licencePath;

var signOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var doc = JsonSerializer.Deserialize<LicenceDocument>(await File.ReadAllTextAsync(licencePath), signOptions)
    ?? throw new InvalidOperationException("Licence file was empty.");

var keyPair = LicenceKeyPair.FromPrivatePem(await File.ReadAllTextAsync(privateKeyPath));
doc.Signature = LicenceCryptography.Sign(doc, keyPair);

await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(doc, signOptions));
Console.WriteLine($"Signed licence for {doc.OrganisationId} -> {outputPath}");
return 0;
