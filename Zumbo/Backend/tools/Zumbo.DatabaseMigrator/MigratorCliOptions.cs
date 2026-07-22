namespace Zumbo.DatabaseMigrator;

internal enum MigratorCommand
{
    Status,
    Apply,
    Rollback,
    Script
}

internal sealed record MigratorCliOptions(
    MigratorCommand Command,
    string ConnectionString,
    long? TargetVersion,
    long? FromVersion,
    long? ToVersion,
    bool Idempotent,
    string? OutputPath)
{
    private const string PrimaryConnectionStringEnvironment = "ZUMBO_POSTGRES_CONNECTION_STRING";
    private const string StandardConnectionStringEnvironment = "ConnectionStrings__PostgreSql";

    public static MigratorCliOptions Parse(string[] args, Func<string, string?> readEnvironment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(readEnvironment);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            throw new MigratorUsageException(null);
        }

        var command = ParseCommand(args[0]);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (IsHelp(option))
            {
                throw new MigratorUsageException(null);
            }

            var normalizedOption = option.ToLowerInvariant();
            if (normalizedOption == "--idempotent")
            {
                EnsureUnique(flags.Add(normalizedOption), option);
                continue;
            }

            if (normalizedOption is not ("--connection-string" or "--target-version" or "--from-version" or "--to-version" or "--output"))
            {
                throw new MigratorUsageException($"Unknown option '{option}'.");
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new MigratorUsageException($"Option '{option}' requires a value.");
            }

            EnsureUnique(values.TryAdd(normalizedOption, args[++index]), option);
        }

        var connectionString = ReadConnectionString(values, readEnvironment);
        var options = new MigratorCliOptions(
            command,
            connectionString,
            ReadVersion(values, "--target-version"),
            ReadVersion(values, "--from-version"),
            ReadVersion(values, "--to-version"),
            flags.Contains("--idempotent"),
            ReadOptional(values, "--output"));

        ValidateCommandOptions(options);
        return options;
    }

    private static MigratorCommand ParseCommand(string value) => value.ToLowerInvariant() switch
    {
        "status" => MigratorCommand.Status,
        "apply" => MigratorCommand.Apply,
        "rollback" => MigratorCommand.Rollback,
        "script" => MigratorCommand.Script,
        _ => throw new MigratorUsageException($"Unknown command '{value}'.")
    };

    private static string ReadConnectionString(
        IReadOnlyDictionary<string, string> values,
        Func<string, string?> readEnvironment)
    {
        var configured = ReadOptional(values, "--connection-string")
            ?? Normalize(readEnvironment(PrimaryConnectionStringEnvironment))
            ?? Normalize(readEnvironment(StandardConnectionStringEnvironment));

        return configured
            ?? throw new MigratorUsageException(
                $"A PostgreSQL connection string is required via --connection-string, {PrimaryConnectionStringEnvironment}, or {StandardConnectionStringEnvironment}.");
    }

    private static void ValidateCommandOptions(MigratorCliOptions options)
    {
        switch (options.Command)
        {
            case MigratorCommand.Status:
                Reject(options, "--target-version", options.TargetVersion);
                Reject(options, "--from-version", options.FromVersion);
                Reject(options, "--to-version", options.ToVersion);
                Reject(options, "--idempotent", options.Idempotent ? "true" : null);
                Reject(options, "--output", options.OutputPath);
                break;
            case MigratorCommand.Apply:
                Reject(options, "--target-version", options.TargetVersion);
                Reject(options, "--from-version", options.FromVersion);
                Reject(options, "--to-version", options.ToVersion);
                Reject(options, "--idempotent", options.Idempotent ? "true" : null);
                Reject(options, "--output", options.OutputPath);
                break;
            case MigratorCommand.Rollback:
                if (options.TargetVersion is null)
                {
                    throw new MigratorUsageException("Rollback requires --target-version <version>.");
                }
                Reject(options, "--from-version", options.FromVersion);
                Reject(options, "--to-version", options.ToVersion);
                Reject(options, "--idempotent", options.Idempotent ? "true" : null);
                Reject(options, "--output", options.OutputPath);
                break;
            case MigratorCommand.Script:
                Reject(options, "--target-version", options.TargetVersion);
                if (options.FromVersion is { } fromVersion
                    && options.ToVersion is { } toVersion
                    && fromVersion > toVersion)
                {
                    throw new MigratorUsageException("--from-version cannot be greater than --to-version.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options.Command));
        }
    }

    private static string? ReadOptional(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value)
            ? Normalize(value) ?? throw new MigratorUsageException($"Option '{key}' cannot be empty.")
            : null;

    private static long? ReadVersion(IReadOnlyDictionary<string, string> values, string key)
    {
        var value = ReadOptional(values, key);
        if (value is null)
        {
            return null;
        }

        if (!long.TryParse(value, out var version) || version < 0)
        {
            throw new MigratorUsageException($"Option '{key}' requires a non-negative integer.");
        }

        return version;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureUnique(bool added, string option)
    {
        if (!added)
        {
            throw new MigratorUsageException($"Option '{option}' can only be specified once.");
        }
    }

    private static void Reject(MigratorCliOptions options, string name, object? value)
    {
        if (value is not null)
        {
            throw new MigratorUsageException($"Command '{options.Command.ToString().ToLowerInvariant()}' does not accept {name}.");
        }
    }

    private static bool IsHelp(string value) => value is "-h" or "--help" or "help";
}

internal sealed class MigratorUsageException : Exception
{
    public MigratorUsageException(string? usageError)
        : base(usageError ?? string.Empty)
    {
        UsageError = usageError;
    }

    public string? UsageError { get; }

    public const string Usage = """
        Usage:
          Zumbo.DatabaseMigrator status   [--connection-string <value>]
          Zumbo.DatabaseMigrator apply    [--connection-string <value>]
          Zumbo.DatabaseMigrator rollback --target-version <version> [--connection-string <value>]
          Zumbo.DatabaseMigrator script   [--from-version <version>] [--to-version <version>] [--idempotent] [--output <path>] [--connection-string <value>]

        Connection string environment variables, in precedence order after the argument:
          ZUMBO_POSTGRES_CONNECTION_STRING
          ConnectionStrings__PostgreSql
        """;
}
