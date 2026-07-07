using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class RepositoryBranchProcessorTests
{
    [Fact]
    public async Task ProcessBranchAsync_WhenBranchHasNoLanguages_FailsBeforeMarkingBranchProcessed()
    {
        using var context = CreateContext();
        var repository = new Repository
        {
            Id = "repo-1",
            OwnerUserId = "user-1",
            OrgName = "example",
            RepoName = "service",
            GitUrl = "https://example.invalid/org/service.git",
            PrimaryLanguage = "C#",
            Status = RepositoryStatus.Processing
        };
        var branch = new RepositoryBranch
        {
            Id = "branch-1",
            RepositoryId = repository.Id,
            BranchName = "feature/docs"
        };
        context.Repositories.Add(repository);
        context.RepositoryBranches.Add(branch);
        await context.SaveChangesAsync();

        var workspace = new RepositoryWorkspace
        {
            Organization = repository.OrgName,
            RepositoryName = repository.RepoName,
            BranchName = branch.BranchName,
            WorkingDirectory = "/tmp/service",
            CommitId = "0123456789abcdef0123456789abcdef01234567"
        };
        var analyzer = new Mock<IRepositoryAnalyzer>(MockBehavior.Strict);
        analyzer
            .Setup(item => item.PrepareWorkspaceAsync(repository, branch.BranchName, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(workspace);
        analyzer
            .Setup(item => item.CleanupWorkspaceAsync(workspace, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var wikiGenerator = new Mock<IWikiGenerator>(MockBehavior.Strict);
        var processor = new RepositoryBranchProcessor(
            analyzer.Object,
            wikiGenerator.Object,
            skillMarkdownBuilder: null,
            scanPlanResolver: null,
            processingLogService: null,
            NullLogger<RepositoryBranchProcessor>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessBranchAsync(context, repository, branch, null, false));

        Assert.Contains("has no configured languages", exception.Message);
        var updatedBranch = await context.RepositoryBranches.SingleAsync(item => item.Id == branch.Id);
        Assert.Null(updatedBranch.LastCommitId);
        Assert.Null(updatedBranch.LastProcessedAt);
        wikiGenerator.Verify(
            item => item.GenerateCatalogAsync(
                It.IsAny<RepositoryWorkspace>(),
                It.IsAny<BranchLanguage>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        analyzer.Verify(item => item.CleanupWorkspaceAsync(workspace, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TestDbContext(options);
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : MasterDbContext(options);
}
