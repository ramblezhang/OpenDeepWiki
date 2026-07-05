using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class IncrementalUpdateWorkerTests
{
    private const string SnapshotHash =
        "8FDD97DF787CB40485E5D1BDA171AC1608EDEE9649D107E3E6B6C1D4A0E28C0A";

    private const string GitCommitA = "4d24129e28470a23b7d755ba1eeb58fce09447ac";
    private const string GitCommitB = "71d424d03595ecc2350215bbf03f6e6944e71a83";

    [Fact]
    public async Task CheckScheduledUpdatesAsync_WhenRemoteHeadMatchesLastCommit_DoesNotCreateTask()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, updateIntervalMinutes: 60, lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2));
        var branch = SeedBranch(context, repository.Id, "main", "same-sha");
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.GetRemoteBranchHeadCommitAsync(repository, branch.BranchName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("same-sha");

        var worker = CreateWorker();

        await InvokeCheckScheduledUpdatesAsync(worker, context, Mock.Of<IGitPlatformService>(), analyzer.Object);

        Assert.Empty(await context.IncrementalUpdateTasks.ToListAsync());
        var updatedRepository = await context.Repositories.SingleAsync(r => r.Id == repository.Id);
        Assert.True(updatedRepository.LastUpdateCheckAt > repository.CreatedAt);
        analyzer.VerifyAll();
    }

    [Fact]
    public async Task CheckScheduledUpdatesAsync_WhenRemoteHeadChanges_CreatesPendingTask()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, updateIntervalMinutes: 60, lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2));
        var branch = SeedBranch(context, repository.Id, "main", "old-sha");
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.GetRemoteBranchHeadCommitAsync(repository, branch.BranchName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-sha");

        var worker = CreateWorker();

        await InvokeCheckScheduledUpdatesAsync(worker, context, Mock.Of<IGitPlatformService>(), analyzer.Object);

        var task = await context.IncrementalUpdateTasks.SingleAsync();
        Assert.Equal(IncrementalUpdateStatus.Pending, task.Status);
        Assert.Equal("old-sha", task.PreviousCommitId);
        Assert.Equal("new-sha", task.TargetCommitId);
        Assert.False(task.IsManualTrigger);
        analyzer.VerifyAll();
    }

    [Fact]
    public async Task CheckScheduledUpdatesAsync_WhenLocalGitSourceHasSnapshotBaseline_NormalizesWithoutTask()
    {
        using var context = CreateContext();
        var repository = SeedRepository(
            context,
            updateIntervalMinutes: 60,
            lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2),
            gitUrl: RepositorySource.EncodeLocalDirectoryPath("/tmp/source-repo"));
        var branch = SeedBranch(context, repository.Id, "smart-hw/os_services_develop", SnapshotHash);
        var originalLastProcessedAt = branch.LastProcessedAt;
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.GetRemoteBranchHeadCommitAsync(repository, branch.BranchName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitCommitA);

        var worker = CreateWorker();

        await InvokeCheckScheduledUpdatesAsync(worker, context, Mock.Of<IGitPlatformService>(), analyzer.Object);

        Assert.Empty(await context.IncrementalUpdateTasks.ToListAsync());
        var updatedBranch = await context.RepositoryBranches.SingleAsync(b => b.Id == branch.Id);
        Assert.Equal(GitCommitA, updatedBranch.LastCommitId);
        Assert.Equal(originalLastProcessedAt, updatedBranch.LastProcessedAt);
        analyzer.VerifyAll();
    }

    [Fact]
    public async Task CheckScheduledUpdatesAsync_WhenLocalGitSourceCommitChanges_CreatesPendingTask()
    {
        using var context = CreateContext();
        var repository = SeedRepository(
            context,
            updateIntervalMinutes: 60,
            lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2),
            gitUrl: RepositorySource.EncodeLocalDirectoryPath("/tmp/source-repo"));
        var branch = SeedBranch(context, repository.Id, "smart-hw/os_services_develop", GitCommitA);
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.GetRemoteBranchHeadCommitAsync(repository, branch.BranchName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitCommitB);

        var worker = CreateWorker();

        await InvokeCheckScheduledUpdatesAsync(worker, context, Mock.Of<IGitPlatformService>(), analyzer.Object);

        var task = await context.IncrementalUpdateTasks.SingleAsync();
        Assert.Equal(IncrementalUpdateStatus.Pending, task.Status);
        Assert.Equal(GitCommitA, task.PreviousCommitId);
        Assert.Equal(GitCommitB, task.TargetCommitId);
        Assert.False(task.IsManualTrigger);
        analyzer.VerifyAll();
    }

    [Fact]
    public async Task CheckScheduledUpdatesAsync_WhenLocalDirectoryHasNoGitHead_CreatesSnapshotTask()
    {
        using var context = CreateContext();
        var repository = SeedRepository(
            context,
            updateIntervalMinutes: 60,
            lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2),
            gitUrl: RepositorySource.EncodeLocalDirectoryPath("/tmp/plain-directory"));
        var branch = SeedBranch(context, repository.Id, "main", SnapshotHash);
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.GetRemoteBranchHeadCommitAsync(repository, branch.BranchName, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var worker = CreateWorker();

        await InvokeCheckScheduledUpdatesAsync(worker, context, Mock.Of<IGitPlatformService>(), analyzer.Object);

        var task = await context.IncrementalUpdateTasks.SingleAsync();
        Assert.Equal(SnapshotHash, task.PreviousCommitId);
        Assert.Null(task.TargetCommitId);
        Assert.False(task.IsManualTrigger);
        analyzer.VerifyAll();
    }

    [Fact]
    public async Task CheckScheduledUpdatesAsync_WhenPendingTaskExists_DoesNotCreateDuplicate()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, updateIntervalMinutes: 60, lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2));
        var branch = SeedBranch(context, repository.Id, "main", "old-sha");
        context.IncrementalUpdateTasks.Add(new IncrementalUpdateTask
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchId = branch.Id,
            Status = IncrementalUpdateStatus.Pending
        });
        await context.SaveChangesAsync();

        var worker = CreateWorker();

        await InvokeCheckScheduledUpdatesAsync(
            worker,
            context,
            Mock.Of<IGitPlatformService>(),
            new Mock<IRepositoryAnalyzer>(MockBehavior.Strict).Object);

        Assert.Single(await context.IncrementalUpdateTasks.ToListAsync());
    }

    [Fact]
    public async Task CheckScheduledUpdatesAsync_IgnoresSoftDeletedRepositoryAndBranch()
    {
        using var context = CreateContext();
        var deletedRepository = SeedRepository(
            context,
            updateIntervalMinutes: 60,
            lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2),
            isDeleted: true);
        SeedBranch(context, deletedRepository.Id, "main", "sha-1");

        var activeRepository = SeedRepository(
            context,
            updateIntervalMinutes: 60,
            lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2));
        SeedBranch(context, activeRepository.Id, "main", "sha-2", isDeleted: true);
        await context.SaveChangesAsync();

        var worker = CreateWorker();

        await InvokeCheckScheduledUpdatesAsync(
            worker,
            context,
            Mock.Of<IGitPlatformService>(),
            new Mock<IRepositoryAnalyzer>(MockBehavior.Strict).Object);

        Assert.Empty(await context.IncrementalUpdateTasks.ToListAsync());
    }

    [Fact]
    public async Task CheckScheduledUpdatesAsync_ClampsInvalidRepositoryIntervalToMinimum()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, updateIntervalMinutes: 0, lastUpdateCheckAt: DateTime.UtcNow.AddMinutes(-4));
        SeedBranch(context, repository.Id, "main", "same-sha");
        await context.SaveChangesAsync();

        var worker = CreateWorker(new IncrementalUpdateOptions
        {
            Enabled = true,
            PollingIntervalSeconds = 60,
            DefaultUpdateIntervalMinutes = 60,
            MinUpdateIntervalMinutes = 5,
            MaxRepositoriesPerPoll = 10
        });

        await InvokeCheckScheduledUpdatesAsync(
            worker,
            context,
            Mock.Of<IGitPlatformService>(),
            Mock.Of<IRepositoryAnalyzer>(MockBehavior.Strict));

        Assert.Empty(await context.IncrementalUpdateTasks.ToListAsync());
    }

    [Fact]
    public async Task CheckScheduledUpdatesAsync_WhenDisabled_SkipsAutomaticScan()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, updateIntervalMinutes: 60, lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2));
        SeedBranch(context, repository.Id, "main", "old-sha");
        await context.SaveChangesAsync();

        var worker = CreateWorker(new IncrementalUpdateOptions
        {
            Enabled = false,
            PollingIntervalSeconds = 60,
            DefaultUpdateIntervalMinutes = 60,
            MinUpdateIntervalMinutes = 5,
            MaxRepositoriesPerPoll = 10
        });

        await InvokeCheckScheduledUpdatesAsync(
            worker,
            context,
            new Mock<IGitPlatformService>(MockBehavior.Strict).Object,
            new Mock<IRepositoryAnalyzer>(MockBehavior.Strict).Object);

        Assert.Empty(await context.IncrementalUpdateTasks.ToListAsync());
    }

    private static IncrementalUpdateWorker CreateWorker(IncrementalUpdateOptions? options = null)
    {
        return new IncrementalUpdateWorker(
            Mock.Of<IServiceScopeFactory>(MockBehavior.Strict),
            NullLogger<IncrementalUpdateWorker>.Instance,
            Options.Create(options ?? new IncrementalUpdateOptions
            {
                Enabled = true,
                PollingIntervalSeconds = 60,
                DefaultUpdateIntervalMinutes = 60,
                MinUpdateIntervalMinutes = 5,
                MaxRepositoriesPerPoll = 10
            }));
    }

    private static async Task InvokeCheckScheduledUpdatesAsync(
        IncrementalUpdateWorker worker,
        TestDbContext context,
        IGitPlatformService gitPlatformService,
        IRepositoryAnalyzer analyzer)
    {
        var method = typeof(IncrementalUpdateWorker).GetMethod(
            "CheckScheduledUpdatesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task?)method!.Invoke(worker, [context, gitPlatformService, analyzer, CancellationToken.None]);
        Assert.NotNull(task);
        await task!;
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    private static Repository SeedRepository(
        TestDbContext context,
        int? updateIntervalMinutes,
        DateTime? lastUpdateCheckAt,
        bool isDeleted = false,
        string? gitUrl = null)
    {
        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = "user-1",
            GitUrl = gitUrl ?? "https://example.com/demo/repo.git",
            OrgName = "demo",
            RepoName = "repo",
            Status = RepositoryStatus.Completed,
            UpdateIntervalMinutes = updateIntervalMinutes,
            LastUpdateCheckAt = lastUpdateCheckAt,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null
        };

        context.Repositories.Add(repository);
        return repository;
    }

    private static RepositoryBranch SeedBranch(
        TestDbContext context,
        string repositoryId,
        string branchName,
        string? lastCommitId,
        bool isDeleted = false)
    {
        var branch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            BranchName = branchName,
            LastCommitId = lastCommitId,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null
        };

        context.RepositoryBranches.Add(branch);
        return branch;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : MasterDbContext(options)
    {
    }
}
