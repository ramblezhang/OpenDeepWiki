using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Mcp;

/// <summary>
/// MCP 使用日志服务接口
/// </summary>
public interface IMcpUsageLogService
{
    /// <summary>
    /// 记录 MCP 使用日志；主库异常时写入持久化回退文件，且不改变业务工具结果。
    /// </summary>
    Task LogUsageAsync(McpUsageLog log);

    /// <summary>
    /// 聚合指定日期的日志到每日统计
    /// </summary>
    Task AggregateDailyStatisticsAsync(DateTime date);

    /// <summary>
    /// Rebuilds all materialized daily statistics from versioned business tool-call logs.
    /// </summary>
    Task RebuildDailyStatisticsAsync();

    /// <summary>
    /// Replays durable fallback events that could not be written to the database earlier.
    /// </summary>
    Task ReplayFallbackLogsAsync();

    /// <summary>
    /// Removes raw usage rows and daily aggregates older than the configured retention period.
    /// </summary>
    Task PruneExpiredUsageDataAsync();

    /// <summary>
    /// Returns whether all durable fallback events have reached the primary database.
    /// </summary>
    McpUsageLoggingHealth GetLoggingHealth();
}

public sealed record McpUsageLoggingHealth(
    bool Healthy,
    int PendingFiles,
    long PendingBytes,
    int QuarantinedFiles);
