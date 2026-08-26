using OpenDeepWiki.Services.Mcp;

namespace OpenDeepWiki.MCP;

/// <summary>
/// MCP 统计聚合后台服务
/// 每小时聚合前一天的使用日志到 McpDailyStatistics
/// </summary>
public class McpStatisticsAggregationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<McpStatisticsAggregationService> _logger;
    private static readonly TimeSpan AggregationInterval = TimeSpan.FromHours(1);

    public McpStatisticsAggregationService(
        IServiceScopeFactory scopeFactory,
        ILogger<McpStatisticsAggregationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a bit before first run to let the app start up
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var logService = scope.ServiceProvider.GetRequiredService<IMcpUsageLogService>();
            await logService.ReplayFallbackLogsAsync();
            await logService.RebuildDailyStatisticsAsync();
            await logService.PruneExpiredUsageDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP 每日统计启动重算异常");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var logService = scope.ServiceProvider.GetRequiredService<IMcpUsageLogService>();

                await logService.ReplayFallbackLogsAsync();
                var today = DateTime.UtcNow.Date;
                await logService.AggregateDailyStatisticsAsync(today);
                await logService.AggregateDailyStatisticsAsync(today.AddDays(-1));
                await logService.PruneExpiredUsageDataAsync();

                _logger.LogDebug("MCP 统计聚合完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP 统计聚合服务异常");
            }

            await Task.Delay(AggregationInterval, stoppingToken);
        }
    }
}
