using OpenDeepWiki.Agents.Tools;
using Xunit;

namespace OpenDeepWiki.Tests.Agents.Tools;

public sealed class DocumentSourceToolBudgetTests : IDisposable
{
    private readonly string _testDirectory;

    public DocumentSourceToolBudgetTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "document-source-budget-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        File.WriteAllText(Path.Combine(_testDirectory, "Makefile"), "all:\n\t@echo build\n");
        File.WriteAllText(Path.Combine(_testDirectory, "README.md"), "# Test\n");
    }

    [Fact]
    public async Task SourceTools_ShouldReturnStopSignalAfterBudgetIsExhausted()
    {
        var gitTool = new GitTool(_testDirectory);
        var budget = new DocumentSourceToolBudget(gitTool, maxSourceToolCalls: 1);

        var first = await budget.ListFilesAsync("**/*");
        var second = await budget.ReadAsync("Makefile");
        var third = await budget.GrepAsync("all", "**/*");

        Assert.Contains("Makefile", first);
        Assert.Contains("SOURCE_TOOL_BUDGET_REACHED", second);
        Assert.Single(third);
        Assert.Equal("BUDGET", third[0].FilePath);
        Assert.Contains("SOURCE_TOOL_BUDGET_REACHED", third[0].LineContent);
    }

    [Fact]
    public async Task SourceTools_ShouldUseCatalogPersistenceInstruction_WhenConfiguredForCatalog()
    {
        var gitTool = new GitTool(_testDirectory);
        var budget = new DocumentSourceToolBudget(
            gitTool,
            maxSourceToolCalls: 1,
            "call WriteCatalog with the complete catalog JSON, then finish");

        await budget.ListFilesAsync("**/*");
        var exhausted = await budget.ReadAsync("Makefile");

        Assert.Contains("WriteCatalog", exhausted);
        Assert.DoesNotContain("WriteDoc", exhausted);
        Assert.DoesNotContain("AppendDoc", exhausted);
    }

    [Fact]
    public async Task SourceTools_ShouldCollectBoundedEvidenceForPersistenceRepair()
    {
        var gitTool = new GitTool(_testDirectory);
        var budget = new DocumentSourceToolBudget(gitTool, maxSourceToolCalls: 3);

        await budget.ListFilesAsync("**/*");
        await budget.GrepAsync("all", "Makefile");
        await budget.ReadAsync("Makefile");

        var evidence = budget.GetCollectedEvidence();

        Assert.Contains("## ListFiles", evidence);
        Assert.Contains("## Grep", evidence);
        Assert.Contains("## ReadFile: Makefile", evidence);
        Assert.Contains("@echo build", evidence);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
