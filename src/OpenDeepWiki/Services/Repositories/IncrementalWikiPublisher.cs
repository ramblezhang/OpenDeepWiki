using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Repositories;

public interface IIncrementalWikiPublisher
{
    Task PublishAsync(
        IIncrementalWikiDraft draft,
        string repositoryId,
        string branchId,
        string? expectedBaseline,
        string targetCommitId,
        GenerationLeaseHandle lease,
        CancellationToken cancellationToken = default);
}

public sealed class IncrementalWikiPublisher(
    IContext context,
    IRepositoryAnalyzer repositoryAnalyzer,
    IGenerationWriteGuard writeGuard) : IIncrementalWikiPublisher
{
    public async Task PublishAsync(
        IIncrementalWikiDraft draft,
        string repositoryId,
        string branchId,
        string? expectedBaseline,
        string targetCommitId,
        GenerationLeaseHandle lease,
        CancellationToken cancellationToken = default)
    {
        var repository = await context.Repositories
            .FirstAsync(item => item.Id == repositoryId && !item.IsDeleted, cancellationToken);
        var preflight = await repositoryAnalyzer.GetLocalGitPreflightAsync(repository, cancellationToken);
        if (!preflight.IsLocalGit || !preflight.IsClean)
        {
            throw new LocalGitWorktreeDirtyException(preflight);
        }

        if (!string.Equals(preflight.HeadCommitId, draft.SourceHeadCommitId, StringComparison.Ordinal))
        {
            throw new LocalGitSourceVersionChangedException(
                draft.SourceHeadCommitId,
                preflight.HeadCommitId);
        }

        await writeGuard.ExecuteAsync(
            context,
            lease,
            async publishCancellationToken =>
            {
                var task = await context.IncrementalUpdateTasks.FirstOrDefaultAsync(item =>
                    item.Id == lease.OwnerId &&
                    item.RepositoryId == repositoryId &&
                    item.BranchId == branchId &&
                    item.Status == IncrementalUpdateStatus.Processing &&
                    !item.IsDeleted,
                    publishCancellationToken);
                if (task is null)
                {
                    throw new GenerationLeaseLostException(lease.LockId);
                }

                var baselineUpdated = await CompareAndSwapBaselineAsync(
                    branchId,
                    expectedBaseline,
                    targetCommitId,
                    publishCancellationToken);
                if (!baselineUpdated)
                {
                    throw new IncrementalBaselineConflictException(branchId, expectedBaseline);
                }

                await draft.ApplyAsync(context, publishCancellationToken);

                var now = DateTime.UtcNow;
                repository.LastUpdateCheckAt = now;
                repository.UpdatedAt = now;

                task.TargetCommitId = targetCommitId;
                task.Status = IncrementalUpdateStatus.Completed;
                task.ErrorMessage = null;
                task.CompletedAt = now;
                task.UpdatedAt = now;
            },
            cancellationToken);
    }

    private async Task<bool> CompareAndSwapBaselineAsync(
        string branchId,
        string? expectedBaseline,
        string targetCommitId,
        CancellationToken cancellationToken)
    {
        if (context is not DbContext dbContext)
        {
            throw new InvalidOperationException("Atomic incremental publish requires an EF Core DbContext.");
        }

        var now = DateTime.UtcNow;
        if (IsInMemory(dbContext))
        {
            var branch = await context.RepositoryBranches
                .FirstOrDefaultAsync(item => item.Id == branchId && !item.IsDeleted, cancellationToken);
            if (branch is null ||
                !string.Equals(branch.LastCommitId, expectedBaseline, StringComparison.Ordinal))
            {
                return false;
            }

            branch.LastCommitId = targetCommitId;
            branch.LastProcessedAt = now;
            branch.UpdatedAt = now;
            return true;
        }

        var updated = IsSqlite(dbContext)
            ? await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "RepositoryBranches"
                SET "LastCommitId" = {targetCommitId},
                    "LastProcessedAt" = {now},
                    "UpdatedAt" = {now}
                WHERE "Id" = {branchId}
                  AND "IsDeleted" = 0
                  AND (("LastCommitId" = {expectedBaseline}) OR
                       ("LastCommitId" IS NULL AND {expectedBaseline} IS NULL))
                """, cancellationToken)
            : await context.RepositoryBranches
                .Where(item =>
                    item.Id == branchId &&
                    !item.IsDeleted &&
                    item.LastCommitId == expectedBaseline)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.LastCommitId, targetCommitId)
                        .SetProperty(item => item.LastProcessedAt, now)
                        .SetProperty(item => item.UpdatedAt, now),
                    cancellationToken);
        return updated == 1;
    }

    private static bool IsInMemory(DbContext context) =>
        context.Database.ProviderName?.Contains("InMemory", StringComparison.Ordinal) == true;

    private static bool IsSqlite(DbContext context) =>
        context.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true;
}
