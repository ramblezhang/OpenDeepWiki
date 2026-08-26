using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenDeepWiki.Entities;
using OpenDeepWiki.Services.Mcp;

namespace OpenDeepWiki.MCP;

/// <summary>
/// Enforces caller_user and records one usage row around every tools/call request.
/// Protocol initialization and tools/list remain available without a caller identity.
/// </summary>
public static class McpCallerAccessFilter
{
    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> CreateListTools()
    {
        return next => async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);
            var policy = request.Services!.GetRequiredService<IMcpCallerAccessPolicy>();
            if (!policy.RequiresCallerUser)
            {
                return result;
            }

            foreach (var tool in result.Tools)
            {
                tool.InputSchema = RequireCallerUserInSchema(tool.InputSchema);
            }

            return result;
        };
    }

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create()
    {
        return next => async (request, cancellationToken) =>
        {
            var stopwatch = Stopwatch.StartNew();
            var callerUser = ExtractCallerUser(request.Params.Arguments);
            var policy = request.Services!.GetRequiredService<IMcpCallerAccessPolicy>();
            var decision = policy.Authorize(callerUser);

            if (!decision.Allowed)
            {
                stopwatch.Stop();
                await LogAsync(
                    request,
                    decision,
                    request.Params.Name,
                    decision.Outcome,
                    GetDeniedStatus(decision.ErrorCode),
                    stopwatch.ElapsedMilliseconds,
                    decision.ErrorCode);
                return ErrorResult(decision);
            }

            try
            {
                var result = await next(request, cancellationToken);
                stopwatch.Stop();
                var isError = result.IsError == true;
                await LogAsync(
                    request,
                    decision,
                    request.Params.Name,
                    decision.IdentityType == "legacy_anonymous"
                        ? "legacy_anonymous"
                        : isError ? "authorized_error" : "authorized_success",
                    isError ? 500 : 200,
                    stopwatch.ElapsedMilliseconds,
                    isError ? "TOOL_ERROR" : null);
                return result;
            }
            catch
            {
                stopwatch.Stop();
                await LogAsync(
                    request,
                    decision,
                    request.Params.Name,
                    decision.IdentityType == "legacy_anonymous" ? "legacy_anonymous" : "authorized_error",
                    500,
                    stopwatch.ElapsedMilliseconds,
                    "TOOL_EXCEPTION");
                throw;
            }
        };
    }

    internal static string? ExtractCallerUser(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments == null || !arguments.TryGetValue("caller_user", out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
    }

    internal static JsonElement RequireCallerUserInSchema(JsonElement inputSchema)
    {
        if (JsonNode.Parse(inputSchema.GetRawText()) is not JsonObject schema
            || schema["properties"] is not JsonObject properties
            || !properties.ContainsKey("caller_user"))
        {
            return inputSchema;
        }

        var previousCallerSchema = properties["caller_user"] as JsonObject;
        var normalizedCallerSchema = new JsonObject
        {
            ["type"] = "string",
            ["minLength"] = 1,
            ["maxLength"] = 128
        };
        if (previousCallerSchema?["title"] is { } title)
        {
            normalizedCallerSchema["title"] = title.DeepClone();
        }
        if (previousCallerSchema?["description"] is { } description)
        {
            normalizedCallerSchema["description"] = description.DeepClone();
        }
        properties["caller_user"] = normalizedCallerSchema;

        var required = schema["required"] as JsonArray ?? [];
        if (!required.Any(item => item?.GetValue<string>() == "caller_user"))
        {
            required.Add("caller_user");
        }
        schema["required"] = required;
        return JsonSerializer.SerializeToElement(schema);
    }

    private static int GetDeniedStatus(string? errorCode) => errorCode switch
    {
        "CALLER_USER_REQUIRED" or "CALLER_USER_INVALID" => 400,
        "ACCESS_CONFIG_INVALID" => 503,
        _ => 403
    };

    private static CallToolResult ErrorResult(McpCallerAccessDecision decision)
    {
        var payload = JsonSerializer.Serialize(new
        {
            error = true,
            code = decision.ErrorCode,
            message = decision.Message
        });
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = payload }]
        };
    }

    private static async Task LogAsync(
        RequestContext<CallToolRequestParams> request,
        McpCallerAccessDecision decision,
        string toolName,
        string outcome,
        int responseStatus,
        long durationMs,
        string? errorCode)
    {
        try
        {
            var logService = request.Services!.GetRequiredService<IMcpUsageLogService>();
            var httpContext = request.Services!.GetService<IHttpContextAccessor>()?.HttpContext;
            var principal = request.User ?? httpContext?.User;
            var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? principal?.FindFirstValue("sub");
            var userAgent = httpContext?.Request.Headers.UserAgent.FirstOrDefault();

            await logService.LogUsageAsync(new McpUsageLog
            {
                UserId = userId,
                PresentedUser = decision.PresentedUser,
                CanonicalUser = decision.CanonicalUser,
                IdentityType = decision.IdentityType,
                Outcome = outcome,
                ErrorCode = errorCode,
                ToolName = toolName,
                ResponseStatus = responseStatus,
                DurationMs = durationMs,
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent
            });
        }
        catch (Exception ex)
        {
            request.Services?.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(McpCallerAccessFilter).FullName!)
                .LogCritical(ex, "MCP 使用日志调用链异常，业务结果保持不变: {ToolName}", toolName);
        }
    }
}
