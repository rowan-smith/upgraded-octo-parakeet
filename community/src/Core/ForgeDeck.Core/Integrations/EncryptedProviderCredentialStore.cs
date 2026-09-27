using ForgeDeck.Contracts.Integrations;
using ForgeDeck.Core.Persistence;
using ForgeDeck.Core.Persistence.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Core.Integrations;

public sealed class EncryptedProviderCredentialStore : IProviderCredentialStore
{
    private readonly IDbContextFactory<PlatformDbContext> _factory;
    private readonly IDataProtector _protector;

    public EncryptedProviderCredentialStore(IDbContextFactory<PlatformDbContext> factory, IDataProtectionProvider protection)
    {
        _factory = factory;
        _protector = protection.CreateProtector("ForgeDeck.ProviderCredentials.v1");
        using var db = _factory.CreateDbContext();
        PlatformDbContext.EnsureCreated(db);
    }

    public string? Get(string providerId)
    {
        using var db = _factory.CreateDbContext();
        var protectedSecret = db.Integrations.AsNoTracking()
            .Where(x => x.ProviderId == providerId)
            .Select(x => x.ProtectedSecret)
            .FirstOrDefault();
        return protectedSecret is null ? null : _protector.Unprotect(protectedSecret);
    }

    public bool IsConfigured(string providerId)
    {
        using var db = _factory.CreateDbContext();
        return db.Integrations.AsNoTracking().Any(x => x.ProviderId == providerId);
    }

    public void Set(string providerId, string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("A token is required.");
        }

        using var db = _factory.CreateDbContext();
        var existing = db.Integrations.Find(providerId);
        var protectedSecret = _protector.Protect(secret.Trim());
        var updatedAt = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            db.Integrations.Add(new ProviderCredentialRow
            {
                ProviderId = providerId,
                ProtectedSecret = protectedSecret,
                UpdatedAt = updatedAt
            });
        }
        else
        {
            existing.ProtectedSecret = protectedSecret;
            existing.UpdatedAt = updatedAt;
        }

        db.SaveChanges();
    }

    public void Delete(string providerId)
    {
        using var db = _factory.CreateDbContext();
        db.Integrations.Where(x => x.ProviderId == providerId).ExecuteDelete();
    }
}
