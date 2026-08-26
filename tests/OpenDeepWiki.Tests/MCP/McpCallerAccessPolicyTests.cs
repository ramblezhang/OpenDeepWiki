using Microsoft.Extensions.Logging.Abstractions;
using OpenDeepWiki.MCP;
using System.Text.Json;
using Xunit;

namespace OpenDeepWiki.Tests.MCP;

public class McpCallerAccessPolicyTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"opendeepwiki-mcp-access-{Guid.NewGuid():N}");

    public McpCallerAccessPolicyTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void ExplicitJsonNullIsTreatedAsMissingCallerIdentity()
    {
        using var document = JsonDocument.Parse("{\"caller_user\":null}");
        var arguments = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value);

        Assert.Null(McpCallerAccessFilter.ExtractCallerUser(arguments));
    }

    [Fact]
    public void StrictModeSchemaMarksCallerUserAsRequired()
    {
        var schema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string" },
                caller_user = new { type = new[] { "string", "null" } }
            },
            required = new[] { "query" }
        });

        var strictSchema = McpCallerAccessFilter.RequireCallerUserInSchema(schema);
        var required = strictSchema.GetProperty("required")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();

        Assert.Equal(new[] { "query", "caller_user" }, required);
        var callerUserSchema = strictSchema.GetProperty("properties").GetProperty("caller_user");
        Assert.Equal("string", callerUserSchema.GetProperty("type").GetString());
        Assert.Equal(1, callerUserSchema.GetProperty("minLength").GetInt32());
        Assert.Equal(128, callerUserSchema.GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void AliasResolvesToCanonicalUser()
    {
        var path = WriteAccessFile(enabled: true, services: ["wiki", "datasheet"]);
        var policy = CreatePolicy(path, requireCallerUser: true);

        var decision = policy.Authorize(@"YOUDAO\zhangsan");

        Assert.True(decision.Allowed);
        Assert.Equal(@"YOUDAO\zhangsan", decision.PresentedUser);
        Assert.Equal("zhangsan", decision.CanonicalUser);
        Assert.Equal("declared_username", decision.IdentityType);
    }

    [Theory]
    [InlineData(null, "CALLER_USER_REQUIRED")]
    [InlineData("bad user", "CALLER_USER_INVALID")]
    [InlineData("lisi", "USER_NOT_ALLOWED")]
    public void StrictModeDeniesMissingInvalidOrUnknownUsers(string? callerUser, string expectedCode)
    {
        var path = WriteAccessFile(enabled: true, services: ["wiki"]);
        var decision = CreatePolicy(path, requireCallerUser: true).Authorize(callerUser);

        Assert.False(decision.Allowed);
        Assert.Equal(expectedCode, decision.ErrorCode);
    }

    [Fact]
    public void DisabledAndServiceScopeAreEnforced()
    {
        var disabledPath = WriteAccessFile(enabled: false, services: ["wiki"]);
        Assert.Equal(
            "USER_DISABLED",
            CreatePolicy(disabledPath, requireCallerUser: true).Authorize("zhangsan").ErrorCode);

        var datasheetOnlyPath = WriteAccessFile(enabled: true, services: ["datasheet"]);
        Assert.Equal(
            "SERVICE_NOT_ALLOWED",
            CreatePolicy(datasheetOnlyPath, requireCallerUser: true).Authorize("zhangsan").ErrorCode);
    }

    [Fact]
    public void CompatibilityModeAllowsOnlyMissingLegacyIdentityWithoutConfiguration()
    {
        var path = Path.Combine(_directory, "missing.yaml");
        var policy = CreatePolicy(path, requireCallerUser: false);

        var legacy = policy.Authorize(null);
        var declared = policy.Authorize("zhangsan");

        Assert.True(legacy.Allowed);
        Assert.Equal("legacy_anonymous", legacy.Outcome);
        Assert.False(declared.Allowed);
        Assert.Equal("ACCESS_CONFIG_INVALID", declared.ErrorCode);
    }

    [Fact]
    public void AllowlistHotReloadsAfterAtomicReplacement()
    {
        var path = WriteAccessFile(enabled: false, services: ["wiki"]);
        var policy = CreatePolicy(path, requireCallerUser: true);
        Assert.Equal("USER_DISABLED", policy.Authorize("zhangsan").ErrorCode);

        var replacement = Path.Combine(_directory, "replacement.yaml");
        WriteAccessFile(replacement, enabled: true, services: ["wiki"]);
        File.Move(replacement, path, overwrite: true);

        Assert.True(policy.Authorize("zhangsan").Allowed);
    }

    [Theory]
    [InlineData("version: 1\nusers:\n")]
    [InlineData("version: 1\nusers:\n  zhangsan:\n")]
    [InlineData("version: 1\nusers:\n  ?\n  : { enabled: true, services: [wiki] }\n")]
    public void MalformedIdentityConfigurationReturnsStableError(string yaml)
    {
        var path = Path.Combine(_directory, $"invalid-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml);

        var decision = CreatePolicy(path, requireCallerUser: true).Authorize("zhangsan");

        Assert.False(decision.Allowed);
        Assert.Equal("ACCESS_CONFIG_INVALID", decision.ErrorCode);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private McpCallerAccessPolicy CreatePolicy(string path, bool requireCallerUser) =>
        new(path, requireCallerUser, NullLogger<McpCallerAccessPolicy>.Instance);

    private string WriteAccessFile(bool enabled, IReadOnlyList<string> services)
    {
        var path = Path.Combine(_directory, $"access-{Guid.NewGuid():N}.yaml");
        return WriteAccessFile(path, enabled, services);
    }

    private static string WriteAccessFile(string path, bool enabled, IReadOnlyList<string> services)
    {
        var serviceLines = string.Join(Environment.NewLine, services.Select(service => $"      - {service}"));
        File.WriteAllText(path, $$"""
            version: 1
            users:
              zhangsan:
                enabled: {{enabled.ToString().ToLowerInvariant()}}
                aliases:
                  - zhangsan
                  - YOUDAO\zhangsan
                  - zhangsan@youdao.com
                services:
            {{serviceLines}}

            """);
        return path;
    }
}
