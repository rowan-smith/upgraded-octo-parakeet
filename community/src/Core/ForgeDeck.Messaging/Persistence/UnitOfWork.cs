using Microsoft.EntityFrameworkCore;

namespace ForgeDeck.Messaging.Persistence;

/// <summary>
/// Ambient DbContext/transaction so module writes and outbox inserts share one commit.
/// </summary>
public interface IUnitOfWork
{
    bool HasActiveTransaction { get; }
    Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
}

public sealed class EfUnitOfWork(IDbContextFactory<MessagingDbContext> factory) : IUnitOfWork
{
    private static readonly AsyncLocal<MessagingDbContext?> Current = new();

    public static bool TryGetAmbient(out MessagingDbContext db)
    {
        if (Current.Value is { } ambient)
        {
            db = ambient;
            return true;
        }

        db = null!;
        return false;
    }

    public bool HasActiveTransaction => Current.Value is not null;

    public async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async ct =>
        {
            await action(ct);
            return true;
        }, cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        if (Current.Value is not null)
        {
            return await action(cancellationToken);
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Current.Value = db;
        try
        {
            var result = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch
            {
                // ignored
            }

            throw;
        }
        finally
        {
            Current.Value = null;
        }
    }
}
