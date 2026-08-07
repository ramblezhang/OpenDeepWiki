using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Translation;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Translation;

public class TranslationWorkerTests
{
    [Fact]
    public async Task ScanAndCreateTranslationTasksAsync_WithChineseOnlyConfiguration_DoesNotCreateOtherLanguages()
    {
        await using var context = OpenDeepWiki.Tests.Chat.Sessions.TestDbContext.Create();
        var repository = new Repository
        {
            Id = Guid.NewGuid().ToString(),
            OwnerUserId = "user",
            GitUrl = "https://example.com/demo/repo.git",
            OrgName = "demo",
            RepoName = "repo",
            Status = RepositoryStatus.Completed
        };
        var branch = new RepositoryBranch
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repository.Id,
            BranchName = "main"
        };
        var sourceLanguage = new BranchLanguage
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryBranchId = branch.Id,
            LanguageCode = "zh",
            IsDefault = true
        };
        context.AddRange(repository, branch, sourceLanguage);
        await context.SaveChangesAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WIKI_LANGUAGES"] = "zh"
            })
            .Build();
        var options = new WikiGeneratorOptions();
        WikiGeneratorOptionsConfigurator.Apply(options, configuration);

        var worker = new TranslationWorker(
            Mock.Of<IServiceScopeFactory>(),
            NullLogger<TranslationWorker>.Instance);
        var method = typeof(TranslationWorker).GetMethod(
            "ScanAndCreateTranslationTasksAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var invocation = (Task?)method!.Invoke(
            worker,
            [context, options, CancellationToken.None]);
        Assert.NotNull(invocation);
        await invocation!;

        Assert.Empty(await context.TranslationTasks.ToListAsync());
    }
}
