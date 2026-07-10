using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Repositories;

public interface IRepositoryGenerationLockService
{
    Task<RepositoryGenerationLock?> GetLockAsync(
        string repositoryId,
        CancellationToken cancellationToken = default);

    Task<bool> TryAcquireAsync(
        IContext context,
        string repositoryId,
        RepositoryGenerationLockOwnerType ownerType,
        string ownerId,
        RepositoryGenerationLockScope scope,
        CancellationToken cancellationToken = default);

    Task<GenerationLeaseHandle?> TryAcquireLeaseAsync(
        IContext context,
        string repositoryId,
        RepositoryGenerationLockOwnerType ownerType,
        string ownerId,
        RepositoryGenerationLockScope scope,
        CancellationToken cancellationToken = default);

    Task<bool> RenewLeaseAsync(
        IContext context,
        GenerationLeaseHandle lease,
        CancellationToken cancellationToken = default);

    Task ReleaseLeaseAsync(
        IContext context,
        GenerationLeaseHandle lease,
        CancellationToken cancellationToken = default);

    Task ReleaseAsync(
        IContext context,
        string repositoryId,
        RepositoryGenerationLockOwnerType ownerType,
        string ownerId,
        CancellationToken cancellationToken = default);
}

public sealed class RepositoryGenerationLockService(IContext rootContext) : IRepositoryGenerationLockService
{
    public Task<RepositoryGenerationLock?> GetLockAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        return rootContext.RepositoryGenerationLocks
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.RepositoryId == repositoryId && !item.IsDeleted, cancellationToken);
    }

    public async Task<bool> TryAcquireAsync(
        IContext context,
        string repositoryId,
        RepositoryGenerationLockOwnerType ownerType,
        string ownerId,
        RepositoryGenerationLockScope scope,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.RepositoryGenerationLocks
            .FirstOrDefaultAsync(item => item.RepositoryId == repositoryId && !item.IsDeleted, cancellationToken);

        if (existing is not null)
        {
            return existing.OwnerType == ownerType && existing.OwnerId == ownerId;
        }

        var generationLock = new RepositoryGenerationLock
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            Scope = scope,
            AcquiredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        context.RepositoryGenerationLocks.Add(generationLock);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            if (context is DbContext dbContext)
            {
                dbContext.Entry(generationLock).State = EntityState.Detached;
            }

            return false;
        }
    }

    public async Task<GenerationLeaseHandle?> TryAcquireLeaseAsync(
        IContext context,
        string repositoryId,
        RepositoryGenerationLockOwnerType ownerType,
        string ownerId,
        RepositoryGenerationLockScope scope,
        CancellationToken cancellationToken = default)
    {
        if (await context.RepositoryGenerationLocks.AnyAsync(
                item => item.RepositoryId == repositoryId && !item.IsDeleted,
                cancellationToken))
        {
            return null;
        }

        var generationLock = new RepositoryGenerationLock
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            Scope = scope,
            AcquiredAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        context.RepositoryGenerationLocks.Add(generationLock);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return new GenerationLeaseHandle(
                generationLock.Id,
                generationLock.RepositoryId,
                generationLock.OwnerType,
                generationLock.OwnerId);
        }
        catch (DbUpdateException)
        {
            if (context is DbContext dbContext)
            {
                dbContext.Entry(generationLock).State = EntityState.Detached;
            }

            return null;
        }
    }

    public async Task<bool> RenewLeaseAsync(
        IContext context,
        GenerationLeaseHandle lease,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (context is DbContext dbContext && !IsInMemory(dbContext))
        {
            if (IsSqlite(dbContext))
            {
                return await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "RepositoryGenerationLocks"
                    SET "UpdatedAt" = {now}
                    WHERE "IsDeleted" = 0
                      AND "Id" = {lease.LockId}
                      AND "RepositoryId" = {lease.RepositoryId}
                      AND "OwnerType" = {(int)lease.OwnerType}
                      AND "OwnerId" = {lease.OwnerId}
                    """, cancellationToken) == 1;
            }

            return await context.RepositoryGenerationLocks
                .Where(item =>
                    !item.IsDeleted &&
                    item.Id == lease.LockId &&
                    item.RepositoryId == lease.RepositoryId &&
                    item.OwnerType == lease.OwnerType &&
                    item.OwnerId == lease.OwnerId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(item => item.UpdatedAt, now),
                    cancellationToken) == 1;
        }

        var generationLock = await context.RepositoryGenerationLocks.FirstOrDefaultAsync(item =>
            !item.IsDeleted &&
            item.Id == lease.LockId &&
            item.RepositoryId == lease.RepositoryId &&
            item.OwnerType == lease.OwnerType &&
            item.OwnerId == lease.OwnerId,
            cancellationToken);
        if (generationLock is null)
        {
            return false;
        }

        generationLock.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ReleaseLeaseAsync(
        IContext context,
        GenerationLeaseHandle lease,
        CancellationToken cancellationToken = default)
    {
        if (context is DbContext dbContext && !IsInMemory(dbContext))
        {
            if (IsSqlite(dbContext))
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM "RepositoryGenerationLocks"
                    WHERE "IsDeleted" = 0
                      AND "Id" = {lease.LockId}
                      AND "RepositoryId" = {lease.RepositoryId}
                      AND "OwnerType" = {(int)lease.OwnerType}
                      AND "OwnerId" = {lease.OwnerId}
                    """, cancellationToken);
                return;
            }

            await context.RepositoryGenerationLocks
                .Where(item =>
                    !item.IsDeleted &&
                    item.Id == lease.LockId &&
                    item.RepositoryId == lease.RepositoryId &&
                    item.OwnerType == lease.OwnerType &&
                    item.OwnerId == lease.OwnerId)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var generationLock = await context.RepositoryGenerationLocks.FirstOrDefaultAsync(item =>
            !item.IsDeleted &&
            item.Id == lease.LockId &&
            item.RepositoryId == lease.RepositoryId &&
            item.OwnerType == lease.OwnerType &&
            item.OwnerId == lease.OwnerId,
            cancellationToken);
        if (generationLock is not null)
        {
            context.RepositoryGenerationLocks.Remove(generationLock);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ReleaseAsync(
        IContext context,
        string repositoryId,
        RepositoryGenerationLockOwnerType ownerType,
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        var generationLock = await context.RepositoryGenerationLocks
            .FirstOrDefaultAsync(item =>
                item.RepositoryId == repositoryId &&
                item.OwnerType == ownerType &&
                item.OwnerId == ownerId &&
                !item.IsDeleted,
                cancellationToken);

        if (generationLock is null)
        {
            return;
        }

        context.RepositoryGenerationLocks.Remove(generationLock);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static bool IsInMemory(DbContext context) =>
        context.Database.ProviderName?.Contains("InMemory", StringComparison.Ordinal) == true;

    private static bool IsSqlite(DbContext context) =>
        context.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true;
}
