using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using OpenDeepWiki.Agents.Tools;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class IncrementalWikiDraftPublisherTests
{
    private const string OldHead = "4d24129e28470a23b7d755ba1eeb58fce09447ac";
    private const string NewHead = "71d424d03595ecc2350215bbf03f6e6944e71a83";

    [Fact]
    public async Task FirstDraftWriteThenDirty_DiscardsDocumentsCatalogSkillAndBaseline()
    {
        await using var context = CreateInMemoryContext();
        var state = await SeedAsync(context);
        var dirty = false;
        var draft = new IncrementalWikiDraft(
            context,
            OldHead,
            1024 * 1024,
            _ => dirty
                ? Task.FromException(new LocalGitWorktreeDirtyException(DirtyPreflight()))
                : Task.CompletedTask);
        var docTool = new DocTool(context, state.Language.Id, "page", draft: draft);
        var catalogStorage = new CatalogStorage(context, state.Language.Id, draft: draft);

        Assert.StartsWith("SUCCESS", await docTool.WriteAsync("draft-first-write"));
        await catalogStorage.UpdateNodeAsync(
            "page",
            "{\"title\":\"Draft title\",\"path\":\"page\",\"order\":2,\"children\":[]}");
        await new RepositorySkillMarkdownBuilder().StageSkillMarkdownAsync(
            draft,
            state.Repository,
            state.Branch,
            state.Language);
        Assert.Equal("draft-first-write", await docTool.ReadAsync());

        dirty = true;
        await Assert.ThrowsAsync<LocalGitWorktreeDirtyException>(
            () => docTool.AppendAsync("\nsecond write"));
        await Assert.ThrowsAsync<LocalGitWorktreeDirtyException>(
            () => catalogStorage.UpdateNodeAsync(
                "page",
                "{\"title\":\"Draft title\",\"path\":\"page\",\"order\":1,\"children\":[]}"));

        context.ChangeTracker.Clear();
        Assert.Equal("live-doc", (await context.DocFiles.SingleAsync()).Content);
        Assert.Equal("Live title", (await context.DocCatalogs.SingleAsync()).Title);
        Assert.Equal("live-skill", (await context.BranchLanguages.SingleAsync()).SkillMarkdown);
        Assert.Equal(OldHead, (await context.RepositoryBranches.SingleAsync()).LastCommitId);
    }

    [Theory]
    [InlineData("staged")]
    [InlineData("modified")]
    [InlineData("untracked")]
    [InlineData("head")]
    public async Task PublishGateRejectsDirtyOrChangedHeadWithoutLiveWrites(string sourceChange)
    {
        await using var context = CreateInMemoryContext();
        var state = await SeedAsync(context);
        var draft = await CreateDraftWithDocumentAsync(context, state);
        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(item => item.GetLocalGitPreflightAsync(
                It.IsAny<Repository>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceChange switch
            {
                "staged" => new LocalGitPreflightResult(true, OldHead, ["dirty.txt"], [], []),
                "modified" => new LocalGitPreflightResult(true, OldHead, [], ["dirty.txt"], []),
                "untracked" => DirtyPreflight(),
                _ => new LocalGitPreflightResult(true, NewHead, [], [], [])
            });
        var publisher = CreatePublisher(context, analyzer.Object);

        if (sourceChange != "head")
        {
            await Assert.ThrowsAsync<LocalGitWorktreeDirtyException>(() => publisher.PublishAsync(
                draft, state.Repository.Id, state.Branch.Id, OldHead, NewHead, state.Lease));
        }
        else
        {
            await Assert.ThrowsAsync<LocalGitSourceVersionChangedException>(() => publisher.PublishAsync(
                draft, state.Repository.Id, state.Branch.Id, OldHead, NewHead, state.Lease));
        }

        context.ChangeTracker.Clear();
        await AssertLiveStateAsync(context, "live-doc", "Live title", "live-skill", OldHead, IncrementalUpdateStatus.Processing);
    }

    [Fact]
    public async Task CleanPublish_CommitsDraftSkillBaselineAndCompletedTogether()
    {
        SqliteTestSupport.EnsureInitialized();
        var databasePath = Path.Combine(Path.GetTempPath(), $"opendeepwiki-draft-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=5")
            .Options;

        await using (var context = new TestDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            var state = await SeedAsync(context);
            var draft = await CreateDraftWithDocumentAsync(context, state);
            await draft.UpdateCatalogNodeAsync(
                state.Language.Id,
                "page",
                new CatalogItem { Title = "Published title", Path = "page", Order = 7 });
            draft.StageSkillMarkdown(state.Language.Id, "published-skill", DateTime.UtcNow);
            var analyzer = CleanAnalyzer(state.Repository, OldHead);

            await CreatePublisher(context, analyzer.Object).PublishAsync(
                draft,
                state.Repository.Id,
                state.Branch.Id,
                OldHead,
                NewHead,
                state.Lease);
        }

        await using (var verification = new TestDbContext(options))
        {
            await AssertLiveStateAsync(
                verification,
                "draft-doc",
                "Published title",
                "published-skill",
                NewHead,
                IncrementalUpdateStatus.Completed);
        }

        File.Delete(databasePath);
    }

    [Fact]
    public async Task PublishException_RollsBackDraftBaselineAndTask()
    {
        SqliteTestSupport.EnsureInitialized();
        var databasePath = Path.Combine(Path.GetTempPath(), $"opendeepwiki-draft-rollback-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=5")
            .Options;

        await using (var context = new TestDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            var state = await SeedAsync(context);
            var throwingDraft = new ApplyProbeDraft(OldHead, async (publishContext, cancellationToken) =>
            {
                var document = await publishContext.DocFiles.SingleAsync(cancellationToken);
                document.Content = "must-roll-back";
                throw new InvalidOperationException("injected publish failure");
            });
            var analyzer = CleanAnalyzer(state.Repository, OldHead);

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreatePublisher(context, analyzer.Object)
                .PublishAsync(
                    throwingDraft,
                    state.Repository.Id,
                    state.Branch.Id,
                    OldHead,
                    NewHead,
                    state.Lease));
        }

        await using (var verification = new TestDbContext(options))
        {
            await AssertLiveStateAsync(
                verification,
                "live-doc",
                "Live title",
                "live-skill",
                OldHead,
                IncrementalUpdateStatus.Processing);
        }

        File.Delete(databasePath);
    }

    [Theory]
    [InlineData("doc")]
    [InlineData("catalog")]
    [InlineData("skill")]
    public async Task ConcurrentLiveEdit_RejectsPublishAndRollsBackEveryDraftChange(string conflictTarget)
    {
        SqliteTestSupport.EnsureInitialized();
        var databasePath = Path.Combine(Path.GetTempPath(), $"opendeepwiki-draft-conflict-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=5")
            .Options;
        string repositoryId;
        string branchId;
        GenerationLeaseHandle lease;

        await using (var context = new TestDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            var state = await SeedAsync(context);
            repositoryId = state.Repository.Id;
            branchId = state.Branch.Id;
            lease = state.Lease;
            var draft = await CreateDraftWithDocumentAsync(context, state);
            await draft.UpdateCatalogNodeAsync(
                state.Language.Id,
                "page",
                new CatalogItem { Title = "draft-title", Path = "page", Order = 2 });
            draft.StageSkillMarkdown(state.Language.Id, "draft-skill", DateTime.UtcNow);

            await using (var concurrent = new TestDbContext(options))
            {
                var concurrentVersion = new byte[] { 9, 8, 7, 6 };
                switch (conflictTarget)
                {
                    case "doc":
                        await concurrent.Database.ExecuteSqlInterpolatedAsync($"""
                            UPDATE "DocFiles" SET "Content" = {"manual-doc"}, "Version" = {concurrentVersion}
                            WHERE "BranchLanguageId" = {state.Language.Id}
                            """);
                        break;
                    case "catalog":
                        await concurrent.Database.ExecuteSqlInterpolatedAsync($"""
                            UPDATE "DocCatalogs" SET "Title" = {"manual-title"}, "Version" = {concurrentVersion}
                            WHERE "BranchLanguageId" = {state.Language.Id}
                            """);
                        break;
                    case "skill":
                        await concurrent.Database.ExecuteSqlInterpolatedAsync($"""
                            UPDATE "BranchLanguages" SET "SkillMarkdown" = {"manual-skill"}, "Version" = {concurrentVersion}
                            WHERE "Id" = {state.Language.Id}
                            """);
                        break;
                }
            }

            var analyzer = CleanAnalyzer(state.Repository, OldHead);
            await Assert.ThrowsAsync<IncrementalWikiPublishConflictException>(() =>
                CreatePublisher(context, analyzer.Object).PublishAsync(
                    draft,
                    repositoryId,
                    branchId,
                    OldHead,
                    NewHead,
                    lease));
        }

        await using (var verification = new TestDbContext(options))
        {
            await AssertLiveStateAsync(
                verification,
                conflictTarget == "doc" ? "manual-doc" : "live-doc",
                conflictTarget == "catalog" ? "manual-title" : "Live title",
                conflictTarget == "skill" ? "manual-skill" : "live-skill",
                OldHead,
                IncrementalUpdateStatus.Processing);
        }

        File.Delete(databasePath);
    }

    [Fact]
    public async Task OldTokenAndBaselineCasBothRejectBeforeApplyingDraft()
    {
        await using var context = CreateInMemoryContext();
        var state = await SeedAsync(context);
        var analyzer = CleanAnalyzer(state.Repository, OldHead);
        var oldTokenDraft = new ApplyProbeDraft(OldHead, (_, _) => Task.CompletedTask);
        var oldLease = state.Lease with { LockId = Guid.NewGuid().ToString() };

        await Assert.ThrowsAsync<GenerationLeaseLostException>(() => CreatePublisher(context, analyzer.Object)
            .PublishAsync(
                oldTokenDraft,
                state.Repository.Id,
                state.Branch.Id,
                OldHead,
                NewHead,
                oldLease));
        Assert.False(oldTokenDraft.ApplyWasCalled);

        context.ChangeTracker.Clear();
        var baselineDraft = new ApplyProbeDraft(OldHead, (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<IncrementalBaselineConflictException>(() => CreatePublisher(context, analyzer.Object)
            .PublishAsync(
                baselineDraft,
                state.Repository.Id,
                state.Branch.Id,
                "unexpected-baseline",
                NewHead,
                state.Lease));
        Assert.False(baselineDraft.ApplyWasCalled);

        context.ChangeTracker.Clear();
        await AssertLiveStateAsync(context, "live-doc", "Live title", "live-skill", OldHead, IncrementalUpdateStatus.Processing);
    }

    [Fact]
    public async Task DraftSizeLimitRejectsOverlayWithoutLiveWrites()
    {
        await using var context = CreateInMemoryContext();
        var state = await SeedAsync(context);
        var largeDocument = new DocFile
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = state.Language.Id,
            Content = new string('界', 800)
        };
        context.DocFiles.Add(largeDocument);
        context.DocCatalogs.Add(new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = state.Language.Id,
            Title = "Large",
            Path = "large",
            Order = 2,
            DocFileId = largeDocument.Id
        });
        await context.SaveChangesAsync();

        var freshDraft = new IncrementalWikiDraft(context, OldHead, 2048, _ => Task.CompletedTask);
        await freshDraft.GetCatalogsAsync(state.Language.Id, includeDocuments: false);
        var freshDraftSize = freshDraft.SizeBytes;
        var freshDocTool = new DocTool(context, state.Language.Id, "page", draft: freshDraft);
        await Assert.ThrowsAsync<IncrementalWikiDraftLimitExceededException>(
            () => freshDocTool.WriteAsync(new string('x', 4096)));
        Assert.Equal(freshDraftSize, freshDraft.SizeBytes);
        await Assert.ThrowsAsync<IncrementalWikiDraftLimitExceededException>(
            () => freshDocTool.AppendAsync(new string('y', 4096)));
        Assert.Equal(freshDraftSize, freshDraft.SizeBytes);

        var draft = new IncrementalWikiDraft(context, OldHead, 2048, _ => Task.CompletedTask);
        var docTool = new DocTool(context, state.Language.Id, "page", draft: draft);
        Assert.Equal("live-doc", await docTool.ReadAsync());
        var sizeBeforeRejectedMutation = draft.SizeBytes;

        await Assert.ThrowsAsync<IncrementalWikiDraftLimitExceededException>(
            () => docTool.WriteAsync(new string('x', 4096)));
        Assert.Equal(sizeBeforeRejectedMutation, draft.SizeBytes);
        Assert.Equal("live-doc", await docTool.ReadAsync());

        await Assert.ThrowsAsync<IncrementalWikiDraftLimitExceededException>(
            () => docTool.AppendAsync(new string('y', 4096)));
        Assert.Equal(sizeBeforeRejectedMutation, draft.SizeBytes);
        Assert.Equal("live-doc", await docTool.ReadAsync());

        await Assert.ThrowsAsync<IncrementalWikiDraftLimitExceededException>(() => Task.Run(() =>
            draft.StageSkillMarkdown(state.Language.Id, new string('s', 4096), DateTime.UtcNow)));
        Assert.Equal(sizeBeforeRejectedMutation, draft.SizeBytes);

        await Assert.ThrowsAsync<IncrementalWikiDraftLimitExceededException>(
            () => draft.ReadDocumentAsync(state.Language.Id, "large"));
        Assert.Equal(sizeBeforeRejectedMutation, draft.SizeBytes);

        context.ChangeTracker.Clear();
        Assert.Contains(await context.DocFiles.ToListAsync(), item => item.Content == "live-doc");
        Assert.Contains(await context.DocFiles.ToListAsync(), item => item.Content == new string('界', 800));
        Assert.Equal(OldHead, (await context.RepositoryBranches.SingleAsync()).LastCommitId);
    }

    private static async Task<IncrementalWikiDraft> CreateDraftWithDocumentAsync(
        TestDbContext context,
        SeedState state)
    {
        var draft = new IncrementalWikiDraft(context, OldHead, 1024 * 1024, _ => Task.CompletedTask);
        var result = await draft.WriteDocumentAsync(
            state.Language.Id,
            "page",
            "draft-doc",
            null);
        Assert.Equal(DraftDocumentMutationStatus.Updated, result.Status);
        return draft;
    }

    private static IncrementalWikiPublisher CreatePublisher(TestDbContext context, IRepositoryAnalyzer analyzer)
    {
        var options = Options.Create(new IncrementalUpdateOptions());
        return new IncrementalWikiPublisher(context, analyzer, new GenerationWriteGuard(options));
    }

    private static Mock<IRepositoryAnalyzer> CleanAnalyzer(Repository repository, string head)
    {
        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(item => item.GetLocalGitPreflightAsync(
                It.Is<Repository>(value => value.Id == repository.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocalGitPreflightResult(true, head, [], [], []));
        return analyzer;
    }

    private static LocalGitPreflightResult DirtyPreflight() =>
        new(true, OldHead, [], [], ["dirty.txt"]);

    private static async Task<SeedState> SeedAsync(TestDbContext context)
    {
        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = "user",
            GitUrl = RepositorySource.EncodeLocalDirectoryPath("/tmp/local-git"),
            OrgName = "demo",
            RepoName = "repo",
            Status = RepositoryStatus.Completed,
            GenerateSkill = true
        };
        var branch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchName = "main",
            LastCommitId = OldHead
        };
        var language = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true,
            SkillMarkdown = "live-skill"
        };
        var document = new DocFile
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = language.Id,
            Content = "live-doc"
        };
        var catalog = new DocCatalog
        {
            Id = Guid.NewGuid().ToString(),
            BranchLanguageId = language.Id,
            Title = "Live title",
            Path = "page",
            Order = 1,
            DocFileId = document.Id
        };
        var task = new IncrementalUpdateTask
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchId = branch.Id,
            PreviousCommitId = OldHead,
            Status = IncrementalUpdateStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow
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
        context.AddRange(repository, branch, language, document, catalog, task, generationLock);
        await context.SaveChangesAsync();
        return new SeedState(
            repository,
            branch,
            language,
            new GenerationLeaseHandle(generationLock.Id, repository.Id, generationLock.OwnerType, task.Id));
    }

    private static async Task AssertLiveStateAsync(
        TestDbContext context,
        string documentContent,
        string catalogTitle,
        string skillMarkdown,
        string baseline,
        IncrementalUpdateStatus taskStatus)
    {
        Assert.Equal(documentContent, (await context.DocFiles.SingleAsync()).Content);
        Assert.Equal(catalogTitle, (await context.DocCatalogs.SingleAsync()).Title);
        Assert.Equal(skillMarkdown, (await context.BranchLanguages.SingleAsync()).SkillMarkdown);
        Assert.Equal(baseline, (await context.RepositoryBranches.SingleAsync()).LastCommitId);
        Assert.Equal(taskStatus, (await context.IncrementalUpdateTasks.SingleAsync()).Status);
    }

    private static TestDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TestDbContext(options);
    }

    private sealed record SeedState(
        Repository Repository,
        RepositoryBranch Branch,
        BranchLanguage Language,
        GenerationLeaseHandle Lease);

    private sealed class ApplyProbeDraft(
        string sourceHeadCommitId,
        Func<IContext, CancellationToken, Task> apply) : IIncrementalWikiDraft
    {
        public string SourceHeadCommitId { get; } = sourceHeadCommitId;
        public long SizeBytes => 0;
        public bool ApplyWasCalled { get; private set; }

        public Task ApplyAsync(IContext publishContext, CancellationToken cancellationToken = default)
        {
            ApplyWasCalled = true;
            return apply(publishContext, cancellationToken);
        }

        public Task<IReadOnlyList<DocCatalog>> GetCatalogsAsync(
            string branchLanguageId,
            bool includeDocuments,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task UpdateCatalogNodeAsync(
            string branchLanguageId,
            string path,
            CatalogItem updatedItem,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DraftDocumentMutationResult> WriteDocumentAsync(
            string branchLanguageId,
            string path,
            string content,
            string? sourceFiles,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DraftDocumentMutationResult> AppendDocumentAsync(
            string branchLanguageId,
            string path,
            string content,
            string? sourceFiles,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DraftDocumentMutationResult> EditDocumentAsync(
            string branchLanguageId,
            string path,
            string oldContent,
            string newContent,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string?> ReadDocumentAsync(
            string branchLanguageId,
            string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DocumentExistsAsync(
            string branchLanguageId,
            string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void StageSkillMarkdown(string branchLanguageId, string markdown, DateTime generatedAtUtc) =>
            throw new NotSupportedException();
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : MasterDbContext(options);
}
