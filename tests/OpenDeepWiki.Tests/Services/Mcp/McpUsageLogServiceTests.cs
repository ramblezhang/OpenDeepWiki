using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Mcp;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Mcp;

public class McpUsageLogServiceTests
{
    [Fact]
    public async Task LogUsageAsync_AttributesCallsToTheBuiltInGlobalProvider()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var context = CreateContext(databaseName, databaseRoot))
        {
            context.McpProviders.AddRange(
                new McpProvider
                {
                    Id = "unrelated-provider",
                    Name = "Unrelated provider",
                    ServerUrl = "/elsewhere",
                    SortOrder = -100,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new McpProvider
                {
                    Id = "global-provider",
                    Name = "OpenDeepWiki Global MCP",
                    ServerUrl = "/api/mcp",
                    SortOrder = 100,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            await context.SaveChangesAsync();
        }

        var service = new McpUsageLogService(
            new TestContextFactory(databaseName, databaseRoot),
            NullLogger<McpUsageLogService>.Instance);
        var log = CreateUsage(DateTime.UtcNow, "declared_username", "list_repositories");
        log.Id = string.Empty;
        log.McpProviderId = null;
        await service.LogUsageAsync(log);

        await using var verificationContext = CreateContext(databaseName, databaseRoot);
        var usage = await verificationContext.McpUsageLogs.SingleAsync();
        Assert.Equal("global-provider", usage.McpProviderId);
    }

    [Fact]
    public async Task LogUsageAsync_AttributesCallsToTheActiveGlobalEndpointAfterProviderRename()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var context = CreateContext(databaseName, databaseRoot))
        {
            context.McpProviders.AddRange(
                new McpProvider
                {
                    Id = "deleted-built-in-provider",
                    Name = "OpenDeepWiki Global MCP",
                    ServerUrl = "/api/mcp/{owner}/{repo}",
                    IsActive = false,
                    IsDeleted = true,
                    CreatedAt = DateTime.UtcNow
                },
                new McpProvider
                {
                    Id = "deployed-global-provider",
                    Name = "YoudaoHW_Code_WIKI",
                    ServerUrl = "/api/mcp",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                },
                new McpProvider
                {
                    Id = "unrelated-provider",
                    Name = "Another active MCP",
                    ServerUrl = "/another-mcp",
                    SortOrder = -100,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            await context.SaveChangesAsync();
        }

        var service = new McpUsageLogService(
            new TestContextFactory(databaseName, databaseRoot),
            NullLogger<McpUsageLogService>.Instance);
        var log = CreateUsage(DateTime.UtcNow, "declared_username", "list_repositories");
        log.Id = string.Empty;
        log.McpProviderId = null;

        await service.LogUsageAsync(log);

        await using var verificationContext = CreateContext(databaseName, databaseRoot);
        var usage = await verificationContext.McpUsageLogs.SingleAsync();
        Assert.Equal("deployed-global-provider", usage.McpProviderId);
    }

    [Fact]
    public async Task LogUsageAsync_FallsBackWithoutThrowingAndReplaysLater()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"mcp-usage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var fallbackPath = Path.Combine(temporaryDirectory, "fallback.jsonl");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MCP_USAGE_FALLBACK_FILE"] = fallbackPath
                })
                .Build();
            var failingService = new McpUsageLogService(
                new FailingContextFactory(),
                NullLogger<McpUsageLogService>.Instance,
                configuration);

            var log = CreateUsage(DateTime.UtcNow, "declared_username", "search_docs");
            log.Id = string.Empty;
            log.McpProviderId = null;
            await failingService.LogUsageAsync(log);

            Assert.True(File.Exists(fallbackPath));
            Assert.False(failingService.GetLoggingHealth().Healthy);

            var databaseName = Guid.NewGuid().ToString();
            var databaseRoot = new InMemoryDatabaseRoot();
            await using (var context = CreateContext(databaseName, databaseRoot))
            {
                context.McpProviders.Add(new McpProvider
                {
                    Id = "global-provider",
                    Name = "OpenDeepWiki Global MCP",
                    ServerUrl = "/api/mcp",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }
            var recoveredService = new McpUsageLogService(
                new TestContextFactory(databaseName, databaseRoot),
                NullLogger<McpUsageLogService>.Instance,
                configuration);

            await recoveredService.ReplayFallbackLogsAsync();

            await using var verificationContext = CreateContext(databaseName, databaseRoot);
            var replayed = await verificationContext.McpUsageLogs.SingleAsync();
            Assert.Equal("search_docs", replayed.ToolName);
            Assert.Equal("global-provider", replayed.McpProviderId);
            Assert.True(recoveredService.GetLoggingHealth().Healthy);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RebuildDailyStatisticsAsync_ExcludesLegacyHttpRowsAndClearsPollutedCounts()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        var date = DateTime.UtcNow.Date.AddDays(-3);
        await using (var context = CreateContext(databaseName, databaseRoot))
        {
            context.McpUsageLogs.AddRange(
                CreateUsage(date.AddHours(1), identityType: null, toolName: "POST /api/mcp"),
                CreateUsage(date.AddHours(2), identityType: "declared_username", toolName: "list_repositories"));
            context.McpDailyStatistics.Add(new McpDailyStatistics
            {
                Id = Guid.NewGuid().ToString(),
                McpProviderId = "provider",
                Date = date,
                RequestCount = 500,
                SuccessCount = 400,
                ErrorCount = 100,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var service = new McpUsageLogService(
            new TestContextFactory(databaseName, databaseRoot),
            NullLogger<McpUsageLogService>.Instance);

        await service.RebuildDailyStatisticsAsync();

        await using var verificationContext = CreateContext(databaseName, databaseRoot);
        var statistic = await verificationContext.McpDailyStatistics.SingleAsync(item => item.Date == date);
        Assert.Equal(1, statistic.RequestCount);
        Assert.Equal(1, statistic.SuccessCount);
        Assert.Equal(0, statistic.ErrorCount);
    }

    private static McpUsageLog CreateUsage(DateTime createdAt, string? identityType, string toolName) => new()
    {
        Id = Guid.NewGuid().ToString(),
        UserId = "anonymous",
        IdentityType = identityType,
        Outcome = identityType == null ? null : "authorized_success",
        McpProviderId = "provider",
        ToolName = toolName,
        ResponseStatus = 200,
        DurationMs = 1,
        CreatedAt = createdAt
    };

    private static TestDbContext CreateContext(string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;
        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class TestContextFactory(string databaseName, InMemoryDatabaseRoot databaseRoot)
        : IContextFactory
    {
        public IContext CreateContext() => McpUsageLogServiceTests.CreateContext(databaseName, databaseRoot);
    }

    private sealed class FailingContextFactory : IContextFactory
    {
        public IContext CreateContext() => throw new InvalidOperationException("simulated database failure");
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options)
        : MasterDbContext(options);
}
