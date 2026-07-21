using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.BuildingBlocks.Infrastructure.Search;

internal static class ApiConfigurationValidation
{
    private static readonly string[] PlaceholderFragments =
    [
        "change-me",
        "replace-with",
        "development-signing-key",
        "example-secret"
    ];

    internal static void Validate(WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var persistence = RequireKnown(configuration, "Persistence:Provider", "InMemory", "Mongo", "PostgreSql");
        var storage = StorageConfiguration.GetValidatedProvider(configuration);
        var search = RequireKnown(configuration, "Search:Provider", "InMemory", "OpenSearch");
        var distributedLock = RequireKnown(configuration, "DistributedLock:Provider", "InMemory", "Redis");
        var realtime = RequireKnown(configuration, "Realtime:Backplane", "InMemory", "Redis");
        var readModelCache = RequireKnown(configuration, "ReadModelCache:Provider", "InMemory", "Redis");
        var rateLimiting = RequireKnown(configuration, "RateLimiting:Provider", "InMemory", "Redis");
        _ = RequireKnown(configuration, "AttachmentSecurity:ScannerProvider", "PolicyOnly", "ClamAv");
        _ = RequireKnown(configuration, "Runtime:Role", "Api", "Worker");

        ValidateSelectedDependencies(configuration, persistence, search, distributedLock, realtime, readModelCache, rateLimiting);
        ValidateRateLimiting(configuration);
        ValidateRequestLimits(configuration);
        ValidateCors(configuration, requireAtLeastOneOrigin: !builder.Environment.IsDevelopment());

        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        var signingKeys = jwt.ResolveSigningKeys();
        _ = jwt.ResolveActiveSigningKey();

        if (builder.Environment.IsDevelopment())
        {
            return;
        }

        RequireProductionProvider(persistence, "Persistence:Provider", "Mongo", "PostgreSql");
        RequireProductionProvider(storage, "Storage:Provider", "Minio");
        RequireProductionProvider(search, "Search:Provider", "OpenSearch");
        RequireProductionProvider(distributedLock, "DistributedLock:Provider", "Redis");
        RequireProductionProvider(realtime, "Realtime:Backplane", "Redis");
        RequireProductionProvider(readModelCache, "ReadModelCache:Provider", "Redis");
        RequireProductionProvider(rateLimiting, "RateLimiting:Provider", "Redis");

        if (!configuration.GetValue("BrowserSession:SecureCookies", true))
        {
            throw new InvalidOperationException("BrowserSession:SecureCookies must be true outside Development.");
        }

        Require(configuration, "DataProtection:KeyPath");
        ValidateAllowedHosts(configuration);
        foreach (var key in signingKeys.Values)
        {
            if (PlaceholderFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Jwt signing keys must not use a documented placeholder or development key outside Development.");
            }
        }
    }

    private static void ValidateSelectedDependencies(
        IConfiguration configuration,
        string persistence,
        string search,
        string distributedLock,
        string realtime,
        string readModelCache,
        string rateLimiting)
    {
        if (persistence.Equals("Mongo", StringComparison.OrdinalIgnoreCase))
        {
            Require(configuration, "MongoDb:ConnectionString");
            Require(configuration, "MongoDb:DatabaseName");
        }
        else if (persistence.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            Require(configuration, "PostgreSql:ConnectionString");
        }

        if (search.Equals("OpenSearch", StringComparison.OrdinalIgnoreCase))
        {
            RequireHttpUrl(configuration, "Search:OpenSearch:BaseUrl");
            Require(configuration, "Search:OpenSearch:IndexName");
            OpenSearchWorkItemSearchIndex.ValidateConfiguration(
                configuration.GetSection("Search:OpenSearch").Get<OpenSearchOptions>() ?? new OpenSearchOptions());
        }

        var searchOptions = configuration.GetSection("Search").Get<SearchOptions>() ?? new SearchOptions();
        if (searchOptions.DegradedFallbackMaxItems is < 1 or > 10_000)
            throw new InvalidOperationException("Search degraded fallback limit must be between 1 and 10000.");

        if (distributedLock.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            Require(configuration, "DistributedLock:Redis:ConnectionString");
        }

        if (realtime.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            var connection = configuration["Realtime:Redis:ConnectionString"]
                ?? configuration["DistributedLock:Redis:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connection))
            {
                throw new InvalidOperationException("Realtime Redis requires a configured Redis connection string.");
            }
        }

        if (readModelCache.Equals("Redis", StringComparison.OrdinalIgnoreCase)
            && !distributedLock.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ReadModelCache:Provider=Redis requires DistributedLock:Provider=Redis so a shared connection is configured.");
        }

        if (rateLimiting.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            var rateConnection = Require(configuration, "RateLimiting:Redis:ConnectionString");
            if (!distributedLock.Equals("Redis", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "RateLimiting:Provider=Redis requires DistributedLock:Provider=Redis so a shared connection is configured.");
            }

            var lockConnection = Require(configuration, "DistributedLock:Redis:ConnectionString");
            if (!rateConnection.Equals(lockConnection, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "RateLimiting and DistributedLock Redis connection strings must match so one shared multiplexer is used.");
            }
        }
    }

    private static void ValidateRateLimiting(IConfiguration configuration)
    {
        var options = configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        var limits = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(options.LoginPermitLimit)] = options.LoginPermitLimit,
            [nameof(options.PasswordResetPermitLimit)] = options.PasswordResetPermitLimit,
            [nameof(options.ApiPermitLimit)] = options.ApiPermitLimit,
            [nameof(options.SearchPermitLimit)] = options.SearchPermitLimit,
            [nameof(options.UploadPermitLimit)] = options.UploadPermitLimit,
            [nameof(options.ReportPermitLimit)] = options.ReportPermitLimit,
            [nameof(options.BulkPermitLimit)] = options.BulkPermitLimit,
            [nameof(options.RealtimeConnectPermitLimit)] = options.RealtimeConnectPermitLimit
        };
        if (limits.Any(entry => entry.Value <= 0))
        {
            throw new InvalidOperationException("Every RateLimiting permit limit must be greater than zero.");
        }

        if (options.StandardWindowSeconds is < 1 or > 3600
            || options.PasswordResetWindowSeconds is < 1 or > 86400)
        {
            throw new InvalidOperationException("RateLimiting windows must be positive and bounded.");
        }

        if (options.Redis.OperationTimeoutMilliseconds is < 50 or > 5000
            || string.IsNullOrWhiteSpace(options.Redis.KeyPrefix)
            || options.Redis.KeyPrefix.Length > 64)
        {
            throw new InvalidOperationException("RateLimiting Redis timeout and key prefix must be bounded.");
        }
    }

    private static void ValidateRequestLimits(IConfiguration configuration)
    {
        var limits = configuration.GetSection("RequestLimits").Get<RequestLimitsOptions>() ?? new RequestLimitsOptions();
        if (limits.MaxRequestBodyBytes is < 1_048_576 or > 104_857_600
            || limits.MaxHeaderCount is < 10 or > 200
            || limits.MaxHeaderBytes is < 8192 or > 131_072
            || limits.MaxQueryStringBytes is < 1024 or > 32_768
            || limits.MaxQueryParameters is < 5 or > 100
            || limits.MaxQueryValueCharacters is < 128 or > 8192
            || limits.MaxPage is < 100 or > 1_000_000
            || limits.MaxPageSize is < 10 or > 500)
        {
            throw new InvalidOperationException("RequestLimits values must remain inside the supported abuse-protection bounds.");
        }
    }

    private static void ValidateCors(IConfiguration configuration, bool requireAtLeastOneOrigin)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (requireAtLeastOneOrigin && origins.Length == 0)
        {
            throw new InvalidOperationException("Cors:AllowedOrigins requires at least one exact origin outside Development.");
        }

        if (origins.Distinct(StringComparer.Ordinal).Count() != origins.Length)
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must not contain duplicate origins.");
        }

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(uri.PathAndQuery.Trim('/'))
                || !string.IsNullOrEmpty(uri.Fragment)
                || origin.EndsWith('/'))
            {
                throw new InvalidOperationException($"Cors origin '{origin}' must be an exact HTTP(S) origin without a path or trailing slash.");
            }
        }
    }

    private static void ValidateAllowedHosts(IConfiguration configuration)
    {
        var hosts = (configuration["AllowedHosts"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Contains("*", StringComparer.Ordinal))
        {
            throw new InvalidOperationException("AllowedHosts must contain explicit hosts and must not contain a wildcard outside Development.");
        }
    }

    private static string RequireKnown(IConfiguration configuration, string key, params string[] allowed)
    {
        var value = Require(configuration, key);
        if (!allowed.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{key} has unsupported value '{value}'. Expected one of: {string.Join(", ", allowed)}.");
        }

        return allowed.Single(candidate => candidate.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static void RequireProductionProvider(string actual, string key, params string[] allowed)
    {
        if (!allowed.Contains(actual, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{key}={actual} is not allowed outside Development.");
        }
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} must be configured.");
        }

        return value;
    }

    private static void RequireHttpUrl(IConfiguration configuration, string key)
    {
        var value = Require(configuration, key);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"{key} must be an absolute HTTP(S) URL.");
        }
    }
}
