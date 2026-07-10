using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Repositories;

public sealed record GenerationLeaseHandle(
    string LockId,
    string RepositoryId,
    RepositoryGenerationLockOwnerType OwnerType,
    string OwnerId);

public sealed class GenerationLeaseLostException(string lockId)
    : InvalidOperationException($"Generation lease '{lockId}' is no longer active.");

public interface IGenerationWriteGuard
{
    Task SaveChangesAsync(
        IContext context,
        GenerationLeaseHandle lease,
        CancellationToken cancellationToken = default);

    Task ExecuteAsync(
        IContext context,
        GenerationLeaseHandle lease,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}

public sealed class GenerationWriteGuard(IOptions<IncrementalUpdateOptions> options) : IGenerationWriteGuard
{
    private readonly IncrementalUpdateOptions _options = options.Value;

    public async Task SaveChangesAsync(
        IContext context,
        GenerationLeaseHandle lease,
        CancellationToken cancellationToken = default) =>
        await ExecuteAsync(context, lease, _ => Task.CompletedTask, cancellationToken);

    public async Task ExecuteAsync(
        IContext context,
        GenerationLeaseHandle lease,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("Fenced writes require an EF Core DbContext.");
        }

        if (IsInMemory(dbContext))
        {
            var inMemoryLease = await context.RepositoryGenerationLocks.FirstOrDefaultAsync(item =>
                !item.IsDeleted &&
                item.Id == lease.LockId &&
                item.RepositoryId == lease.RepositoryId &&
                item.OwnerType == lease.OwnerType &&
                item.OwnerId == lease.OwnerId,
                cancellationToken);
            if (inMemoryLease is null)
            {
                dbContext.ChangeTracker.Clear();
                throw new GenerationLeaseLostException(lease.LockId);
            }

            await dbContext.Entry(inMemoryLease).ReloadAsync(cancellationToken);
            if (IsExpired(inMemoryLease, DateTime.UtcNow))
            {
                dbContext.ChangeTracker.Clear();
                throw new GenerationLeaseLostException(lease.LockId);
            }

            try
            {
                await operation(cancellationToken);
                inMemoryLease.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                dbContext.ChangeTracker.Clear();
                throw;
            }
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-Math.Max(1, _options.StaleTaskTimeoutMinutes));
        var renewed = IsSqlite(dbContext)
            ? await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "RepositoryGenerationLocks"
                SET "UpdatedAt" = {now}
                WHERE "IsDeleted" = 0
                  AND "Id" = {lease.LockId}
                  AND "RepositoryId" = {lease.RepositoryId}
                  AND "OwnerType" = {(int)lease.OwnerType}
                  AND "OwnerId" = {lease.OwnerId}
                  AND COALESCE("UpdatedAt", "AcquiredAt") > {cutoff}
                """, cancellationToken)
            : await context.RepositoryGenerationLocks
                .Where(item =>
                    !item.IsDeleted &&
                    item.Id == lease.LockId &&
                    item.RepositoryId == lease.RepositoryId &&
                    item.OwnerType == lease.OwnerType &&
                    item.OwnerId == lease.OwnerId &&
                    (item.UpdatedAt ?? item.AcquiredAt) > cutoff)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.UpdatedAt, now),
                    cancellationToken);

        if (renewed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            throw new GenerationLeaseLostException(lease.LockId);
        }

        try
        {
            await operation(cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private bool IsExpired(RepositoryGenerationLock lease, DateTime now)
    {
        var heartbeatAt = lease.UpdatedAt ?? lease.AcquiredAt;
        return heartbeatAt <= now.AddMinutes(-Math.Max(1, _options.StaleTaskTimeoutMinutes));
    }

    private static bool IsInMemory(DbContext context) =>
        context.Database.ProviderName?.Contains("InMemory", StringComparison.Ordinal) == true;

    private static bool IsSqlite(DbContext context) =>
        context.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true;
}
