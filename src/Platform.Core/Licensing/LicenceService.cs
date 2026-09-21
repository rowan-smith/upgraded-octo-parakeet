using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Contracts.Capabilities;
using Platform.Contracts.Licensing;
using Platform.Contracts.Modules;
using Platform.Core.Capabilities;
using Platform.Core.Domain;
using Platform.Core.Persistence;

namespace Platform.Core.Licensing;

public sealed record LicenceModuleStatus(string ModuleId, string Name, bool Installed, string Edition);
public sealed record LicenceStatusView(
    LicenceMode Mode,
    string Status,
    string? CustomerId,
    string? LicenceId,
    DateTimeOffset? IssuedAt,
    DateTimeOffset? ExpiresAt,
    Guid InstanceId,
    IReadOnlyList<LicenceModuleStatus> Modules,
    IReadOnlyList<string> Capabilities,
    int CapabilityCount);

public sealed class LicenceService(
    ITenancyStore store,
    ManagedLicenceEntitlementStore entitlements,
    IEnumerable<IPlatformModule> modules,
    ICapabilityService capabilities,
    IOptions<LicensingOptions> options,
    IHostEnvironment environment,
    ILogger<LicenceService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public LicenceStatusView GetStatus()
    {
        store.EnsureInstanceId();
        var instance = store.GetInstance();
        var organisationId = store.GetOrganisation()?.Id ?? KnownIds.OrganisationId;
        var granted = capabilities.ForOrganisation(organisationId);
        var moduleStatuses = modules
            .GroupBy(m => BaseModuleId(m.Manifest.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var community = group.FirstOrDefault(m => !m.Manifest.Edition.Equals("Commercial", StringComparison.OrdinalIgnoreCase))
                                ?? group.First();
                var edition = capabilities.EditionFor(organisationId, community.Manifest.Id, community.Manifest.Edition);
                return new LicenceModuleStatus(community.Manifest.Id, community.Manifest.Name, true, edition);
            })
            .OrderBy(m => m.Name)
            .ToArray();

        var active = store.GetActiveLicence();
        var status = instance.LicenceMode switch
        {
            LicenceMode.Commercial when active?.ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow => "Expired",
            LicenceMode.Commercial => "Active",
            LicenceMode.Community => "Community",
            _ => "None"
        };

        return new LicenceStatusView(
            instance.LicenceMode,
            status,
            active?.CustomerId,
            active?.LicenceId,
            active?.IssuedAt,
            active?.ExpiresAt,
            instance.InstanceId,
            moduleStatuses,
            granted.OrderBy(c => c).ToArray(),
            granted.Count);
    }

    public void SelectCommunity()
    {
        var instance = store.GetInstance();
        store.EnsureInstanceId();
        instance = store.GetInstance();
        store.MarkActiveLicence(LicenceRecordStatus.Removed);
        entitlements.ClearEntitlement();
        instance.LicenceMode = LicenceMode.Community;
        store.SaveInstance(instance);
        store.ReplaceActiveLicence(new LicenceRecord
        {
            Mode = LicenceMode.Community,
            Status = LicenceRecordStatus.Active,
            LicenceId = "community"
        });
        store.AddLicenceHistory(new LicenceHistoryEntry
        {
            Action = "licence.community_selected",
            Mode = LicenceMode.Community,
            Detail = "Community mode selected"
        });
    }

    public LicenceStatusView InstallCommercial(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Licence payload is required.");

        OrganisationLicenceEntitlement entitlement;
        LicenceDocument document;
        try
        {
            document = JsonSerializer.Deserialize<LicenceDocument>(payload.Trim(), JsonOptions)
                ?? throw new InvalidOperationException("Licence could not be validated.");
            entitlement = ManagedLicenceEntitlementStore.ParseVerified(payload.Trim(), entitlements.PublicKey)
                ?? throw new InvalidOperationException("Licence could not be validated.");
        }
        catch (CryptographicException)
        {
            store.AddLicenceHistory(new LicenceHistoryEntry
            {
                Action = "licence.validation_failed",
                Detail = "Signature invalid"
            });
            throw new ArgumentException("Licence could not be validated. Reason: Signature invalid.");
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            store.AddLicenceHistory(new LicenceHistoryEntry
            {
                Action = "licence.validation_failed",
                Detail = "Licence format unsupported"
            });
            throw new ArgumentException("Licence could not be validated. Reason: Licence format unsupported.");
        }

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset? earliestExpiry = null;
        foreach (var module in entitlement.Modules.Values)
        {
            if (module.ExpiresAt is { } expires)
                earliestExpiry = earliestExpiry is null ? expires : (expires < earliestExpiry ? expires : earliestExpiry);
        }

        if (earliestExpiry is { } exp && exp <= now)
            throw new ArgumentException("Licence could not be validated. Reason: Licence has expired.");

        var hadCommercial = store.GetInstance().LicenceMode == LicenceMode.Commercial && store.GetActiveLicence() is not null;
        var record = new LicenceRecord
        {
            Mode = LicenceMode.Commercial,
            Status = LicenceRecordStatus.Active,
            LicenceId = document.OrganisationId.ToString("N")[..8],
            CustomerId = document.OrganisationId.ToString(),
            IssuedAt = document.IssuedAt,
            ExpiresAt = earliestExpiry,
            Payload = payload.Trim(),
            Signature = document.Signature
        };
        store.ReplaceActiveLicence(record);
        entitlements.SetEntitlement(entitlement);

        var instance = store.GetInstance();
        store.EnsureInstanceId();
        instance = store.GetInstance();
        instance.LicenceMode = LicenceMode.Commercial;
        store.SaveInstance(instance);

        // Persist a copy to the configured file path when possible (non-fatal).
        TryWriteLicenceFile(payload.Trim());

        store.AddLicenceHistory(new LicenceHistoryEntry
        {
            Action = hadCommercial ? "licence.replaced" : "licence.installed",
            Mode = LicenceMode.Commercial,
            LicenceId = record.LicenceId,
            Detail = "Commercial licence validated"
        });

        return GetStatus();
    }

    public void RemoveCommercial()
    {
        store.MarkActiveLicence(LicenceRecordStatus.Removed);
        entitlements.ClearEntitlement();
        var instance = store.GetInstance();
        instance.LicenceMode = LicenceMode.Community;
        store.SaveInstance(instance);
        store.ReplaceActiveLicence(new LicenceRecord
        {
            Mode = LicenceMode.Community,
            Status = LicenceRecordStatus.Active,
            LicenceId = "community"
        });
        store.AddLicenceHistory(new LicenceHistoryEntry
        {
            Action = "licence.removed",
            Mode = LicenceMode.Community,
            Detail = "Commercial licence removed; Community mode active"
        });
    }

    private void TryWriteLicenceFile(string payload)
    {
        try
        {
            var relative = options.Value.LicencePath;
            if (string.IsNullOrWhiteSpace(relative)) return;
            var path = Path.IsPathRooted(relative) ? relative : Path.Combine(environment.ContentRootPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Licence installed in database but file mirror failed.");
        }
    }

    private static string BaseModuleId(string moduleId) =>
        moduleId.EndsWith("-commercial", StringComparison.OrdinalIgnoreCase)
            ? moduleId[..^"-commercial".Length]
            : moduleId;
}
