using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Notifications;
using OpenDeepWiki.Services.Wiki;

namespace OpenDeepWiki.Services.Repositories;

/// <summary>
/// Core incremental update service implementation.
/// </summary>
public class IncrementalUpdateService : IIncrementalUpdateService
{
    private readonly IRepositoryAnalyzer _repositoryAnalyzer;
    private readonly IWikiGenerator _wikiGenerator;
    private readonly IRepositorySkillMarkdownBuilder _skillMarkdownBuilder;
    private readonly ISubscriberNotificationService _notificationService;
    private readonly IContext _context;
    private readonly IncrementalUpdateOptions _options;
    private readonly ILogger<IncrementalUpdateService> _logger;
    private readonly IGenerationWriteGuard? _writeGuard;

    public IncrementalUpdateService(
        IRepositoryAnalyzer repositoryAnalyzer,
        IWikiGenerator wikiGenerator,
        IRepositorySkillMarkdownBuilder skillMarkdownBuilder,
        ISubscriberNotificationService notificationService,
        IContext context,
        IOptions<IncrementalUpdateOptions> options,
        ILogger<IncrementalUpdateService> logger,
        IGenerationWriteGuard? writeGuard = null)
    {
        _repositoryAnalyzer = repositoryAnalyzer;
        _wikiGenerator = wikiGenerator;
        _skillMarkdownBuilder = skillMarkdownBuilder;
        _notificationService = notificationService;
        _context = context;
        _options = options.Value;
        _logger = logger;
        _writeGuard = writeGuard;
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        string repositoryId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Checking for updates. RepositoryId: {RepositoryId}, BranchId: {BranchId}",
            repositoryId, branchId);

        var repository = await _context.Repositories
            .FirstOrDefaultAsync(r => r.Id == repositoryId && !r.IsDeleted, cancellationToken);

        if (repository == null)
        {
            _logger.LogWarning("Repository not found. RepositoryId: {RepositoryId}", repositoryId);
            return new UpdateCheckResult { NeedsUpdate = false };
        }

        var branch = await _context.RepositoryBranches
            .FirstOrDefaultAsync(b => b.Id == branchId && !b.IsDeleted, cancellationToken);

        if (branch == null)
        {
            _logger.LogWarning("Branch not found. BranchId: {BranchId}", branchId);
            return new UpdateCheckResult { NeedsUpdate = false };
        }

        var previousCommitId = branch.LastCommitId;

        try
        {
            var workspace = await PrepareWorkspaceWithRetryAsync(
                repository, branch.BranchName, previousCommitId, cancellationToken);

            var currentCommitId = workspace.CommitId;

            if (previousCommitId == currentCommitId)
            {
                _logger.LogInformation(
                    "No changes detected. RepositoryId: {RepositoryId}, CommitId: {CommitId}",
                    repositoryId, currentCommitId);

                return new UpdateCheckResult
                {
                    NeedsUpdate = false,
                    PreviousCommitId = previousCommitId,
                    CurrentCommitId = currentCommitId,
                    ChangedFiles = Array.Empty<string>()
                };
            }

            var changedFiles = await _repositoryAnalyzer.GetChangedFilesAsync(
                workspace, previousCommitId, currentCommitId, cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Update check completed. RepositoryId: {RepositoryId}, PreviousCommit: {PreviousCommit}, CurrentCommit: {CurrentCommit}, ChangedFiles: {ChangedFilesCount}, Duration: {Duration}ms",
                repositoryId, previousCommitId ?? "none", currentCommitId, changedFiles.Length, stopwatch.ElapsedMilliseconds);

            return new UpdateCheckResult
            {
                NeedsUpdate = changedFiles.Length > 0 || string.IsNullOrEmpty(previousCommitId),
                PreviousCommitId = previousCommitId,
                CurrentCommitId = currentCommitId,
                ChangedFiles = changedFiles
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Failed to check for updates. RepositoryId: {RepositoryId}, BranchId: {BranchId}, Duration: {Duration}ms",
                repositoryId, branchId, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IncrementalUpdateResult> ProcessIncrementalUpdateAsync(
        string repositoryId,
        string branchId,
        CancellationToken cancellationToken = default,
        GenerationLeaseHandle? lease = null)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Processing incremental update. RepositoryId: {RepositoryId}, BranchId: {BranchId}",
            repositoryId, branchId);

        try
        {
            var repository = await _context.Repositories
                .FirstOrDefaultAsync(r => r.Id == repositoryId && !r.IsDeleted, cancellationToken);

            var branch = await _context.RepositoryBranches
                .FirstOrDefaultAsync(b => b.Id == branchId && !b.IsDeleted, cancellationToken);

            if (repository == null || branch == null)
            {
                throw new InvalidOperationException(
                    $"Repository or branch not found. RepositoryId: {repositoryId}, BranchId: {branchId}");
            }

            await ThrowIfLocalGitDirtyAsync(repository, cancellationToken);

            var previousCommitId = branch.LastCommitId;
            var workspace = await PrepareWorkspaceWithRetryAsync(
                repository, branch.BranchName, previousCommitId, cancellationToken);
            await ThrowIfLocalGitDirtyAsync(repository, cancellationToken);
            var currentCommitId = workspace.CommitId;

            if (previousCommitId == currentCommitId)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "No update needed. RepositoryId: {RepositoryId}, Duration: {Duration}ms",
                    repositoryId, stopwatch.ElapsedMilliseconds);

                return new IncrementalUpdateResult
                {
                    Success = true,
                    PreviousCommitId = previousCommitId,
                    CurrentCommitId = currentCommitId,
                    ChangedFilesCount = 0,
                    UpdatedDocumentsCount = 0,
                    Duration = stopwatch.Elapsed
                };
            }

            var changedFiles = await _repositoryAnalyzer.GetChangedFilesAsync(
                workspace,
                previousCommitId,
                currentCommitId,
                cancellationToken);

            if (changedFiles.Length == 0 && !string.IsNullOrEmpty(previousCommitId))
            {
                await AdvanceBranchStateAsync(repository, branch, currentCommitId, cancellationToken, lease);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Commit advanced without document changes. RepositoryId: {RepositoryId}, PreviousCommit: {PreviousCommit}, CurrentCommit: {CurrentCommit}, Duration: {Duration}ms",
                    repositoryId, previousCommitId, currentCommitId, stopwatch.ElapsedMilliseconds);

                return new IncrementalUpdateResult
                {
                    Success = true,
                    PreviousCommitId = previousCommitId,
                    CurrentCommitId = currentCommitId,
                    ChangedFilesCount = 0,
                    UpdatedDocumentsCount = 0,
                    Duration = stopwatch.Elapsed
                };
            }

            var branchLanguages = await _context.BranchLanguages
                .Where(bl => bl.RepositoryBranchId == branchId && !bl.IsDeleted)
                .ToListAsync(cancellationToken);

            var updatedDocumentsCount = 0;

            // Revalidate immediately before the first fenced document/catalog write.
            await ThrowIfLocalGitDirtyAsync(repository, cancellationToken);

            foreach (var branchLanguage in branchLanguages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation(
                    "Updating wiki for language {LanguageCode}. RepositoryId: {RepositoryId}",
                    branchLanguage.LanguageCode, repositoryId);

                await _wikiGenerator.IncrementalUpdateAsync(
                    workspace,
                    branchLanguage,
                    changedFiles,
                    cancellationToken,
                    lease);

                if (repository.GenerateSkill)
                {
                    await _skillMarkdownBuilder.RefreshSkillMarkdownAsync(
                        _context,
                        repository,
                        branch,
                        branchLanguage,
                        cancellationToken,
                        lease);
                }

                updatedDocumentsCount++;
            }

            await AdvanceBranchStateAsync(repository, branch, currentCommitId, cancellationToken, lease);

            await NotifySubscribersSafelyAsync(
                repository,
                branch,
                new UpdateCheckResult
                {
                    NeedsUpdate = true,
                    PreviousCommitId = previousCommitId,
                    CurrentCommitId = currentCommitId,
                    ChangedFiles = changedFiles
                },
                cancellationToken);

            // The worker persists Completed after this method returns. Keep that final
            // fenced transition behind a fresh source preflight as well.
            await ThrowIfLocalGitDirtyAsync(repository, cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Incremental update completed. RepositoryId: {RepositoryId}, ChangedFiles: {ChangedFilesCount}, UpdatedLanguages: {UpdatedLanguagesCount}, Duration: {Duration}ms",
                repositoryId, changedFiles.Length, updatedDocumentsCount, stopwatch.ElapsedMilliseconds);

            return new IncrementalUpdateResult
            {
                Success = true,
                PreviousCommitId = previousCommitId,
                CurrentCommitId = currentCommitId,
                ChangedFilesCount = changedFiles.Length,
                UpdatedDocumentsCount = updatedDocumentsCount,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex) when (ex is GenerationLeaseLostException or LocalGitWorktreeDirtyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Incremental update failed. RepositoryId: {RepositoryId}, BranchId: {BranchId}, Duration: {Duration}ms",
                repositoryId, branchId, stopwatch.ElapsedMilliseconds);

            return new IncrementalUpdateResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public async Task<string> TriggerManualUpdateAsync(
        string repositoryId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Manual update triggered. RepositoryId: {RepositoryId}, BranchId: {BranchId}",
            repositoryId, branchId);

        var repository = await _context.Repositories
            .FirstOrDefaultAsync(item => item.Id == repositoryId && !item.IsDeleted, cancellationToken)
            ?? throw new InvalidOperationException($"Repository not found: {repositoryId}");
        await ThrowIfLocalGitDirtyAsync(repository, cancellationToken);

        var existingTask = await _context.IncrementalUpdateTasks
            .Where(t => !t.IsDeleted &&
                        t.RepositoryId == repositoryId &&
                        t.BranchId == branchId &&
                        (t.Status == IncrementalUpdateStatus.Pending
                            || t.Status == IncrementalUpdateStatus.Processing))
            .FirstOrDefaultAsync(cancellationToken);

        if (existingTask != null)
        {
            _logger.LogInformation(
                "Existing task found. TaskId: {TaskId}, Status: {Status}",
                existingTask.Id, existingTask.Status);
            return existingTask.Id;
        }

        var activeBranchGenerationTask = await _context.BranchGenerationTasks
            .AnyAsync(t => !t.IsDeleted &&
                           t.RepositoryId == repositoryId &&
                           t.BranchId == branchId &&
                           (t.Status == BranchGenerationTaskStatus.Pending ||
                            t.Status == BranchGenerationTaskStatus.Processing),
                cancellationToken);

        if (activeBranchGenerationTask)
        {
            throw new InvalidOperationException("该分支已有 full generation 任务正在排队或处理中");
        }

        var branch = await _context.RepositoryBranches
            .FirstOrDefaultAsync(b => b.Id == branchId && !b.IsDeleted, cancellationToken);

        var task = new IncrementalUpdateTask
        {
            Id = Guid.NewGuid().ToString(),
            RepositoryId = repositoryId,
            BranchId = branchId,
            PreviousCommitId = branch?.LastCommitId,
            Status = IncrementalUpdateStatus.Pending,
            Priority = _options.ManualTriggerPriority,
            IsManualTrigger = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.IncrementalUpdateTasks.Add(task);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Manual update task created. TaskId: {TaskId}, Priority: {Priority}",
            task.Id, task.Priority);

        return task.Id;
    }

    private async Task AdvanceBranchStateAsync(
        Repository repository,
        RepositoryBranch branch,
        string currentCommitId,
        CancellationToken cancellationToken,
        GenerationLeaseHandle? lease)
    {
        // Keep the final baseline persistence behind a fresh local-source check.
        await ThrowIfLocalGitDirtyAsync(repository, cancellationToken);

        branch.LastCommitId = currentCommitId;
        branch.LastProcessedAt = DateTime.UtcNow;
        branch.UpdatedAt = DateTime.UtcNow;

        repository.LastUpdateCheckAt = DateTime.UtcNow;
        repository.UpdatedAt = DateTime.UtcNow;

        await SaveChangesAsync(lease, cancellationToken);
    }

    private async Task ThrowIfLocalGitDirtyAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        if (RepositorySource.Parse(repository.GitUrl).SourceType != RepositorySourceType.LocalDirectory)
        {
            return;
        }

        var preflight = await _repositoryAnalyzer.GetLocalGitPreflightAsync(repository, cancellationToken);
        if (preflight.IsLocalGit && !preflight.IsClean)
        {
            throw new LocalGitWorktreeDirtyException(preflight);
        }
    }

    private Task SaveChangesAsync(GenerationLeaseHandle? lease, CancellationToken cancellationToken)
    {
        if (lease is null)
        {
            return _context.SaveChangesAsync(cancellationToken);
        }

        return (_writeGuard ?? throw new InvalidOperationException("Generation write guard is not configured."))
            .SaveChangesAsync(_context, lease, cancellationToken);
    }

    private async Task<RepositoryWorkspace> PrepareWorkspaceWithRetryAsync(
        Repository repository,
        string branchName,
        string? previousCommitId,
        CancellationToken cancellationToken)
    {
        var retryCount = 0;
        Exception? lastException = null;

        while (retryCount < _options.MaxRetryAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await _repositoryAnalyzer.PrepareWorkspaceAsync(
                    repository, branchName, previousCommitId, cancellationToken);
            }
            catch (LocalGitWorktreeDirtyException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                retryCount++;

                _logger.LogWarning(ex,
                    "Workspace preparation failed. Attempt {Attempt}/{MaxAttempts}, Repository: {Org}/{Repo}",
                    retryCount, _options.MaxRetryAttempts, repository.OrgName, repository.RepoName);

                if (retryCount < _options.MaxRetryAttempts)
                {
                    if (IsWorkspaceCorrupted(ex))
                    {
                        _logger.LogInformation(
                            "Workspace appears corrupted, cleaning up. Repository: {Org}/{Repo}",
                            repository.OrgName, repository.RepoName);

                        await CleanupCorruptedWorkspaceAsync(repository, cancellationToken);
                    }

                    var delay = _options.RetryBaseDelayMs * (int)Math.Pow(2, retryCount - 1);
                    _logger.LogInformation(
                        "Retrying in {Delay}ms. Repository: {Org}/{Repo}",
                        delay, repository.OrgName, repository.RepoName);

                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            $"Failed to prepare workspace after {_options.MaxRetryAttempts} attempts",
            lastException);
    }

    private static bool IsWorkspaceCorrupted(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("corrupt")
               || message.Contains("invalid")
               || message.Contains("not a git repository")
               || message.Contains("bad object")
               || message.Contains("broken");
    }

    private async Task CleanupCorruptedWorkspaceAsync(
        Repository repository,
        CancellationToken cancellationToken)
    {
        try
        {
            var workspace = new RepositoryWorkspace
            {
                Organization = repository.OrgName,
                RepositoryName = repository.RepoName
            };

            await _repositoryAnalyzer.CleanupWorkspaceAsync(workspace, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to cleanup corrupted workspace. Repository: {Org}/{Repo}",
                repository.OrgName, repository.RepoName);
        }
    }

    private async Task NotifySubscribersSafelyAsync(
        Repository repository,
        RepositoryBranch branch,
        UpdateCheckResult checkResult,
        CancellationToken cancellationToken)
    {
        try
        {
            var notification = new RepositoryUpdateNotification
            {
                RepositoryId = repository.Id,
                RepositoryName = $"{repository.OrgName}/{repository.RepoName}",
                BranchName = branch.BranchName,
                Summary = $"Updated with {checkResult.ChangedFiles?.Length ?? 0} changed files",
                ChangedFilesCount = checkResult.ChangedFiles?.Length ?? 0,
                UpdatedAt = DateTime.UtcNow,
                CommitId = checkResult.CurrentCommitId ?? string.Empty
            };

            await _notificationService.NotifySubscribersAsync(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to notify subscribers. RepositoryId: {RepositoryId}",
                repository.Id);
        }
    }
}
