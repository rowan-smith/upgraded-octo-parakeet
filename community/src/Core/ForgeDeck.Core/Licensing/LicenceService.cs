using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeDeck.Contracts.Capabilities;
using ForgeDeck.Contracts.Licensing;
using ForgeDeck.Contracts.Modules;
using ForgeDeck.Core.Capabilities;
using ForgeDeck.Core.Domain;
using ForgeDeck.Core.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ForgeDeck.Core.Licensing;

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
    int CapabilityCount,
    SeatCapacityStatus? Seats = null,
    MaintenanceStatus? Maintenance = null);

public sealed class LicenceService(
    ITenancyStore store,
    ManagedLicenceEntitlementStore entitlements,
    IEnumerable<IPlatformModule> modules,
    ICapabilityService capabilities,
    IEntitlementService entitlementService,
    IOptions<LicensingOptions> options,
    IHostEnvironment environment,
    ILogger<LicenceService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public LicenceStatusView GetStatus()
    {
        store.EnsureInstanceId();
        var instance = store.GetInstance();
        var organisationId = store.GetOrganisation()?.Id ?? KnownIds.OrganisationId;
        var granted = capabilities.ForOrganisation(organisationId);
        var moduleStatuses = modules
            .GroupBy(m => PlatformModules.Normalize(m.Manifest.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var community = group.FirstOrDefault(m =>
                                    !m.Manifest.Edition.Equals("Commercial", StringComparison.OrdinalIgnoreCase) &&
                                    !m.Manifest.Edition.Equals("Team", StringComparison.OrdinalIgnoreCase) &&
                                    !m.Manifest.Edition.Equals("Enterprise", StringComparison.OrdinalIgnoreCase))
                                ?? group.First();
                var level = entitlementService.GetEntitlement(organisationId, group.Key);
                return new LicenceModuleStatus(
                    group.Key,
                    community.Manifest.Name,
                    true,
                    EntitlementLevelParser.ToLicenceEdition(level));
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
            granted.Count,
            entitlementService.GetSeatStatus(organisationId),
            entitlementService.GetMaintenanceStatus());
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

    /// <summary>Creates an offline licence request (licence-request.nlr) for sneaker-net to the customer portal.</summary>
    public LicenceRequestDocument CreateLicenceRequest()
    {
        store.EnsureInstanceId();
        var instance = store.GetInstance();
        var organisation = store.GetOrganisation();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";
        return new LicenceRequestDocument
        {
            InstanceId = instance.InstanceId,
            InstallationVersion = version,
            OrganisationId = organisation?.Id,
            OrganisationName = organisation?.Name,
            RequestedAt = DateTimeOffset.UtcNow
        };
    }

    public string ExportLicenceRequestJson() =>
        JsonSerializer.Serialize(CreateLicenceRequest(), JsonOptions);

    /// <summary>Exports signed usage for offline seat reconciliation. Soft seats never block work.</summary>
    public UsageExportDocument ExportUsage(string? period = null)
    {
        store.EnsureInstanceId();
        var organisationId = store.GetOrganisation()?.Id ?? KnownIds.OrganisationId;
        var seats = entitlementService.GetSeatStatus(organisationId);
        var active = store.GetActiveLicence();
        var doc = new UsageExportDocument
        {
            LicenceId = active?.LicenceId,
            InstanceId = store.GetInstance().InstanceId,
            Period = period ?? $"{DateTimeOffset.UtcNow:yyyy}-Q{(DateTimeOffset.UtcNow.Month - 1) / 3 + 1}",
            PeakUsers = seats.ActiveUsers,
            ActiveUsers = seats.ActiveUsers,
            ExportedAt = DateTimeOffset.UtcNow
        };
        var payload = Encoding.UTF8.GetBytes(
            $"{doc.LicenceId}\n{doc.InstanceId:N}\n{doc.Period}\n{doc.PeakUsers}\n{doc.ActiveUsers}\n{doc.ExportedAt:O}");
        // Attestation: HMAC with instance id as key material (not vendor private key — installation-local).
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(doc.InstanceId.ToString("N")));
        doc.Signature = Convert.ToBase64String(hmac.ComputeHash(payload));
        return doc;
    }

    public LicenceStatusView InstallCommercial(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Licence payload is required.");
        }

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
        if (document.ValidUntil is { } validUntil && validUntil <= now)
        {
            throw new ArgumentException("Licence could not be validated. Reason: Licence has expired.");
        }

        DateTimeOffset? earliestExpiry = document.ValidUntil;
        foreach (var module in entitlement.Modules.Values)
        {
            if (module.ExpiresAt is { } expires)
            {
                earliestExpiry = earliestExpiry is null ? expires : (expires < earliestExpiry ? expires : earliestExpiry);
            }
        }

        if (earliestExpiry is { } exp && exp <= now)
        {
            throw new ArgumentException("Licence could not be validated. Reason: Licence has expired.");
        }

        store.EnsureInstanceId();
        var localInstance = store.GetInstance().InstanceId;
        if (document.InstanceId is { } boundInstance && boundInstance != Guid.Empty && boundInstance != localInstance)
        {
            store.AddLicenceHistory(new LicenceHistoryEntry
            {
                Action = "licence.validation_failed",
                Detail = "Instance binding mismatch"
            });
            throw new ArgumentException(
                $"Licence could not be validated. Reason: Licence is bound to a different ForgeDeck instance ({boundInstance:N}). This instance is {localInstance:N}.");
        }

        var hadCommercial = store.GetInstance().LicenceMode == LicenceMode.Commercial && store.GetActiveLicence() is not null;
        var record = new LicenceRecord
        {
            Mode = LicenceMode.Commercial,
            Status = LicenceRecordStatus.Active,
            LicenceId = document.LicenceId ?? document.OrganisationId.ToString("N")[..8],
            CustomerId = document.Customer ?? document.OrganisationId.ToString(),
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
            if (string.IsNullOrWhiteSpace(relative))
            {
                return;
            }

            var path = Path.IsPathRooted(relative) ? relative : Path.Combine(environment.ContentRootPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Licence installed in database but file mirror failed.");
        }
    }
}
