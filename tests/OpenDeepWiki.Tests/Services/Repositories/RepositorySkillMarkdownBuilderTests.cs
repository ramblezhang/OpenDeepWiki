using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Repositories;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Repositories;

public class RepositorySkillMarkdownBuilderTests
{
    [Fact]
    public void BuildSkillMarkdown_ShouldCreateEnglishDescriptorAndLeafDocumentIndex()
    {
        var builder = new RepositorySkillMarkdownBuilder();
        var repository = new Repository
        {
            OrgName = "AIDotNet",
            RepoName = "OpenDeepWiki"
        };
        var branch = new RepositoryBranch
        {
            BranchName = "main"
        };
        var language = new BranchLanguage
        {
            Id = "lang-1",
            LanguageCode = "zh"
        };
        var catalogs = new List<DocCatalog>
        {
            new()
            {
                Id = "overview",
                BranchLanguageId = language.Id,
                Title = "Overview",
                Path = "overview",
                Order = 1,
                DocFileId = "doc-overview"
            },
            new()
            {
                Id = "api",
                BranchLanguageId = language.Id,
                ParentId = "overview",
                Title = "API Guide",
                Path = "overview/api",
                Order = 2,
                DocFileId = "doc-api"
            }
        };

        var markdown = builder.BuildSkillMarkdown(
            repository,
            branch,
            language,
            catalogs,
            new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc));

        Assert.Contains("name: aidotnet-opendeepwiki", markdown);
        Assert.Contains("description: \"Generated repository documentation skill for AIDotNet/OpenDeepWiki.\"", markdown);
        Assert.Contains("Documentation language: `zh`", markdown);
        Assert.Contains("Use this skill when answering questions", markdown);
        Assert.DoesNotContain("- [Overview](references/docs/Overview.md)", markdown);
        Assert.Contains("  - [API Guide](references/docs/Overview/API%20Guide.md)", markdown);
    }

    [Fact]
    public void BuildSkillMarkdown_ShouldUseStableSkillNameWithinLimit()
    {
        var builder = new RepositorySkillMarkdownBuilder();
        var repository = new Repository
        {
            OrgName = "Very_Long.Owner_Name_With Symbols",
            RepoName = "Another_Extremely_Long_Repository_Name_That_Should_Be_Truncated_For_Skill_Metadata"
        };
        var branch = new RepositoryBranch
        {
            BranchName = "feature/skill-export"
        };
        var language = new BranchLanguage
        {
            LanguageCode = "en"
        };

        var first = builder.BuildSkillMarkdown(repository, branch, language, [], DateTime.UnixEpoch);
        var second = builder.BuildSkillMarkdown(repository, branch, language, [], DateTime.UnixEpoch);
        var firstName = GetFrontmatterValue(first, "name");
        var secondName = GetFrontmatterValue(second, "name");

        Assert.Equal(firstName, secondName);
        Assert.True(firstName.Length <= 64);
        Assert.Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$", firstName);
        Assert.Contains("No generated documentation files were available", first);
    }

    private static string GetFrontmatterValue(string markdown, string key)
    {
        var prefix = $"{key}: ";
        var line = markdown
            .Split('\n')
            .First(line => line.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..].Trim();
    }
}
