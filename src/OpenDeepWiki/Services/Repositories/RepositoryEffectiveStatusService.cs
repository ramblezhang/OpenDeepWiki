using OpenDeepWiki.Entities;
using OpenDeepWiki.Models;

namespace OpenDeepWiki.Services.Repositories;

public static class RepositoryEffectiveStatusService
{
    public static RepositoryEffectiveStatusDto Build(
        Repository repository,
        IReadOnlyCollection<RepositoryBranch> branches,
        IReadOnlyCollection<BranchGenerationTask> branchGenerationTasks,
        IReadOnlyCollection<IncrementalUpdateTask> incrementalTasks)
    {
        var visibleBranches = branches.Where(branch => !branch.IsDeleted).ToList();
        var activeBranchTasks = branchGenerationTasks
            .Where(task => !task.IsDeleted && IsActive(task.Status))
            .OrderByDescending(task => task.StartedAt ?? task.CreatedAt)
            .ThenByDescending(task => task.CreatedAt)
            .ToList();
        var activeIncrementalTasks = incrementalTasks
            .Where(task => !task.IsDeleted && IsActive(task.Status))
            .OrderByDescending(task => task.StartedAt ?? task.CreatedAt)
            .ThenByDescending(task => task.CreatedAt)
            .ToList();

        var result = new RepositoryEffectiveStatusDto
        {
            StatusCounts = new RepositoryStatusCountsDto
            {
                TotalBranches = visibleBranches.Count,
                CompletedBranches = visibleBranches.Count(IsBranchCompleted),
                FailedBranches = visibleBranches.Count(branch => branch.GenerationStatus == BranchGenerationTaskStatus.Failed),
                BranchFullPending = CountActiveBranchStatus(visibleBranches, activeBranchTasks, BranchGenerationTaskStatus.Pending),
                BranchFullProcessing = CountActiveBranchStatus(visibleBranches, activeBranchTasks, BranchGenerationTaskStatus.Processing),
                IncrementalPending = activeIncrementalTasks.Count(task => task.Status == IncrementalUpdateStatus.Pending),
                IncrementalProcessing = activeIncrementalTasks.Count(task => task.Status == IncrementalUpdateStatus.Processing)
            },
            ActiveOperations = BuildActiveOperations(repository, visibleBranches, activeBranchTasks, activeIncrementalTasks),
            BlockingFailures = BuildBlockingFailures(visibleBranches)
        };

        var pendingBranchFullCount = result.StatusCounts.BranchFullPending;
        var processingBranchFullCount = result.StatusCounts.BranchFullProcessing;
        var activeBranchFullCount = pendingBranchFullCount + processingBranchFullCount;
        var incrementalActiveCount = result.StatusCounts.IncrementalPending + result.StatusCounts.IncrementalProcessing;

        if (repository.Status is RepositoryStatus.Pending or RepositoryStatus.Processing)
        {
            result.EffectiveStatus = repository.Status == RepositoryStatus.Pending
                ? RepositoryEffectiveStatuses.RepositoryFullPending
                : RepositoryEffectiveStatuses.RepositoryFullProcessing;
            result.EffectiveStatusReason = repository.Status == RepositoryStatus.Pending
                ? "Repository full generation is pending"
                : "Repository full generation is processing";
            return result;
        }

        if (activeBranchFullCount > 0)
        {
            if (processingBranchFullCount > 0)
            {
                result.EffectiveStatus = processingBranchFullCount == Math.Max(visibleBranches.Count, 1) && pendingBranchFullCount == 0
                    ? RepositoryEffectiveStatuses.AllBranchesGenerating
                    : RepositoryEffectiveStatuses.PartialBranchesGenerating;
                result.EffectiveStatusReason = $"{processingBranchFullCount}/{visibleBranches.Count} branches are generating; {pendingBranchFullCount} branches are queued";
                return result;
            }

            result.EffectiveStatus = pendingBranchFullCount == Math.Max(visibleBranches.Count, 1)
                ? RepositoryEffectiveStatuses.AllBranchesQueued
                : RepositoryEffectiveStatuses.PartialBranchesQueued;
            result.EffectiveStatusReason = $"{pendingBranchFullCount}/{visibleBranches.Count} branches are queued for full generation";
            return result;
        }

        if (result.StatusCounts.FailedBranches > 0)
        {
            result.EffectiveStatus = result.StatusCounts.FailedBranches == Math.Max(visibleBranches.Count, 1)
                ? RepositoryEffectiveStatuses.Failed
                : RepositoryEffectiveStatuses.PartialFailed;
            result.EffectiveStatusReason = incrementalActiveCount > 0
                ? $"{result.StatusCounts.FailedBranches}/{visibleBranches.Count} branches failed; {incrementalActiveCount} incremental updates are active"
                : $"{result.StatusCounts.FailedBranches}/{visibleBranches.Count} branches failed";
            return result;
        }

        if (incrementalActiveCount > 0)
        {
            result.EffectiveStatus = RepositoryEffectiveStatuses.IncrementalUpdating;
            result.EffectiveStatusReason = $"{incrementalActiveCount} incremental updates are active";
            return result;
        }

        if (result.StatusCounts.TotalBranches > 0 &&
            result.StatusCounts.CompletedBranches == result.StatusCounts.TotalBranches)
        {
            result.EffectiveStatus = RepositoryEffectiveStatuses.Completed;
            result.EffectiveStatusReason = "All branches are completed";
            return result;
        }

        if (repository.Status == RepositoryStatus.Failed)
        {
            result.EffectiveStatus = RepositoryEffectiveStatuses.Failed;
            result.EffectiveStatusReason = "Repository full generation failed";
            return result;
        }

        result.EffectiveStatus = RepositoryEffectiveStatuses.Unknown;
        result.EffectiveStatusReason = "No completed branch or active operation was found";
        return result;
    }

    private static bool IsActive(BranchGenerationTaskStatus status)
        => status is BranchGenerationTaskStatus.Pending or BranchGenerationTaskStatus.Processing;

    private static bool IsActive(IncrementalUpdateStatus status)
        => status is IncrementalUpdateStatus.Pending or IncrementalUpdateStatus.Processing;

    private static bool IsBranchCompleted(RepositoryBranch branch)
        => branch.GenerationStatus == BranchGenerationTaskStatus.Completed ||
           (branch.GenerationStatus is null && branch.LastProcessedAt.HasValue);

    private static int CountActiveBranchStatus(
        IReadOnlyCollection<RepositoryBranch> branches,
        IReadOnlyCollection<BranchGenerationTask> activeTasks,
        BranchGenerationTaskStatus status)
    {
        var branchIdsFromTasks = activeTasks
            .Where(task => task.Status == status)
            .Select(task => task.BranchId)
            .ToHashSet();

        return branches.Count(branch =>
            branch.GenerationStatus == status || branchIdsFromTasks.Contains(branch.Id));
    }

    private static List<RepositoryActiveOperationDto> BuildActiveOperations(
        Repository repository,
        IReadOnlyCollection<RepositoryBranch> branches,
        IReadOnlyCollection<BranchGenerationTask> activeBranchTasks,
        IReadOnlyCollection<IncrementalUpdateTask> activeIncrementalTasks)
    {
        var branchById = branches.ToDictionary(branch => branch.Id);
        var operations = new List<RepositoryActiveOperationDto>();

        if (repository.Status is RepositoryStatus.Pending or RepositoryStatus.Processing)
        {
            operations.Add(new RepositoryActiveOperationDto
            {
                Type = "RepositoryFullGeneration",
                RepositoryId = repository.Id,
                Status = repository.Status.ToString(),
                CreatedAt = repository.CreatedAt
            });
        }

        operations.AddRange(activeBranchTasks.Select(task =>
        {
            branchById.TryGetValue(task.BranchId, out var branch);
            return new RepositoryActiveOperationDto
            {
                Type = "BranchFullGeneration",
                RepositoryId = task.RepositoryId,
                BranchId = task.BranchId,
                BranchName = branch?.BranchName,
                TaskId = task.Id,
                Status = task.Status.ToString(),
                CreatedAt = task.CreatedAt,
                StartedAt = task.StartedAt
            };
        }));

        operations.AddRange(activeIncrementalTasks.Select(task =>
        {
            branchById.TryGetValue(task.BranchId, out var branch);
            return new RepositoryActiveOperationDto
            {
                Type = "IncrementalUpdate",
                RepositoryId = task.RepositoryId,
                BranchId = task.BranchId,
                BranchName = branch?.BranchName,
                TaskId = task.Id,
                Status = task.Status.ToString(),
                CreatedAt = task.CreatedAt,
                StartedAt = task.StartedAt
            };
        }));

        return operations;
    }

    private static List<RepositoryBlockingFailureDto> BuildBlockingFailures(IReadOnlyCollection<RepositoryBranch> branches)
    {
        return branches
            .Where(branch => branch.GenerationStatus == BranchGenerationTaskStatus.Failed)
            .Select(branch => new RepositoryBlockingFailureDto
            {
                Type = "BranchGeneration",
                BranchId = branch.Id,
                BranchName = branch.BranchName,
                TaskId = branch.LastGenerationTaskId,
                Reason = IsZeroLanguageFailure(branch.LastGenerationError)
                    ? "NoLanguagesConfigured"
                    : "BranchGenerationFailed",
                Message = branch.LastGenerationError
            })
            .ToList();
    }

    private static bool IsZeroLanguageFailure(string? message)
        => !string.IsNullOrWhiteSpace(message) &&
           (message.Contains("NoLanguages", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("no configured languages", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("0 languages", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("zero-language", StringComparison.OrdinalIgnoreCase));
}
