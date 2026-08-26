using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenDeepWiki.EFCore;
using OpenDeepWiki.Entities;

namespace OpenDeepWiki.Services.Mcp;

/// <summary>
/// MCP 使用日志服务实现
/// </summary>
public class McpUsageLogService : IMcpUsageLogService
{
    private const string GlobalMcpProviderName = "OpenDeepWiki Global MCP";
    private const string GlobalMcpServerUrl = "/api/mcp";
    private static readonly SemaphoreSlim FallbackFileLock = new(1, 1);

    private readonly IContextFactory _contextFactory;
    private readonly ILogger<McpUsageLogService> _logger;
    private readonly string _fallbackPath;
    private readonly long _fallbackMaxBytes;
    private readonly int _fallbackBackupCount;
    private readonly int _retentionDays;

    public McpUsageLogService(
        IContextFactory contextFactory,
        ILogger<McpUsageLogService> logger,
        IConfiguration? configuration = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _fallbackPath = Path.GetFullPath(
            configuration?["MCP_USAGE_FALLBACK_FILE"] ?? "/data/mcp-usage-fallback.jsonl");
        _fallbackMaxBytes = Math.Clamp(
            configuration?.GetValue<long?>("MCP_USAGE_FALLBACK_MAX_BYTES") ?? 10 * 1024 * 1024,
            1024,
            1024L * 1024 * 1024);
        _fallbackBackupCount = Math.Clamp(
            configuration?.GetValue<int?>("MCP_USAGE_FALLBACK_BACKUP_COUNT") ?? 12,
            1,
            1000);
        _retentionDays = Math.Clamp(
            configuration?.GetValue<int?>("MCP_USAGE_RETENTION_DAYS") ?? 180,
            0,
            3650);
    }

    public async Task LogUsageAsync(McpUsageLog log)
    {
        log.Id = string.IsNullOrWhiteSpace(log.Id) ? Guid.NewGuid().ToString() : log.Id;
        log.CreatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(log.UserId))
        {
            log.UserId = "anonymous";
        }

        Exception? databaseError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var context = _contextFactory.CreateContext();
                log.McpProviderId = await ResolveProviderIdAsync(context, log.McpProviderId);
                context.McpUsageLogs.Add(log);
                await context.SaveChangesAsync();
                return;
            }
            catch (Exception ex)
            {
                databaseError = ex;
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt));
                }
            }
        }

        try
        {
            await AppendFallbackAsync(log);
            _logger.LogError(
                databaseError,
                "写入 MCP 使用日志数据库失败，事件已写入持久化回退文件: {ToolName}",
                log.ToolName);
        }
        catch (Exception fallbackError)
        {
            _logger.LogCritical(
                fallbackError,
                "MCP 使用日志数据库和持久化回退文件均写入失败，事件未能保存: {ToolName}; DatabaseError={DatabaseError}",
                log.ToolName,
                databaseError?.Message);
        }
    }

    public McpUsageLoggingHealth GetLoggingHealth()
    {
        try
        {
            var directory = Path.GetDirectoryName(_fallbackPath)!;
            if (!Directory.Exists(directory))
            {
                return new McpUsageLoggingHealth(true, 0, 0, 0);
            }

            var fileName = Path.GetFileName(_fallbackPath);
            var allFiles = Directory.EnumerateFiles(directory, $"{fileName}*").ToList();
            var quarantinedFiles = allFiles.Count(path =>
                path.Contains(".invalid-", StringComparison.Ordinal));
            var files = allFiles
                .Where(path => !path.Contains(".invalid-", StringComparison.Ordinal))
                .Select(path => new FileInfo(path))
                .Where(file => file.Exists && file.Length > 0)
                .ToList();
            return new McpUsageLoggingHealth(
                files.Count == 0 && quarantinedFiles == 0,
                files.Count,
                files.Sum(file => file.Length),
                quarantinedFiles);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "无法检查 MCP 使用日志回退文件状态");
            return new McpUsageLoggingHealth(false, 0, 0, 0);
        }
    }

    public async Task ReplayFallbackLogsAsync()
    {
        await FallbackFileLock.WaitAsync();
        try
        {
            while (TryClaimFallbackFile(out var replayPath))
            {
                var invalidLineCount = 0;
                try
                {
                    var pendingLogs = new List<McpUsageLog>();
                    foreach (var line in await File.ReadAllLinesAsync(replayPath))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        try
                        {
                            var pendingLog = JsonSerializer.Deserialize<McpUsageLog>(line);
                            if (pendingLog == null || string.IsNullOrWhiteSpace(pendingLog.ToolName))
                            {
                                invalidLineCount++;
                                continue;
                            }

                            pendingLog.Id = string.IsNullOrWhiteSpace(pendingLog.Id)
                                ? Guid.NewGuid().ToString()
                                : pendingLog.Id;
                            pendingLog.UserId = string.IsNullOrWhiteSpace(pendingLog.UserId)
                                ? "anonymous"
                                : pendingLog.UserId;
                            pendingLogs.Add(pendingLog);
                        }
                        catch (JsonException)
                        {
                            invalidLineCount++;
                        }
                    }

                    pendingLogs = pendingLogs
                        .GroupBy(log => log.Id, StringComparer.Ordinal)
                        .Select(group => group.First())
                        .ToList();

                    foreach (var batch in pendingLogs.Chunk(200))
                    {
                        using var context = _contextFactory.CreateContext();
                        var ids = batch.Select(log => log.Id).ToList();
                        var existingIds = await context.McpUsageLogs
                            .Where(log => ids.Contains(log.Id))
                            .Select(log => log.Id)
                            .ToHashSetAsync();
                        var defaultProviderId = await ResolveProviderIdAsync(context, null);
                        foreach (var pendingLog in batch.Where(log => !existingIds.Contains(log.Id)))
                        {
                            pendingLog.McpProviderId = string.IsNullOrWhiteSpace(pendingLog.McpProviderId)
                                ? defaultProviderId
                                : pendingLog.McpProviderId;
                            context.McpUsageLogs.Add(pendingLog);
                        }
                        await context.SaveChangesAsync();
                    }

                    if (invalidLineCount == 0)
                    {
                        File.Delete(replayPath);
                    }
                    else
                    {
                        var invalidPath = $"{_fallbackPath}.invalid-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                        File.Move(replayPath, invalidPath, true);
                        ProtectFile(invalidPath);
                        _logger.LogError(
                            "MCP 回退日志包含 {Count} 条无效记录，原文件保留为 {Path}",
                            invalidLineCount,
                            invalidPath);
                    }

                    _logger.LogInformation(
                        "MCP 回退日志重放完成: {Count} 条有效记录",
                        pendingLogs.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MCP 回退日志重放失败，文件保留待下次重试: {Path}", replayPath);
                    return;
                }
            }
        }
        finally
        {
            FallbackFileLock.Release();
        }
    }

    public async Task PruneExpiredUsageDataAsync()
    {
        if (_retentionDays == 0)
        {
            return;
        }

        try
        {
            using var context = _contextFactory.CreateContext();
            var cutoff = DateTime.UtcNow.Date.AddDays(-_retentionDays);
            var deletedLogs = await context.McpUsageLogs
                .Where(log => log.CreatedAt < cutoff)
                .ExecuteDeleteAsync();
            var deletedStatistics = await context.McpDailyStatistics
                .Where(statistic => statistic.Date < cutoff)
                .ExecuteDeleteAsync();
            if (deletedLogs > 0 || deletedStatistics > 0)
            {
                _logger.LogInformation(
                    "MCP 使用数据保留策略已执行: cutoff={Cutoff}, logs={Logs}, daily={Daily}",
                    cutoff,
                    deletedLogs,
                    deletedStatistics);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP 使用数据保留策略执行失败");
        }
    }

    public async Task AggregateDailyStatisticsAsync(DateTime date)
    {
        try
        {
            using var context = _contextFactory.CreateContext();
            var providerCount = await AggregateDailyStatisticsAsync(context, date.Date);
            _logger.LogInformation(
                "MCP 每日统计聚合完成: {Date}, {Count} 条提供商记录",
                date.Date.ToString("yyyy-MM-dd"),
                providerCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP 每日统计聚合失败: {Date}", date.ToString("yyyy-MM-dd"));
        }
    }

    public async Task RebuildDailyStatisticsAsync()
    {
        try
        {
            using var context = _contextFactory.CreateContext();
            var materializedDates = await context.McpDailyStatistics
                .Where(statistic => !statistic.IsDeleted)
                .Select(statistic => statistic.Date)
                .Distinct()
                .ToListAsync();
            var toolCallDates = await context.McpUsageLogs
                .Where(log => !log.IsDeleted && log.IdentityType != null)
                .Select(log => log.CreatedAt.Date)
                .Distinct()
                .ToListAsync();
            var dates = materializedDates
                .Concat(toolCallDates)
                .Select(date => date.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            foreach (var date in dates)
            {
                await AggregateDailyStatisticsAsync(context, date);
            }

            _logger.LogInformation("MCP 每日统计全量重算完成: {Count} 天", dates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP 每日统计全量重算失败");
        }
    }

    private static async Task<string> ResolveProviderIdAsync(IContext context, string? providerId)
    {
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            return providerId;
        }

        return await context.McpProviders
                   .Where(provider => provider.ServerUrl == GlobalMcpServerUrl
                                      && provider.IsActive
                                      && !provider.IsDeleted)
                   .OrderByDescending(provider => provider.Name == GlobalMcpProviderName)
                   .ThenBy(provider => provider.SortOrder)
                   .ThenBy(provider => provider.Id)
                   .Select(provider => provider.Id)
                   .FirstOrDefaultAsync()
               ?? "unknown";
    }

    private async Task AppendFallbackAsync(McpUsageLog log)
    {
        var encoded = JsonSerializer.Serialize(log) + Environment.NewLine;
        await FallbackFileLock.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_fallbackPath)!;
            Directory.CreateDirectory(directory);
            if (File.Exists(_fallbackPath)
                && new FileInfo(_fallbackPath).Length + encoded.Length > _fallbackMaxBytes)
            {
                RotateFallbackFiles();
            }

            await File.AppendAllTextAsync(_fallbackPath, encoded);
            ProtectFile(_fallbackPath);
        }
        finally
        {
            FallbackFileLock.Release();
        }
    }

    private bool TryClaimFallbackFile(out string replayPath)
    {
        replayPath = $"{_fallbackPath}.replay";
        if (File.Exists(replayPath))
        {
            return true;
        }

        for (var index = _fallbackBackupCount; index >= 1; index--)
        {
            var rotatedPath = $"{_fallbackPath}.{index}";
            if (!File.Exists(rotatedPath))
            {
                continue;
            }

            File.Move(rotatedPath, replayPath);
            ProtectFile(replayPath);
            return true;
        }

        if (!File.Exists(_fallbackPath))
        {
            return false;
        }

        File.Move(_fallbackPath, replayPath);
        ProtectFile(replayPath);
        return true;
    }

    private void RotateFallbackFiles()
    {
        var oldestPath = $"{_fallbackPath}.{_fallbackBackupCount}";
        if (File.Exists(oldestPath))
        {
            _logger.LogCritical(
                "MCP 持久化回退日志已达到 {Count} 个轮转文件上限，最旧文件将被删除: {Path}",
                _fallbackBackupCount,
                oldestPath);
        }
        File.Delete(oldestPath);
        for (var index = _fallbackBackupCount - 1; index >= 1; index--)
        {
            var sourcePath = $"{_fallbackPath}.{index}";
            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, $"{_fallbackPath}.{index + 1}");
            }
        }

        File.Move(_fallbackPath, $"{_fallbackPath}.1");
        ProtectFile($"{_fallbackPath}.1");
    }

    private static void ProtectFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static async Task<int> AggregateDailyStatisticsAsync(IContext context, DateTime dateStart)
    {
        const string nullProviderKey = "\0";
        var dateEnd = dateStart.AddDays(1);
        var logs = await context.McpUsageLogs
            .Where(log => !log.IsDeleted
                          && log.IdentityType != null
                          && log.CreatedAt >= dateStart
                          && log.CreatedAt < dateEnd)
            .GroupBy(log => log.McpProviderId)
            .Select(group => new
            {
                McpProviderId = group.Key,
                RequestCount = group.LongCount(),
                SuccessCount = group.LongCount(log => log.ResponseStatus >= 200 && log.ResponseStatus < 300),
                ErrorCount = group.LongCount(log => log.ResponseStatus >= 400),
                TotalDurationMs = group.Sum(log => log.DurationMs),
                InputTokens = group.Sum(log => (long)log.InputTokens),
                OutputTokens = group.Sum(log => (long)log.OutputTokens)
            })
            .ToListAsync();
        var existingByProvider = await context.McpDailyStatistics
            .Where(statistic => !statistic.IsDeleted && statistic.Date == dateStart)
            .ToDictionaryAsync(statistic => statistic.McpProviderId ?? nullProviderKey);
        var now = DateTime.UtcNow;

        foreach (var statistic in existingByProvider.Values)
        {
            statistic.RequestCount = 0;
            statistic.SuccessCount = 0;
            statistic.ErrorCount = 0;
            statistic.TotalDurationMs = 0;
            statistic.InputTokens = 0;
            statistic.OutputTokens = 0;
            statistic.UpdatedAt = now;
        }

        foreach (var log in logs)
        {
            var providerKey = log.McpProviderId ?? nullProviderKey;
            if (!existingByProvider.TryGetValue(providerKey, out var statistic))
            {
                statistic = new McpDailyStatistics
                {
                    Id = Guid.NewGuid().ToString(),
                    McpProviderId = log.McpProviderId,
                    Date = dateStart,
                    CreatedAt = now
                };
                context.McpDailyStatistics.Add(statistic);
            }

            statistic.RequestCount = log.RequestCount;
            statistic.SuccessCount = log.SuccessCount;
            statistic.ErrorCount = log.ErrorCount;
            statistic.TotalDurationMs = log.TotalDurationMs;
            statistic.InputTokens = log.InputTokens;
            statistic.OutputTokens = log.OutputTokens;
            statistic.UpdatedAt = now;
        }

        await context.SaveChangesAsync();
        return logs.Count;
    }
}
