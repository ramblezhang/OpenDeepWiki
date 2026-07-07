namespace OpenDeepWiki.Models;

public static class RepositoryEffectiveStatuses
{
    public const string RepositoryFullPending = nameof(RepositoryFullPending);
    public const string RepositoryFullProcessing = nameof(RepositoryFullProcessing);
    public const string AllBranchesQueued = nameof(AllBranchesQueued);
    public const string PartialBranchesQueued = nameof(PartialBranchesQueued);
    public const string AllBranchesGenerating = nameof(AllBranchesGenerating);
    public const string PartialBranchesGenerating = nameof(PartialBranchesGenerating);
    public const string IncrementalUpdating = nameof(IncrementalUpdating);
    public const string Failed = nameof(Failed);
    public const string PartialFailed = nameof(PartialFailed);
    public const string Completed = nameof(Completed);
    public const string Cancelled = nameof(Cancelled);
    public const string Unknown = nameof(Unknown);
}

public class RepositoryEffectiveStatusDto
{
    public string EffectiveStatus { get; set; } = RepositoryEffectiveStatuses.Unknown;
    public string EffectiveStatusReason { get; set; } = string.Empty;
    public RepositoryStatusCountsDto StatusCounts { get; set; } = new();
    public List<RepositoryActiveOperationDto> ActiveOperations { get; set; } = [];
    public List<RepositoryBlockingFailureDto> BlockingFailures { get; set; } = [];
}

public class RepositoryStatusCountsDto
{
    public int TotalBranches { get; set; }
    public int CompletedBranches { get; set; }
    public int FailedBranches { get; set; }
    public int BranchFullPending { get; set; }
    public int BranchFullProcessing { get; set; }
    public int IncrementalPending { get; set; }
    public int IncrementalProcessing { get; set; }
}

public class RepositoryActiveOperationDto
{
    public string Type { get; set; } = string.Empty;
    public string? RepositoryId { get; set; }
    public string? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? TaskId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
}

public class RepositoryBlockingFailureDto
{
    public string Type { get; set; } = string.Empty;
    public string? BranchId { get; set; }
    public string? BranchName { get; set; }
    public string? TaskId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Message { get; set; }
}
