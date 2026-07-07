using OpenDeepWiki.Entities;
using OpenDeepWiki.Models;
using OpenDeepWiki.Services.Repositories;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class RepositoryEffectiveStatusServiceTests
{
    [Fact]
    public void Build_ReturnsRepositoryFullProcessing_WhenRepositoryIsProcessing()
    {
        var repo = CreateRepository(RepositoryStatus.Processing);

        var result = RepositoryEffectiveStatusService.Build(repo, [], [], []);

        Assert.Equal(RepositoryEffectiveStatuses.RepositoryFullProcessing, result.EffectiveStatus);
        Assert.Single(result.ActiveOperations);
        Assert.Equal("RepositoryFullGeneration", result.ActiveOperations[0].Type);
    }

    [Fact]
    public void Build_ReturnsPartialBranchesGenerating_WhenSomeBranchesAreProcessingAndSomeQueued()
    {
        var repo = CreateRepository();
        var branches = new[]
        {
            CreateBranch("branch-1", BranchGenerationTaskStatus.Pending),
            CreateBranch("branch-2", BranchGenerationTaskStatus.Processing)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, [], []);

        Assert.Equal(RepositoryEffectiveStatuses.PartialBranchesGenerating, result.EffectiveStatus);
        Assert.Equal(1, result.StatusCounts.BranchFullPending);
        Assert.Equal(1, result.StatusCounts.BranchFullProcessing);
        Assert.Contains("1/2 branches are generating; 1 branches are queued", result.EffectiveStatusReason);
    }

    [Fact]
    public void Build_ReturnsAllBranchesQueued_WhenEveryBranchIsPending()
    {
        var repo = CreateRepository();
        var branches = new[]
        {
            CreateBranch("branch-1", BranchGenerationTaskStatus.Pending),
            CreateBranch("branch-2", BranchGenerationTaskStatus.Pending)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, [], []);

        Assert.Equal(RepositoryEffectiveStatuses.AllBranchesQueued, result.EffectiveStatus);
        Assert.Equal(2, result.StatusCounts.BranchFullPending);
        Assert.Equal(0, result.StatusCounts.BranchFullProcessing);
    }

    [Fact]
    public void Build_ReturnsPartialBranchesQueued_WhenOnlyOneBranchIsPending()
    {
        var repo = CreateRepository();
        var branches = new[]
        {
            CreateBranch("branch-1", BranchGenerationTaskStatus.Completed),
            CreateBranch("branch-2", BranchGenerationTaskStatus.Pending)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, [], []);

        Assert.Equal(RepositoryEffectiveStatuses.PartialBranchesQueued, result.EffectiveStatus);
        Assert.Equal(1, result.StatusCounts.CompletedBranches);
        Assert.Equal(1, result.StatusCounts.BranchFullPending);
        Assert.Equal(0, result.StatusCounts.BranchFullProcessing);
    }

    [Fact]
    public void Build_ReturnsAllBranchesGenerating_WhenEveryBranchIsProcessing()
    {
        var repo = CreateRepository();
        var branches = new[]
        {
            CreateBranch("branch-1", BranchGenerationTaskStatus.Processing),
            CreateBranch("branch-2", BranchGenerationTaskStatus.Processing)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, [], []);

        Assert.Equal(RepositoryEffectiveStatuses.AllBranchesGenerating, result.EffectiveStatus);
        Assert.Equal(0, result.StatusCounts.BranchFullPending);
        Assert.Equal(2, result.StatusCounts.BranchFullProcessing);
    }

    [Fact]
    public void Build_ReturnsPartialBranchesGenerating_WhenOnlyOneBranchIsActive()
    {
        var repo = CreateRepository();
        var branches = new[]
        {
            CreateBranch("branch-1", BranchGenerationTaskStatus.Completed),
            CreateBranch("branch-2", BranchGenerationTaskStatus.Processing)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, [], []);

        Assert.Equal(RepositoryEffectiveStatuses.PartialBranchesGenerating, result.EffectiveStatus);
        Assert.Equal(1, result.StatusCounts.CompletedBranches);
        Assert.Equal(1, result.StatusCounts.BranchFullProcessing);
    }

    [Fact]
    public void Build_ReturnsPartialFailedBeforeIncremental_WhenFailedBranchAndIncrementalAreActive()
    {
        var repo = CreateRepository();
        var branches = new[]
        {
            CreateBranch("branch-1", BranchGenerationTaskStatus.Completed),
            CreateBranch("branch-2", BranchGenerationTaskStatus.Failed, "No configured languages")
        };
        var incrementalTasks = new[]
        {
            CreateIncrementalTask("task-1", "branch-1", IncrementalUpdateStatus.Processing)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, [], incrementalTasks);

        Assert.Equal(RepositoryEffectiveStatuses.PartialFailed, result.EffectiveStatus);
        Assert.Equal(1, result.StatusCounts.IncrementalProcessing);
        Assert.Single(result.ActiveOperations);
        Assert.Single(result.BlockingFailures);
        Assert.Equal("NoLanguagesConfigured", result.BlockingFailures[0].Reason);
    }

    [Fact]
    public void Build_ReturnsFailed_WhenEveryBranchFailed()
    {
        var repo = CreateRepository();
        var branches = new[]
        {
            CreateBranch("branch-1", BranchGenerationTaskStatus.Failed),
            CreateBranch("branch-2", BranchGenerationTaskStatus.Failed)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, [], []);

        Assert.Equal(RepositoryEffectiveStatuses.Failed, result.EffectiveStatus);
        Assert.Equal(2, result.BlockingFailures.Count);
    }

    [Fact]
    public void Build_ReturnsIncrementalUpdating_WhenOnlyIncrementalIsActive()
    {
        var repo = CreateRepository();
        var branches = new[] { CreateBranch("branch-1", BranchGenerationTaskStatus.Completed) };
        var incrementalTasks = new[]
        {
            CreateIncrementalTask("task-1", "branch-1", IncrementalUpdateStatus.Pending)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, [], incrementalTasks);

        Assert.Equal(RepositoryEffectiveStatuses.IncrementalUpdating, result.EffectiveStatus);
        Assert.Equal(1, result.StatusCounts.IncrementalPending);
        Assert.Single(result.ActiveOperations);
        Assert.Equal("IncrementalUpdate", result.ActiveOperations[0].Type);
    }

    [Fact]
    public void Build_ReturnsCompleted_WhenAllBranchesCompletedAndNoActiveOperations()
    {
        var repo = CreateRepository();
        var branches = new[]
        {
            CreateBranch("branch-1", BranchGenerationTaskStatus.Completed),
            CreateBranch("branch-2", BranchGenerationTaskStatus.Completed)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, [], []);

        Assert.Equal(RepositoryEffectiveStatuses.Completed, result.EffectiveStatus);
        Assert.Equal(2, result.StatusCounts.CompletedBranches);
    }

    [Fact]
    public void Build_IgnoresHistoricalFailedBranchTask_WhenBranchWasLaterProcessedSuccessfully()
    {
        var repo = CreateRepository();
        var branches = new[]
        {
            CreateBranch("branch-1", null)
        };
        branches[0].LastProcessedAt = DateTime.UtcNow;
        branches[0].LastGenerationTaskId = null;
        branches[0].LastGenerationError = null;

        var branchTasks = new[]
        {
            CreateBranchTask("old-failed-task", "branch-1", BranchGenerationTaskStatus.Failed)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, branchTasks, []);

        Assert.Equal(RepositoryEffectiveStatuses.Completed, result.EffectiveStatus);
        Assert.Equal(1, result.StatusCounts.CompletedBranches);
        Assert.Equal(0, result.StatusCounts.FailedBranches);
        Assert.Empty(result.BlockingFailures);
        Assert.Empty(result.ActiveOperations);
    }

    [Fact]
    public void Build_CountsActiveBranchTask_WhenBranchStatusHasNotCaughtUp()
    {
        var repo = CreateRepository();
        var branches = new[]
        {
            CreateBranch("branch-1", BranchGenerationTaskStatus.Completed),
            CreateBranch("branch-2", null)
        };
        var branchTasks = new[]
        {
            CreateBranchTask("task-1", "branch-2", BranchGenerationTaskStatus.Processing)
        };

        var result = RepositoryEffectiveStatusService.Build(repo, branches, branchTasks, []);

        Assert.Equal(RepositoryEffectiveStatuses.PartialBranchesGenerating, result.EffectiveStatus);
        Assert.Equal(1, result.StatusCounts.BranchFullProcessing);
        Assert.Single(result.ActiveOperations);
    }

    private static Repository CreateRepository(RepositoryStatus status = RepositoryStatus.Completed)
    {
        return new Repository
        {
            Id = "repo-1",
            OwnerUserId = "user-1",
            GitUrl = "https://example.com/example/repo.git",
            OrgName = "example",
            RepoName = "repo",
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static RepositoryBranch CreateBranch(
        string id,
        BranchGenerationTaskStatus? status,
        string? error = null)
    {
        return new RepositoryBranch
        {
            Id = id,
            RepositoryId = "repo-1",
            BranchName = id,
            GenerationStatus = status,
            LastGenerationTaskId = status == BranchGenerationTaskStatus.Failed ? $"task-{id}" : null,
            LastGenerationError = error,
            LastProcessedAt = status == BranchGenerationTaskStatus.Completed ? DateTime.UtcNow : null
        };
    }

    private static BranchGenerationTask CreateBranchTask(string id, string branchId, BranchGenerationTaskStatus status)
    {
        return new BranchGenerationTask
        {
            Id = id,
            RepositoryId = "repo-1",
            BranchId = branchId,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static IncrementalUpdateTask CreateIncrementalTask(string id, string branchId, IncrementalUpdateStatus status)
    {
        return new IncrementalUpdateTask
        {
            Id = id,
            RepositoryId = "repo-1",
            BranchId = branchId,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
    }
}
