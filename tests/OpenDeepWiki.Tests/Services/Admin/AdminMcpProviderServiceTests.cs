using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Admin;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Admin;

public class AdminMcpProviderServiceTests
{
    [Fact]
    public async Task GetProvidersAsync_ShouldReturnGlobalMcpServerUrl()
    {
        await using var context = CreateContext();
        context.McpProviders.Add(new McpProvider
        {
            Id = Guid.NewGuid().ToString(),
            Name = "OpenDeepWiki Global MCP",
            Description = "Global MCP",
            ServerUrl = "/api/mcp/{owner}/{repo}",
            TransportType = "streamable_http",
            RequiresApiKey = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = new AdminMcpProviderService(context, NullLogger<AdminMcpProviderService>.Instance);

        var providers = await service.GetProvidersAsync();

        var provider = Assert.Single(providers);
        Assert.Equal("/api/mcp", provider.ServerUrl);
    }

    [Fact]
    public async Task GetMcpUsageStatisticsAsync_GroupsAliasesByCanonicalUserAndCountsDenials()
    {
        await using var context = CreateContext();
        context.McpUsageLogs.AddRange(
            CreateUsage(@"YOUDAO\zhangsan", "zhangsan", "authorized_success", 200),
            CreateUsage("zhangsan", "zhangsan", "authorized_success", 200),
            CreateUsage("lisi", null, "denied_not_registered", 403),
            CreateLegacyHttpUsage());
        context.McpDailyStatistics.Add(new McpDailyStatistics
        {
            Id = Guid.NewGuid().ToString(),
            McpProviderId = "provider",
            Date = DateTime.UtcNow.Date,
            RequestCount = 10_000,
            SuccessCount = 9_000,
            ErrorCount = 1_000,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var service = new AdminMcpProviderService(context, NullLogger<AdminMcpProviderService>.Instance);

        var statistics = await service.GetMcpUsageStatisticsAsync(30);

        var zhangsan = Assert.Single(statistics.UserUsages, usage => usage.User == "zhangsan");
        Assert.Equal(2, zhangsan.RequestCount);
        Assert.Equal(2, zhangsan.SuccessCount);
        Assert.Equal(0, zhangsan.DeniedCount);

        var lisi = Assert.Single(statistics.UserUsages, usage => usage.User == "lisi");
        Assert.Equal(1, lisi.RequestCount);
        Assert.Equal(1, lisi.DeniedCount);

        Assert.Equal(3, statistics.TotalRequests);
        Assert.Equal(2, statistics.TotalSuccessful);
        Assert.Equal(1, statistics.TotalErrors);
        Assert.DoesNotContain(statistics.UserUsages, usage => usage.User == "legacy_anonymous");
    }

    private static McpUsageLog CreateUsage(
        string? presentedUser,
        string? canonicalUser,
        string outcome,
        int responseStatus) => new()
    {
        Id = Guid.NewGuid().ToString(),
        UserId = "anonymous",
        PresentedUser = presentedUser,
        CanonicalUser = canonicalUser,
        IdentityType = "declared_username",
        Outcome = outcome,
        ErrorCode = responseStatus >= 400 ? "USER_NOT_ALLOWED" : null,
        McpProviderId = "provider",
        ToolName = "list_repositories",
        ResponseStatus = responseStatus,
        DurationMs = 1,
        CreatedAt = DateTime.UtcNow
    };

    private static McpUsageLog CreateLegacyHttpUsage() => new()
    {
        Id = Guid.NewGuid().ToString(),
        UserId = "anonymous",
        McpProviderId = "provider",
        ToolName = "POST /api/mcp",
        ResponseStatus = 200,
        DurationMs = 1,
        CreatedAt = DateTime.UtcNow
    };

    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options)
        : MasterDbContext(options);
}
