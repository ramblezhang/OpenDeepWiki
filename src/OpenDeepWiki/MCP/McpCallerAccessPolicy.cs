using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenDeepWiki.MCP;

public sealed record McpCallerAccessDecision(
    bool Allowed,
    string? PresentedUser,
    string? CanonicalUser,
    string IdentityType,
    string Outcome,
    string? ErrorCode = null,
    string? Message = null);

public interface IMcpCallerAccessPolicy
{
    bool RequiresCallerUser { get; }

    McpCallerAccessDecision Authorize(string? callerUser);
}

/// <summary>
/// Authorizes the declarative caller_user argument against a hot-reloaded YAML allowlist.
/// This is an internal-network accountability control, not proof of identity.
/// </summary>
public sealed class McpCallerAccessPolicy : IMcpCallerAccessPolicy
{
    private readonly string _path;
    private readonly bool _requireCallerUser;
    private readonly ILogger<McpCallerAccessPolicy> _logger;
    private readonly object _reloadLock = new();
    private FileStamp? _stamp;
    private Dictionary<string, UserEntry> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private string? _configurationError;

    public McpCallerAccessPolicy(
        string path,
        bool requireCallerUser,
        ILogger<McpCallerAccessPolicy> logger)
    {
        _path = Path.GetFullPath(path);
        _requireCallerUser = requireCallerUser;
        _logger = logger;
    }

    public bool RequiresCallerUser => _requireCallerUser;

    public McpCallerAccessDecision Authorize(string? callerUser)
    {
        if (callerUser == null)
        {
            return _requireCallerUser
                ? Denied(
                    null,
                    null,
                    "missing",
                    "denied_caller_user_required",
                    "CALLER_USER_REQUIRED",
                    "caller_user is required for this MCP tool")
                : new McpCallerAccessDecision(
                    true,
                    null,
                    null,
                    "legacy_anonymous",
                    "legacy_anonymous");
        }

        if (!IsValidIdentity(callerUser))
        {
            return Denied(
                null,
                null,
                "invalid",
                "denied_caller_user_invalid",
                "CALLER_USER_INVALID",
                "caller_user has an invalid format");
        }

        ReloadIfNeeded();
        if (_configurationError != null)
        {
            return Denied(
                callerUser,
                null,
                "declared_username",
                "denied_access_config",
                "ACCESS_CONFIG_INVALID",
                "MCP access configuration is unavailable");
        }

        if (!_aliases.TryGetValue(callerUser, out var entry))
        {
            return Denied(
                callerUser,
                null,
                "declared_username",
                "denied_not_registered",
                "USER_NOT_ALLOWED",
                "caller_user is not allowed to use this MCP service");
        }

        if (!entry.Enabled)
        {
            return Denied(
                callerUser,
                entry.CanonicalUser,
                "declared_username",
                "denied_user_disabled",
                "USER_DISABLED",
                "caller_user is disabled");
        }

        if (!entry.Services.Contains("wiki"))
        {
            return Denied(
                callerUser,
                entry.CanonicalUser,
                "declared_username",
                "denied_service_not_allowed",
                "SERVICE_NOT_ALLOWED",
                "caller_user is not allowed to use this MCP service");
        }

        return new McpCallerAccessDecision(
            true,
            callerUser,
            entry.CanonicalUser,
            "declared_username",
            "authorized");
    }

    private static McpCallerAccessDecision Denied(
        string? presentedUser,
        string? canonicalUser,
        string identityType,
        string outcome,
        string errorCode,
        string message) => new(
        false,
        presentedUser,
        canonicalUser,
        identityType,
        outcome,
        errorCode,
        message);

    private void ReloadIfNeeded()
    {
        var stamp = GetFileStamp();
        if (stamp == _stamp)
        {
            return;
        }

        lock (_reloadLock)
        {
            stamp = GetFileStamp();
            if (stamp == _stamp)
            {
                return;
            }

            try
            {
                if (!stamp.Exists)
                {
                    throw new InvalidDataException("access file does not exist");
                }

                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                var configuration = deserializer.Deserialize<AccessConfiguration>(File.ReadAllText(_path))
                                    ?? throw new InvalidDataException("access file is empty");
                if (configuration.Version != 1)
                {
                    throw new InvalidDataException("version must be 1");
                }

                if (configuration.Users == null)
                {
                    throw new InvalidDataException("users must be a mapping");
                }

                var aliases = new Dictionary<string, UserEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var (canonicalUser, user) in configuration.Users)
                {
                    if (user == null)
                    {
                        throw new InvalidDataException($"user {canonicalUser} must be a mapping");
                    }

                    ValidateIdentity(canonicalUser, "canonical user");
                    var entry = new UserEntry(
                        canonicalUser,
                        user.Enabled,
                        (user.Services ?? [])
                            .Where(service => !string.IsNullOrWhiteSpace(service))
                            .Select(service => service.Trim())
                            .ToHashSet(StringComparer.OrdinalIgnoreCase));

                    foreach (var alias in (user.Aliases ?? []).Prepend(canonicalUser))
                    {
                        ValidateIdentity(alias, $"alias for {canonicalUser}");
                        if (aliases.TryGetValue(alias, out var existing) &&
                            !string.Equals(existing.CanonicalUser, canonicalUser, StringComparison.Ordinal))
                        {
                            throw new InvalidDataException($"alias {alias} is assigned more than once");
                        }

                        aliases[alias] = entry;
                    }
                }

                _aliases = aliases;
                _configurationError = null;
                _logger.LogInformation(
                    "MCP caller access file reloaded: {UserCount} canonical users",
                    configuration.Users.Count);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or YamlDotNet.Core.YamlException)
            {
                _aliases = new Dictionary<string, UserEntry>(StringComparer.OrdinalIgnoreCase);
                _configurationError = exception.Message;
                _logger.LogError(exception, "MCP caller access file is invalid or unavailable: {Path}", _path);
            }

            _stamp = stamp;
        }
    }

    private FileStamp GetFileStamp()
    {
        var file = new FileInfo(_path);
        return file.Exists
            ? new FileStamp(true, file.LastWriteTimeUtc.Ticks, file.Length)
            : new FileStamp(false, 0, 0);
    }

    private static bool IsValidIdentity(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        return value.All(character =>
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            return !char.IsWhiteSpace(character) && category is not UnicodeCategory.Control
                and not UnicodeCategory.Format
                and not UnicodeCategory.Surrogate;
        });
    }

    private static void ValidateIdentity(string value, string field)
    {
        if (!IsValidIdentity(value))
        {
            throw new InvalidDataException($"{field} must contain 1..128 non-padding characters without whitespace or controls");
        }
    }

    private sealed record FileStamp(bool Exists, long LastWriteTicks, long Length);

    private sealed record UserEntry(string CanonicalUser, bool Enabled, HashSet<string> Services);

    private sealed class AccessConfiguration
    {
        public int Version { get; set; }
        public Dictionary<string, UserConfiguration> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class UserConfiguration
    {
        public bool Enabled { get; set; }
        public List<string> Aliases { get; set; } = [];
        public List<string> Services { get; set; } = [];
    }
}
