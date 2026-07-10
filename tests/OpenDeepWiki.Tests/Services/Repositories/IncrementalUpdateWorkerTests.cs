using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Notifications;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using Xunit;
using GitCommitOptions = LibGit2Sharp.CommitOptions;
using GitCommands = LibGit2Sharp.Commands;
using GitRepository = LibGit2Sharp.Repository;
using GitSignature = LibGit2Sharp.Signature;

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
        analyzer.Setup(x => x.GetLocalGitPreflightAsync(repository, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalGitPreflightResult(true, GitCommitA, [], [], []));
        analyzer
            .Setup(x => x.GetRemoteBranchHeadCommitAsync(repository, branch.BranchName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitCommitA);
        analyzer
            .Setup(x => x.CanNormalizeLocalGitSnapshotAsync(
                repository,
                SnapshotHash,
                GitCommitA,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var worker = CreateWorker();

        await InvokeCheckScheduledUpdatesAsync(worker, context, Mock.Of<IGitPlatformService>(), analyzer.Object);

        Assert.Empty(await context.IncrementalUpdateTasks.ToListAsync());
        var updatedBranch = await context.RepositoryBranches.SingleAsync(b => b.Id == branch.Id);
        Assert.Equal(GitCommitA, updatedBranch.LastCommitId);
        Assert.Equal(originalLastProcessedAt, updatedBranch.LastProcessedAt);
        analyzer.VerifyAll();
    }

    [Fact]
    public async Task CheckScheduledUpdatesAsync_WhenLocalGitSnapshotDoesNotMatchSource_DoesNotNormalizeBaseline()
    {
        using var context = CreateContext();
        var repository = SeedRepository(
            context,
            updateIntervalMinutes: 60,
            lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2),
            gitUrl: RepositorySource.EncodeLocalDirectoryPath("/tmp/dirty-source-repo"));
        var branch = SeedBranch(context, repository.Id, "main", SnapshotHash);
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer.Setup(x => x.GetLocalGitPreflightAsync(repository, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalGitPreflightResult(true, GitCommitA, [], [], []));
        analyzer
            .Setup(x => x.GetRemoteBranchHeadCommitAsync(repository, branch.BranchName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitCommitA);
        analyzer
            .Setup(x => x.CanNormalizeLocalGitSnapshotAsync(
                repository,
                SnapshotHash,
                GitCommitA,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await InvokeCheckScheduledUpdatesAsync(
            CreateWorker(),
            context,
            Mock.Of<IGitPlatformService>(),
            analyzer.Object);

        var task = await context.IncrementalUpdateTasks.SingleAsync();
        Assert.Equal(IncrementalUpdateStatus.Pending, task.Status);
        Assert.Equal(SnapshotHash, task.PreviousCommitId);
        Assert.Equal(GitCommitA, task.TargetCommitId);
        Assert.Equal(SnapshotHash, branch.LastCommitId);
        analyzer.VerifyAll();
    }

    [Theory]
    [InlineData("staged")]
    [InlineData("modified")]
    [InlineData("untracked")]
    public async Task CheckScheduledUpdatesAsync_WhenLocalGitIsDirty_PreservesBaselineAndDocuments(string dirtyKind)
    {
        using var context = CreateContext();
        var repository = SeedRepository(
            context,
            60,
            DateTime.UtcNow.AddHours(-2),
            gitUrl: RepositorySource.EncodeLocalDirectoryPath("/tmp/dirty-source"));
        var branch = SeedBranch(context, repository.Id, "main", SnapshotHash);
        var languageId = Guid.NewGuid().ToString();
        var docId = Guid.NewGuid().ToString();
        context.BranchLanguages.Add(new BranchLanguage
        {
            Id = languageId,
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true
        });
        context.DocFiles.Add(new DocFile
        {
            Id = docId,
            BranchLanguageId = languageId,
            Content = "existing-doc"
        });
        context.DocCatalogs.Add(new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = languageId,
            Title = "Existing",
            Path = "existing",
            DocFileId = docId
        });
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer.Setup(item => item.GetLocalGitPreflightAsync(repository, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDirtyPreflight(dirtyKind));

        await InvokeCheckScheduledUpdatesAsync(
            CreateWorker(), context, Mock.Of<IGitPlatformService>(), analyzer.Object);

        Assert.Equal(SnapshotHash, branch.LastCommitId);
        Assert.Equal("existing-doc", (await context.DocFiles.SingleAsync()).Content);
        Assert.Single(await context.DocCatalogs.ToListAsync());
        var audit = await context.IncrementalUpdateTasks.SingleAsync();
        Assert.Equal(IncrementalUpdateStatus.Cancelled, audit.Status);
        Assert.StartsWith(LocalGitWorktreeDirtyException.ErrorCode, audit.ErrorMessage);
        Assert.DoesNotContain(await context.IncrementalUpdateTasks.ToListAsync(), task =>
            task.Status is IncrementalUpdateStatus.Pending or IncrementalUpdateStatus.Processing);
        analyzer.VerifyAll();
    }

    [Fact]
    public async Task ProcessSingleTaskAsync_WhenSourceBecomesDirtyAfterEntryPreflight_CancelsWithoutWorkspaceOrWrites()
    {
        var repositoriesRoot = CreateTempDirectory();
        var sourceRoot = CreateTempDirectory();
        GitRepository.Init(sourceRoot);
        string sourceBranch;
        string sourceCommit;
        using (var sourceRepository = new GitRepository(sourceRoot))
        {
            File.WriteAllText(Path.Combine(sourceRoot, "tracked.txt"), "original");
            GitCommands.Stage(sourceRepository, "tracked.txt");
            var signature = new GitSignature("OpenDeepWiki Tests", "tests@example.com", DateTimeOffset.UtcNow);
            sourceCommit = sourceRepository.Commit(
                "initial",
                signature,
                signature,
                new GitCommitOptions()).Sha;
            sourceBranch = sourceRepository.Head.FriendlyName;
        }

        using var context = CreateContext();
        var repository = SeedRepository(
            context,
            updateIntervalMinutes: 60,
            lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2),
            gitUrl: RepositorySource.EncodeLocalDirectoryPath(sourceRoot));
        var branch = SeedBranch(context, repository.Id, sourceBranch, sourceCommit);
        var languageId = Guid.NewGuid().ToString();
        var docId = Guid.NewGuid().ToString();
        context.BranchLanguages.Add(new BranchLanguage
        {
            Id = languageId,
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true
        });
        context.DocFiles.Add(new DocFile
        {
            Id = docId,
            BranchLanguageId = languageId,
            Content = "existing-doc"
        });
        context.DocCatalogs.Add(new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = languageId,
            Title = "Existing",
            Path = "existing",
            DocFileId = docId
        });
        var task = new IncrementalUpdateTask
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchId = branch.Id,
            PreviousCommitId = sourceCommit,
            Status = IncrementalUpdateStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        context.IncrementalUpdateTasks.Add(task);
        await context.SaveChangesAsync();

        var analyzerOptions = new RepositoryAnalyzerOptions
        {
            RepositoriesDirectory = repositoriesRoot,
            AllowedLocalPathRoots = [Path.GetDirectoryName(sourceRoot)!],
            MaxRetryAttempts = 1
        };
        var realAnalyzer = new RepositoryAnalyzer(
            Options.Create(analyzerOptions),
            NullLogger<RepositoryAnalyzer>.Instance);
        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        var preflightChecks = 0;
        analyzer
            .Setup(item => item.GetLocalGitPreflightAsync(repository, It.IsAny<CancellationToken>()))
            .Returns(async (Repository _, CancellationToken cancellationToken) =>
            {
                var cleanPreflight = await realAnalyzer.GetLocalGitPreflightAsync(repository, cancellationToken);
                if (Interlocked.Increment(ref preflightChecks) == 2)
                {
                    File.WriteAllText(Path.Combine(sourceRoot, "became-dirty.txt"), "dirty after entry preflight");
                }

                return cleanPreflight;
            });
        analyzer
            .Setup(item => item.PrepareWorkspaceAsync(
                repository,
                sourceBranch,
                sourceCommit,
                It.IsAny<CancellationToken>()))
            .Returns((Repository _, string branchName, string? previousCommitId, CancellationToken cancellationToken) =>
                realAnalyzer.PrepareWorkspaceAsync(repository, branchName, previousCommitId, cancellationToken));

        var wikiGenerator = new Mock<IWikiGenerator>(MockBehavior.Strict);
        var skillBuilder = new Mock<IRepositorySkillMarkdownBuilder>(MockBehavior.Strict);
        var notificationService = new Mock<ISubscriberNotificationService>(MockBehavior.Strict);
        var updateOptions = Options.Create(new IncrementalUpdateOptions { MaxRetryAttempts = 1 });
        var updateService = new IncrementalUpdateService(
            analyzer.Object,
            wikiGenerator.Object,
            skillBuilder.Object,
            notificationService.Object,
            context,
            updateOptions,
            NullLogger<IncrementalUpdateService>.Instance,
            new GenerationWriteGuard(updateOptions));
        var workerServices = new ServiceCollection();
        workerServices.AddSingleton<IContext>(context);
        workerServices.AddScoped<IRepositoryGenerationLockService, RepositoryGenerationLockService>();
        await using var workerProvider = workerServices.BuildServiceProvider();
        var worker = new IncrementalUpdateWorker(
            workerProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IncrementalUpdateWorker>.Instance,
            Options.Create(new IncrementalUpdateOptions()));

        await InvokeProcessSingleTaskAsync(
            worker,
            context,
            updateService,
            new RepositoryGenerationLockService(context),
            task,
            analyzer.Object);

        Assert.Equal(2, preflightChecks);
        Assert.Equal(IncrementalUpdateStatus.Cancelled, task.Status);
        Assert.StartsWith(LocalGitWorktreeDirtyException.ErrorCode, task.ErrorMessage);
        Assert.Equal(sourceCommit, branch.LastCommitId);
        Assert.Equal("existing-doc", (await context.DocFiles.SingleAsync()).Content);
        Assert.Single(await context.DocCatalogs.ToListAsync());
        Assert.Empty(await context.RepositoryGenerationLocks.ToListAsync());
        Assert.False(Directory.Exists(Path.Combine(
            repositoriesRoot,
            repository.OrgName,
            repository.RepoName,
            "branches",
            sourceBranch,
            "tree")));
        wikiGenerator.VerifyNoOtherCalls();
        skillBuilder.VerifyNoOtherCalls();
        notificationService.VerifyNoOtherCalls();
        analyzer.VerifyAll();
    }

    [Fact]
    public async Task RecoverStaleTasksAsync_WhenProcessingTaskHasNoLease_CancelsWithoutDeletingDocuments()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, 60, DateTime.UtcNow.AddHours(-2));
        var branch = SeedBranch(context, repository.Id, "main", GitCommitA);
        var branchLanguageId = Guid.NewGuid().ToString();
        var docFileId = Guid.NewGuid().ToString();
        context.BranchLanguages.Add(new BranchLanguage
        {
            Id = branchLanguageId,
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true
        });
        context.DocFiles.Add(new DocFile
        {
            Id = docFileId,
            BranchLanguageId = branchLanguageId,
            Content = "# Existing wiki"
        });
        context.DocCatalogs.Add(new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = branchLanguageId,
            Title = "Existing wiki",
            Path = "existing-wiki",
            DocFileId = docFileId
        });
        var task = SeedProcessingTask(context, repository.Id, branch.Id, DateTime.UtcNow.AddHours(-2));
        await context.SaveChangesAsync();

        await InvokeRecoverStaleTasksAsync(CreateWorker(), context);

        Assert.Equal(IncrementalUpdateStatus.Cancelled, task.Status);
        Assert.Contains("no active generation lease", task.ErrorMessage);
        Assert.Single(await context.DocFiles.ToListAsync());
        Assert.Single(await context.DocCatalogs.ToListAsync());
        Assert.Equal(GitCommitA, branch.LastCommitId);
    }

    [Fact]
    public async Task RecoverStaleTasksAsync_WhenLongRunningTaskHasRecentHeartbeat_DoesNotReleaseLease()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, 60, DateTime.UtcNow.AddHours(-2));
        var branch = SeedBranch(context, repository.Id, "main", GitCommitA);
        var task = SeedProcessingTask(context, repository.Id, branch.Id, DateTime.UtcNow.AddHours(-2));
        context.RepositoryGenerationLocks.Add(new RepositoryGenerationLock
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            OwnerType = RepositoryGenerationLockOwnerType.IncrementalTask,
            OwnerId = task.Id,
            Scope = RepositoryGenerationLockScope.Branch,
            AcquiredAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
        });
        await context.SaveChangesAsync();

        await InvokeRecoverStaleTasksAsync(CreateWorker(), context);

        Assert.Equal(IncrementalUpdateStatus.Processing, task.Status);
        Assert.Single(await context.RepositoryGenerationLocks.ToListAsync());
    }

    [Fact]
    public async Task ProcessSingleTaskAsync_WhenTaskRunsLongerThanItsRecordedAge_HeartbeatPreventsRecovery()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), databaseRoot)
            .Options;
        await using var setupContext = new TestDbContext(options);
        var repository = SeedRepository(setupContext, 60, DateTime.UtcNow.AddHours(-2));
        var branch = SeedBranch(setupContext, repository.Id, "main", GitCommitA);
        var task = new IncrementalUpdateTask
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchId = branch.Id,
            Status = IncrementalUpdateStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        setupContext.IncrementalUpdateTasks.Add(task);
        await setupContext.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddScoped<IContext>(_ => new TestDbContext(options));
        services.AddScoped<IRepositoryGenerationLockService, RepositoryGenerationLockService>();
        await using var provider = services.BuildServiceProvider();
        var worker = new IncrementalUpdateWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IncrementalUpdateWorker>.Instance,
            Options.Create(new IncrementalUpdateOptions
            {
                Enabled = true,
                LeaseHeartbeatIntervalSeconds = 1,
                StaleTaskTimeoutMinutes = 1
            }));
        var updateService = new Mock<IIncrementalUpdateService>(MockBehavior.Strict);
        updateService
            .Setup(service => service.ProcessIncrementalUpdateAsync(
                repository.Id,
                branch.Id,
                It.IsAny<CancellationToken>(),
                It.IsAny<GenerationLeaseHandle?>()))
            .Returns(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1200));
                await using var recoveryContext = new TestDbContext(options);
                var persistedTask = await recoveryContext.IncrementalUpdateTasks.SingleAsync(item => item.Id == task.Id);
                persistedTask.StartedAt = DateTime.UtcNow.AddHours(-2);
                persistedTask.UpdatedAt = DateTime.UtcNow.AddHours(-2);
                await recoveryContext.SaveChangesAsync();

                await InvokeRecoverStaleTasksAsync(worker, recoveryContext);

                Assert.Equal(IncrementalUpdateStatus.Processing, persistedTask.Status);
                Assert.Single(await recoveryContext.RepositoryGenerationLocks.ToListAsync());
                return new IncrementalUpdateResult
                {
                    Success = true,
                    PreviousCommitId = GitCommitA,
                    CurrentCommitId = GitCommitA
                };
            });

        await InvokeProcessSingleTaskAsync(
            worker,
            setupContext,
            updateService.Object,
            new RepositoryGenerationLockService(setupContext),
            task);

        Assert.Equal(IncrementalUpdateStatus.Completed, task.Status);
        await using var verificationContext = new TestDbContext(options);
        Assert.Empty(await verificationContext.RepositoryGenerationLocks.ToListAsync());
        updateService.VerifyAll();
    }

    [Fact]
    public async Task RecoverStaleTasksAsync_WhenLeaseHeartbeatExpires_CancelsAndReleasesLease()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, 60, DateTime.UtcNow.AddHours(-2));
        var branch = SeedBranch(context, repository.Id, "main", GitCommitA);
        var task = SeedProcessingTask(context, repository.Id, branch.Id, DateTime.UtcNow.AddHours(-2));
        context.RepositoryGenerationLocks.Add(new RepositoryGenerationLock
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            OwnerType = RepositoryGenerationLockOwnerType.IncrementalTask,
            OwnerId = task.Id,
            Scope = RepositoryGenerationLockScope.Branch,
            AcquiredAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await context.SaveChangesAsync();

        await InvokeRecoverStaleTasksAsync(CreateWorker(), context);

        Assert.Equal(IncrementalUpdateStatus.Cancelled, task.Status);
        Assert.Contains("generation lease expired", task.ErrorMessage);
        Assert.Empty(await context.RepositoryGenerationLocks.ToListAsync());
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
        analyzer.Setup(x => x.GetLocalGitPreflightAsync(repository, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalGitPreflightResult(true, GitCommitB, [], [], []));
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
    public async Task CheckScheduledUpdatesAsync_WhenGitRepositoryHasLegacyBaseline_CreatesPendingTask()
    {
        using var context = CreateContext();
        var repository = SeedRepository(
            context,
            updateIntervalMinutes: 60,
            lastUpdateCheckAt: DateTime.UtcNow.AddHours(-2));
        var branch = SeedBranch(context, repository.Id, "main", SnapshotHash);
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.GetRemoteBranchHeadCommitAsync(repository, branch.BranchName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitCommitA);

        var worker = CreateWorker();

        await InvokeCheckScheduledUpdatesAsync(worker, context, Mock.Of<IGitPlatformService>(), analyzer.Object);

        var task = await context.IncrementalUpdateTasks.SingleAsync();
        Assert.Equal(IncrementalUpdateStatus.Pending, task.Status);
        Assert.Equal(SnapshotHash, task.PreviousCommitId);
        Assert.Equal(GitCommitA, task.TargetCommitId);
        Assert.False(task.IsManualTrigger);
        var updatedBranch = await context.RepositoryBranches.SingleAsync(b => b.Id == branch.Id);
        Assert.Equal(SnapshotHash, updatedBranch.LastCommitId);
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
        analyzer.Setup(x => x.GetLocalGitPreflightAsync(repository, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalGitPreflightResult(false, null, [], [], []));
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

    private static async Task InvokeRecoverStaleTasksAsync(
        IncrementalUpdateWorker worker,
        TestDbContext context)
    {
        var method = typeof(IncrementalUpdateWorker).GetMethod(
            "RecoverStaleTasksAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var invocation = (Task?)method!.Invoke(worker, [context, CancellationToken.None]);
        Assert.NotNull(invocation);
        await invocation!;
    }

    private static async Task InvokeProcessSingleTaskAsync(
        IncrementalUpdateWorker worker,
        TestDbContext context,
        IIncrementalUpdateService updateService,
        IRepositoryGenerationLockService lockService,
        IncrementalUpdateTask task,
        IRepositoryAnalyzer? repositoryAnalyzer = null)
    {
        var method = typeof(IncrementalUpdateWorker).GetMethod(
            "ProcessSingleTaskAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var invocation = (Task?)method!.Invoke(
            worker,
            [
                context,
                updateService,
                repositoryAnalyzer ?? Mock.Of<IRepositoryAnalyzer>(MockBehavior.Strict),
                lockService,
                new GenerationWriteGuard(Options.Create(new IncrementalUpdateOptions())),
                task,
                CancellationToken.None
            ]);
        Assert.NotNull(invocation);
        await invocation!;
    }

    private static IncrementalUpdateTask SeedProcessingTask(
        TestDbContext context,
        string repositoryId,
        string branchId,
        DateTime updatedAt)
    {
        var task = new IncrementalUpdateTask
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            BranchId = branchId,
            Status = IncrementalUpdateStatus.Processing,
            StartedAt = updatedAt,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };
        context.IncrementalUpdateTasks.Add(task);
        return task;
    }

    private static LocalGitPreflightResult CreateDirtyPreflight(string dirtyKind) => dirtyKind switch
    {
        "staged" => new LocalGitPreflightResult(true, GitCommitA, ["staged.cs"], [], []),
        "modified" => new LocalGitPreflightResult(true, GitCommitA, [], ["modified.cs"], []),
        "untracked" => new LocalGitPreflightResult(true, GitCommitA, [], [], ["untracked.cs"]),
        _ => throw new ArgumentOutOfRangeException(nameof(dirtyKind))
    };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenDeepWiki.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
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
