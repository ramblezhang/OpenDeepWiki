using System.ComponentModel.DataAnnotations;

namespace OpenDeepWiki.Entities;

/// <summary>
/// MCP 使用日志实体
/// </summary>
public class McpUsageLog : AggregateRoot<string>
{
    /// <summary>
    /// 用户 ID（从 Bearer Token 解析）
    /// </summary>
    [StringLength(100)]
    public string? UserId { get; set; }

    /// <summary>
    /// 用户端实际提交的本机登录名。
    /// </summary>
    [StringLength(128)]
    public string? PresentedUser { get; set; }

    /// <summary>
    /// 白名单 alias 归一化后的用户标识。
    /// </summary>
    [StringLength(128)]
    public string? CanonicalUser { get; set; }

    /// <summary>
    /// 身份来源，例如 declared_username 或 legacy_anonymous。
    /// </summary>
    [StringLength(32)]
    public string? IdentityType { get; set; }

    /// <summary>
    /// 调用结果分类，例如 authorized_success、authorized_error 或 denied_not_registered。
    /// </summary>
    [StringLength(64)]
    public string? Outcome { get; set; }

    /// <summary>
    /// 稳定错误码，不包含允许用户列表或请求正文。
    /// </summary>
    [StringLength(64)]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 提供商 ID
    /// </summary>
    [StringLength(100)]
    public string? McpProviderId { get; set; }

    /// <summary>
    /// 调用的工具名
    /// </summary>
    [Required]
    [StringLength(200)]
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// 请求摘要
    /// </summary>
    [StringLength(1000)]
    public string? RequestSummary { get; set; }

    /// <summary>
    /// HTTP 状态码
    /// </summary>
    public int ResponseStatus { get; set; }

    /// <summary>
    /// 响应耗时（毫秒）
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// 输入 Token 数
    /// </summary>
    public int InputTokens { get; set; }

    /// <summary>
    /// 输出 Token 数
    /// </summary>
    public int OutputTokens { get; set; }

    /// <summary>
    /// 请求 IP
    /// </summary>
    [StringLength(50)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// 客户端 User-Agent
    /// </summary>
    [StringLength(500)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    [StringLength(2000)]
    public string? ErrorMessage { get; set; }
}
