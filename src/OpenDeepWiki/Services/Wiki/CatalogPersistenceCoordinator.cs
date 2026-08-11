using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace OpenDeepWiki.Services.Wiki;

/// <summary>
/// Enforces the catalog persistence invariant independently from transport retries.
/// </summary>
internal static class CatalogPersistenceCoordinator
{
    internal static async Task<int> ExecuteAsync(
        int maxAttempts,
        Func<int, bool, ChatToolMode, CancellationToken, Task> executeAttempt,
        Func<CancellationToken, Task<int>> countPersistedItems,
        Func<int, CancellationToken, Task> delayBeforeNextAttempt,
        string target,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executeAttempt);
        ArgumentNullException.ThrowIfNull(countPersistedItems);
        ArgumentNullException.ThrowIfNull(delayBeforeNextAttempt);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(logger);

        var attempts = Math.Max(1, maxAttempts);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isRepair = attempt > 1;
            ChatToolMode toolMode = isRepair
                ? new RequiredChatToolMode("WriteCatalog")
                : ChatToolMode.Auto;

            await executeAttempt(attempt, isRepair, toolMode, cancellationToken);

            var persistedItemCount = await countPersistedItems(cancellationToken);
            if (persistedItemCount > 0)
            {
                if (isRepair)
                {
                    logger.LogInformation(
                        "Catalog persistence recovered. Target: {Target}, Attempt: {Attempt}/{MaxAttempts}, CatalogItems: {CatalogItems}",
                        target,
                        attempt,
                        attempts,
                        persistedItemCount);
                }

                return persistedItemCount;
            }

            logger.LogWarning(
                "Catalog agent completed without persisted items. Target: {Target}, Attempt: {Attempt}/{MaxAttempts}, NextAttemptRequiresWriteCatalog: {RequiresWriteCatalog}",
                target,
                attempt,
                attempts,
                attempt < attempts);

            if (attempt < attempts)
            {
                await delayBeforeNextAttempt(attempt, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Catalog generation failed to persist any catalog items for {target} after {attempts} attempts.");
    }
}
