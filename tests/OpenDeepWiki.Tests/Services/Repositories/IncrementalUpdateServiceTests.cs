using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Notifications;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class IncrementalUpdateServiceTests
{
    [Fact]
    public async Task TriggerManualUpdateAsync_CreatesTaskEvenWhenNoRemoteChangeInformationExists()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, generateSkill: false);
        var branch = SeedBranch(context, repository.Id, "main", "same-sha");
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var taskId = await service.TriggerManualUpdateAsync(repository.Id, branch.Id);

        var task = await context.IncrementalUpdateTasks.SingleAsync(t => t.Id == taskId);
        Assert.True(task.IsManualTrigger);
        Assert.Equal(IncrementalUpdateStatus.Pending, task.Status);
        Assert.Equal("same-sha", task.PreviousCommitId);
    }

    [Theory]
    [InlineData("staged")]
    [InlineData("modified")]
    [InlineData("untracked")]
    public async Task TriggerManualUpdateAsync_WhenLocalGitIsDirty_RejectsAndPreservesState(string dirtyKind)
    {
        using var context = CreateContext();
        var repository = SeedRepository(
            context,
            generateSkill: false,
            RepositorySource.EncodeLocalDirectoryPath("/tmp/dirty-local-git"));
        var branch = SeedBranch(context, repository.Id, "main", "snapshot-baseline");
        var language = SeedBranchLanguage(context, branch.Id, "zh");
        var doc = new DocFile { Id = Guid.NewGuid().ToString(), BranchLanguageId = language.Id, Content = "existing" };
        context.DocFiles.Add(doc);
        context.DocCatalogs.Add(new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = language.Id,
            Title = "Existing",
            Path = "existing",
            DocFileId = doc.Id
        });
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer.Setup(item => item.GetLocalGitPreflightAsync(repository, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDirtyPreflight(dirtyKind));
        var service = CreateService(context, analyzer);

        var exception = await Assert.ThrowsAsync<LocalGitWorktreeDirtyException>(
            () => service.TriggerManualUpdateAsync(repository.Id, branch.Id));
        await Assert.ThrowsAsync<LocalGitWorktreeDirtyException>(
            () => service.ProcessIncrementalUpdateAsync(repository.Id, branch.Id));

        Assert.Contains("Commit, stash, or clean", exception.Message);
        Assert.Empty(await context.IncrementalUpdateTasks.ToListAsync());
        Assert.Equal("snapshot-baseline", branch.LastCommitId);
        Assert.Equal("existing", (await context.DocFiles.SingleAsync()).Content);
        Assert.Single(await context.DocCatalogs.ToListAsync());
        analyzer.Verify(item => item.PrepareWorkspaceAsync(
            It.IsAny<Repository>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessIncrementalUpdateAsync_WhenHeadUnchanged_ReturnsSuccessWithoutDocumentUpdates()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, generateSkill: false);
        var branch = SeedBranch(context, repository.Id, "main", "same-sha");
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.PrepareWorkspaceAsync(repository, branch.BranchName, "same-sha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryWorkspace
            {
                Organization = repository.OrgName,
                RepositoryName = repository.RepoName,
                BranchName = branch.BranchName,
                WorkingDirectory = "C:\\temp\\repo",
                CommitId = "same-sha",
                PreviousCommitId = "same-sha"
            });

        var service = CreateService(context, analyzer: analyzer);

        var result = await service.ProcessIncrementalUpdateAsync(repository.Id, branch.Id);

        Assert.True(result.Success);
        Assert.Equal(0, result.ChangedFilesCount);
        Assert.Equal(0, result.UpdatedDocumentsCount);
        analyzer.Verify(
            x => x.PrepareWorkspaceAsync(repository, branch.BranchName, "same-sha", It.IsAny<CancellationToken>()),
            Times.Once);
        analyzer.Verify(
            x => x.GetChangedFilesAsync(It.IsAny<RepositoryWorkspace>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessIncrementalUpdateAsync_WhenCommitChanges_PreparesWorkspaceOnceAndUpdatesBranch()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, generateSkill: false);
        var branch = SeedBranch(context, repository.Id, "main", "old-sha");
        SeedBranchLanguage(context, branch.Id, "zh");
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.PrepareWorkspaceAsync(repository, branch.BranchName, "old-sha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryWorkspace
            {
                Organization = repository.OrgName,
                RepositoryName = repository.RepoName,
                BranchName = branch.BranchName,
                WorkingDirectory = "C:\\temp\\repo",
                CommitId = "new-sha",
                PreviousCommitId = "old-sha"
            });
        analyzer
            .Setup(x => x.GetChangedFilesAsync(
                It.IsAny<RepositoryWorkspace>(),
                "old-sha",
                "new-sha",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["src/app.cs"]);

        var wikiGenerator = new Mock<IWikiGenerator>(MockBehavior.Strict);
        wikiGenerator
            .Setup(x => x.IncrementalUpdateAsync(
                It.IsAny<RepositoryWorkspace>(),
                It.IsAny<BranchLanguage>(),
                It.Is<string[]>(files => files.SequenceEqual(new[] { "src/app.cs" })),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var notificationService = new Mock<ISubscriberNotificationService>(MockBehavior.Strict);
        notificationService
            .Setup(x => x.NotifySubscribersAsync(
                It.Is<RepositoryUpdateNotification>(n => n.RepositoryId == repository.Id && n.CommitId == "new-sha"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            context,
            analyzer: analyzer,
            wikiGenerator: wikiGenerator,
            notificationService: notificationService);

        var result = await service.ProcessIncrementalUpdateAsync(repository.Id, branch.Id);

        Assert.True(result.Success);
        Assert.Equal(1, result.ChangedFilesCount);
        Assert.Equal(1, result.UpdatedDocumentsCount);

        var updatedBranch = await context.RepositoryBranches.SingleAsync(b => b.Id == branch.Id);
        Assert.Equal("new-sha", updatedBranch.LastCommitId);
        Assert.NotNull(updatedBranch.LastProcessedAt);

        analyzer.Verify(
            x => x.PrepareWorkspaceAsync(repository, branch.BranchName, "old-sha", It.IsAny<CancellationToken>()),
            Times.Once);
        wikiGenerator.VerifyAll();
        notificationService.VerifyAll();
    }

    [Fact]
    public async Task ProcessIncrementalUpdateAsync_ProcessesOnlyConfiguredLanguages()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, generateSkill: false);
        var branch = SeedBranch(context, repository.Id, "main", "old-sha");
        var zhLanguage = SeedBranchLanguage(context, branch.Id, "zh");
        var enLanguage = SeedBranchLanguage(context, branch.Id, "en");
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.PrepareWorkspaceAsync(repository, branch.BranchName, "old-sha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryWorkspace
            {
                Organization = repository.OrgName,
                RepositoryName = repository.RepoName,
                BranchName = branch.BranchName,
                WorkingDirectory = "C:\\temp\\repo",
                CommitId = "new-sha",
                PreviousCommitId = "old-sha"
            });
        analyzer
            .Setup(x => x.GetChangedFilesAsync(
                It.IsAny<RepositoryWorkspace>(),
                "old-sha",
                "new-sha",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["src/app.cs"]);

        var wikiGenerator = new Mock<IWikiGenerator>(MockBehavior.Strict);
        wikiGenerator
            .Setup(x => x.IncrementalUpdateAsync(
                It.IsAny<RepositoryWorkspace>(),
                It.Is<BranchLanguage>(language => language.Id == zhLanguage.Id),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>(),
                null,
                null))
            .Returns(Task.CompletedTask);

        var service = CreateService(
            context,
            analyzer: analyzer,
            wikiGenerator: wikiGenerator,
            wikiOptions: new WikiGeneratorOptions { Languages = " zh " });

        var result = await service.ProcessIncrementalUpdateAsync(repository.Id, branch.Id);

        Assert.True(result.Success);
        Assert.Equal(1, result.UpdatedDocumentsCount);
        wikiGenerator.Verify(
            x => x.IncrementalUpdateAsync(
                It.IsAny<RepositoryWorkspace>(),
                It.Is<BranchLanguage>(language => language.Id == zhLanguage.Id),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>(),
                null,
                null),
            Times.Once);
        wikiGenerator.Verify(
            x => x.IncrementalUpdateAsync(
                It.IsAny<RepositoryWorkspace>(),
                It.Is<BranchLanguage>(language => language.Id == enLanguage.Id),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>(),
                null,
                null),
            Times.Never);
    }

    [Fact]
    public async Task ProcessIncrementalUpdateAsync_WhenNoConfiguredLanguageMatches_FailsWithoutAdvancingBaseline()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, generateSkill: false);
        var branch = SeedBranch(context, repository.Id, "main", "old-sha");
        SeedBranchLanguage(context, branch.Id, "en");
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.PrepareWorkspaceAsync(repository, branch.BranchName, "old-sha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryWorkspace
            {
                Organization = repository.OrgName,
                RepositoryName = repository.RepoName,
                BranchName = branch.BranchName,
                WorkingDirectory = "C:\\temp\\repo",
                CommitId = "new-sha",
                PreviousCommitId = "old-sha"
            });
        analyzer
            .Setup(x => x.GetChangedFilesAsync(
                It.IsAny<RepositoryWorkspace>(),
                "old-sha",
                "new-sha",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["src/app.cs"]);

        var service = CreateService(
            context,
            analyzer: analyzer,
            wikiOptions: new WikiGeneratorOptions { Languages = "zh" });

        var result = await service.ProcessIncrementalUpdateAsync(repository.Id, branch.Id);

        Assert.False(result.Success);
        Assert.Contains("No configured languages match", result.ErrorMessage, StringComparison.Ordinal);
        var persistedBranch = await context.RepositoryBranches.SingleAsync(item => item.Id == branch.Id);
        Assert.Equal("old-sha", persistedBranch.LastCommitId);
        Assert.Null(persistedBranch.LastProcessedAt);
    }

    [Fact]
    public async Task ProcessIncrementalUpdateAsync_LocalGitWithLease_PublishesDraftAtomically()
    {
        using var context = CreateContext();
        var repository = SeedRepository(
            context,
            generateSkill: false,
            RepositorySource.EncodeLocalDirectoryPath("/tmp/local-git-draft"));
        var branch = SeedBranch(context, repository.Id, "main", "old-sha");
        var language = SeedBranchLanguage(context, branch.Id, "zh");
        var document = new DocFile
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = language.Id,
            Content = "live-doc"
        };
        context.DocFiles.Add(document);
        context.DocCatalogs.Add(new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = language.Id,
            Title = "Page",
            Path = "page",
            DocFileId = document.Id
        });
        var task = new IncrementalUpdateTask
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchId = branch.Id,
            Status = IncrementalUpdateStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };
        var generationLock = new RepositoryGenerationLock
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            OwnerType = RepositoryGenerationLockOwnerType.IncrementalTask,
            OwnerId = task.Id,
            Scope = RepositoryGenerationLockScope.Branch,
            AcquiredAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.AddRange(task, generationLock);
        await context.SaveChangesAsync();
        var lease = new GenerationLeaseHandle(
            generationLock.Id,
            repository.Id,
            generationLock.OwnerType,
            task.Id);

        var cleanPreflight = new LocalGitPreflightResult(true, "source-head", [], [], []);
        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(item => item.GetLocalGitPreflightAsync(repository, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cleanPreflight);
        analyzer
            .Setup(item => item.PrepareWorkspaceAsync(
                repository,
                branch.BranchName,
                "old-sha",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryWorkspace
            {
                Organization = repository.OrgName,
                RepositoryName = repository.RepoName,
                BranchName = branch.BranchName,
                WorkingDirectory = "/tmp/draft-workspace",
                CommitId = "new-sha",
                PreviousCommitId = "old-sha",
                SupportsIncrementalUpdates = true
            });
        analyzer
            .Setup(item => item.GetChangedFilesAsync(
                It.IsAny<RepositoryWorkspace>(),
                "old-sha",
                "new-sha",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["src/app.cs"]);

        var wikiGenerator = new Mock<IWikiGenerator>(MockBehavior.Strict);
        wikiGenerator
            .Setup(item => item.IncrementalUpdateAsync(
                It.IsAny<RepositoryWorkspace>(),
                language,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>(),
                lease,
                It.IsAny<IIncrementalWikiDraft>()))
            .Returns((RepositoryWorkspace _, BranchLanguage _, string[] _, CancellationToken cancellationToken,
                GenerationLeaseHandle? _, IIncrementalWikiDraft draft) =>
                draft.WriteDocumentAsync(language.Id, "page", "published-doc", null, cancellationToken));
        var notificationService = new Mock<ISubscriberNotificationService>(MockBehavior.Strict);
        notificationService
            .Setup(item => item.NotifySubscribersAsync(
                It.IsAny<RepositoryUpdateNotification>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var options = Options.Create(new IncrementalUpdateOptions());
        var writeGuard = new GenerationWriteGuard(options);
        var publisher = new IncrementalWikiPublisher(context, analyzer.Object, writeGuard);
        var service = new IncrementalUpdateService(
            analyzer.Object,
            wikiGenerator.Object,
            Mock.Of<IRepositorySkillMarkdownBuilder>(MockBehavior.Strict),
            notificationService.Object,
            context,
            options,
            Mock.Of<ILogger<IncrementalUpdateService>>(),
            writeGuard,
            publisher);

        var result = await service.ProcessIncrementalUpdateAsync(
            repository.Id,
            branch.Id,
            lease: lease);

        Assert.True(result.Success);
        Assert.True(result.PublishedAtomically);
        Assert.Equal("published-doc", (await context.DocFiles.SingleAsync()).Content);
        Assert.Equal("new-sha", (await context.RepositoryBranches.SingleAsync()).LastCommitId);
        Assert.Equal(IncrementalUpdateStatus.Completed, (await context.IncrementalUpdateTasks.SingleAsync()).Status);
        wikiGenerator.VerifyAll();
        notificationService.VerifyAll();
        analyzer.VerifyAll();
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task ProcessIncrementalUpdateAsync_WhenSourceBecomesDirtyBeforeFencedWrite_PreservesBaseline(
        int dirtyPreflightNumber,
        bool wikiUpdateExpected)
    {
        using var context = CreateContext();
        var repository = SeedRepository(
            context,
            generateSkill: false,
            RepositorySource.EncodeLocalDirectoryPath("/tmp/racing-local-git"));
        var branch = SeedBranch(context, repository.Id, "main", "old-sha");
        var language = SeedBranchLanguage(context, branch.Id, "zh");
        var doc = new DocFile
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = language.Id,
            Content = "existing-doc"
        };
        context.DocFiles.Add(doc);
        context.DocCatalogs.Add(new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = language.Id,
            Title = "Existing",
            Path = "existing",
            DocFileId = doc.Id
        });
        await context.SaveChangesAsync();

        var clean = new LocalGitPreflightResult(true, "new-sha", [], [], []);
        var dirty = new LocalGitPreflightResult(true, "new-sha", [], [], ["raced.txt"]);
        var preflightChecks = 0;
        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(item => item.GetLocalGitPreflightAsync(repository, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref preflightChecks) == dirtyPreflightNumber
                ? dirty
                : clean);
        analyzer
            .Setup(item => item.PrepareWorkspaceAsync(
                repository,
                branch.BranchName,
                "old-sha",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryWorkspace
            {
                Organization = repository.OrgName,
                RepositoryName = repository.RepoName,
                BranchName = branch.BranchName,
                WorkingDirectory = "/tmp/prepared-workspace",
                CommitId = "new-sha",
                PreviousCommitId = "old-sha",
                SupportsIncrementalUpdates = true
            });
        analyzer
            .Setup(item => item.GetChangedFilesAsync(
                It.IsAny<RepositoryWorkspace>(),
                "old-sha",
                "new-sha",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(["src/app.cs"]);

        var wikiGenerator = new Mock<IWikiGenerator>(MockBehavior.Strict);
        if (wikiUpdateExpected)
        {
            wikiGenerator
                .Setup(item => item.IncrementalUpdateAsync(
                    It.IsAny<RepositoryWorkspace>(),
                    language,
                    It.Is<string[]>(files => files.SequenceEqual(new[] { "src/app.cs" })),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        var notificationService = new Mock<ISubscriberNotificationService>(MockBehavior.Strict);
        var service = CreateService(
            context,
            analyzer,
            wikiGenerator,
            notificationService);

        await Assert.ThrowsAsync<LocalGitWorktreeDirtyException>(
            () => service.ProcessIncrementalUpdateAsync(repository.Id, branch.Id));

        Assert.Equal(dirtyPreflightNumber, preflightChecks);
        Assert.Equal("old-sha", branch.LastCommitId);
        Assert.Equal("existing-doc", (await context.DocFiles.SingleAsync()).Content);
        Assert.Single(await context.DocCatalogs.ToListAsync());
        wikiGenerator.VerifyAll();
        notificationService.VerifyNoOtherCalls();
        analyzer.VerifyAll();
    }

    [Fact]
    public async Task ProcessIncrementalUpdateAsync_WhenCommitAdvancesWithoutChangedFiles_StillAdvancesStoredCommit()
    {
        using var context = CreateContext();
        var repository = SeedRepository(context, generateSkill: false);
        var branch = SeedBranch(context, repository.Id, "main", "old-sha");
        await context.SaveChangesAsync();

        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(x => x.PrepareWorkspaceAsync(repository, branch.BranchName, "old-sha", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RepositoryWorkspace
            {
                Organization = repository.OrgName,
                RepositoryName = repository.RepoName,
                BranchName = branch.BranchName,
                WorkingDirectory = "C:\\temp\\repo",
                CommitId = "new-sha",
                PreviousCommitId = "old-sha"
            });
        analyzer
            .Setup(x => x.GetChangedFilesAsync(
                It.IsAny<RepositoryWorkspace>(),
                "old-sha",
                "new-sha",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var service = CreateService(context, analyzer: analyzer);

        var result = await service.ProcessIncrementalUpdateAsync(repository.Id, branch.Id);

        Assert.True(result.Success);
        Assert.Equal(0, result.ChangedFilesCount);

        var updatedBranch = await context.RepositoryBranches.SingleAsync(b => b.Id == branch.Id);
        Assert.Equal("new-sha", updatedBranch.LastCommitId);
        Assert.NotNull(updatedBranch.LastProcessedAt);
    }

    private static IncrementalUpdateService CreateService(
        TestDbContext context,
        Mock<IRepositoryAnalyzer>? analyzer = null,
        Mock<IWikiGenerator>? wikiGenerator = null,
        Mock<ISubscriberNotificationService>? notificationService = null,
        WikiGeneratorOptions? wikiOptions = null)
    {
        analyzer ??= new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        wikiGenerator ??= new Mock<IWikiGenerator>(MockBehavior.Strict);
        if (notificationService == null)
        {
            notificationService = new Mock<ISubscriberNotificationService>(MockBehavior.Strict);
            notificationService
                .Setup(x => x.NotifySubscribersAsync(It.IsAny<RepositoryUpdateNotification>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        var wikiOptionsMonitor = new Mock<IOptionsMonitor<WikiGeneratorOptions>>();
        wikiOptionsMonitor
            .SetupGet(item => item.CurrentValue)
            .Returns(wikiOptions ?? new WikiGeneratorOptions());

        return new IncrementalUpdateService(
            analyzer.Object,
            wikiGenerator.Object,
            Mock.Of<IRepositorySkillMarkdownBuilder>(),
            notificationService.Object,
            context,
            Options.Create(new IncrementalUpdateOptions()),
            Mock.Of<ILogger<IncrementalUpdateService>>(),
            wikiOptionsMonitor: wikiOptionsMonitor.Object);
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
        bool generateSkill,
        string? gitUrl = null)
    {
        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = "user-1",
            GitUrl = gitUrl ?? "https://github.com/demo/repo.git",
            OrgName = "demo",
            RepoName = "repo",
            Status = RepositoryStatus.Completed,
            GenerateSkill = generateSkill
        };

        context.Repositories.Add(repository);
        return repository;
    }

    private static LocalGitPreflightResult CreateDirtyPreflight(string dirtyKind) => dirtyKind switch
    {
        "staged" => new LocalGitPreflightResult(true, "head", ["staged.cs"], [], []),
        "modified" => new LocalGitPreflightResult(true, "head", [], ["modified.cs"], []),
        "untracked" => new LocalGitPreflightResult(true, "head", [], [], ["untracked.cs"]),
        _ => throw new ArgumentOutOfRangeException(nameof(dirtyKind))
    };

    private static RepositoryBranch SeedBranch(
        TestDbContext context,
        string repositoryId,
        string branchName,
        string? lastCommitId)
    {
        var branch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            BranchName = branchName,
            LastCommitId = lastCommitId
        };

        context.RepositoryBranches.Add(branch);
        return branch;
    }

    private static BranchLanguage SeedBranchLanguage(
        TestDbContext context,
        string branchId,
        string languageCode)
    {
        var language = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branchId,
            LanguageCode = languageCode,
            IsDefault = true
        };

        context.BranchLanguages.Add(language);
        return language;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : MasterDbContext(options)
    {
    }
}
