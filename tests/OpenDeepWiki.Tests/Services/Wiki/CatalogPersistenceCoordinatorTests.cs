using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDeepWiki.Services.Wiki;
using Xunit;

namespace OpenDeepWiki.Tests.Services.Wiki;

public sealed class CatalogPersistenceCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldRequireWriteCatalogAfterInitialAttemptDoesNotPersist()
    {
        var attempts = new List<(int Number, bool IsRepair, ChatToolMode Mode)>();
        var persistedCount = 0;

        var result = await CatalogPersistenceCoordinator.ExecuteAsync(
            3,
            (attempt, isRepair, mode, _) =>
            {
                attempts.Add((attempt, isRepair, mode));
                if (attempt == 2)
                {
                    persistedCount = 12;
                }

                return Task.CompletedTask;
            },
            _ => Task.FromResult(persistedCount),
            (_, _) => Task.CompletedTask,
            "org/repo (zh)",
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(12, result);
        Assert.Equal(2, attempts.Count);
        Assert.False(attempts[0].IsRepair);
        Assert.Same(ChatToolMode.Auto, attempts[0].Mode);
        var requiredMode = Assert.IsType<RequiredChatToolMode>(attempts[1].Mode);
        Assert.Equal("WriteCatalog", requiredMode.RequiredFunctionName);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStopAfterInitialAttemptPersists()
    {
        var executionCount = 0;

        var result = await CatalogPersistenceCoordinator.ExecuteAsync(
            3,
            (_, _, _, _) =>
            {
                executionCount++;
                return Task.CompletedTask;
            },
            _ => Task.FromResult(8),
            (_, _) => Task.CompletedTask,
            "org/repo (zh)",
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(8, result);
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailAfterAllAttemptsCompleteWithoutPersistence()
    {
        var executionCount = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CatalogPersistenceCoordinator.ExecuteAsync(
                3,
                (_, _, _, _) =>
                {
                    executionCount++;
                    return Task.CompletedTask;
                },
                _ => Task.FromResult(0),
                (_, _) => Task.CompletedTask,
                "org/repo (zh)",
                NullLogger.Instance,
                CancellationToken.None));

        Assert.Equal(3, executionCount);
        Assert.Contains("after 3 attempts", exception.Message);
    }
}
