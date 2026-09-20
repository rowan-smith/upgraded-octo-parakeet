using System.Text.Json;
using System.Text.Json.Serialization;
using Platform.Contracts.Licensing;
using Platform.Core.Licensing;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: LicenceSigner <private-key.pem> <licence.json> [output.json]");
    Console.Error.WriteLine("Signs the licence document in-place (or writes output.json).");
    return 1;
}

var privateKeyPath = args[0];
var licencePath = args[1];
var outputPath = args.Length > 2 ? args[2] : licencePath;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

var document = JsonSerializer.Deserialize<LicenceDocument>(await File.ReadAllTextAsync(licencePath), jsonOptions)
    ?? throw new InvalidOperationException("Licence file was empty.");

using var privateKey = LicenceCryptography.ImportPrivateKeyPem(await File.ReadAllTextAsync(privateKeyPath));
document.Signature = LicenceCryptography.Sign(document, privateKey);

await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(document, jsonOptions));
Console.WriteLine($"Signed licence for {document.OrganisationId} -> {outputPath}");
return 0;
