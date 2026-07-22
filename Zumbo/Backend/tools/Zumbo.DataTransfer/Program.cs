using System.Text.Json;
using Zumbo.DataTransfer;

try
{
    var arguments = CliArguments.Parse(args);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    var target = new ProviderTarget(
        arguments.Provider,
        Environment.GetEnvironmentVariable(arguments.ConnectionEnvironment)
            ?? throw new InvalidOperationException($"Connection environment variable '{arguments.ConnectionEnvironment}' is not set."),
        arguments.Database);

    switch (arguments.Command)
    {
        case "export":
            var manifest = await TransferEngine.ExportAsync(target, arguments.Bundle, cancellation.Token);
            Write(new { result = "exported", provider = target.Provider, datasets = manifest.Datasets.Count, count = manifest.Datasets.Sum(x => x.Count) });
            break;
        case "import":
            await TransferEngine.ImportAsync(target, arguments.Bundle, arguments.DryRun, arguments.FailAfter, cancellation.Token);
            Write(new { result = arguments.DryRun ? "dry-run" : "imported", provider = target.Provider });
            break;
        case "verify":
            await TransferEngine.ValidateBundleAsync(arguments.Bundle, cancellation.Token);
            await TransferEngine.VerifyAsync(target, arguments.Bundle, cancellation.Token);
            Write(new { result = "verified", provider = target.Provider });
            break;
        default:
            throw new ArgumentException("Command must be export, import, or verify.");
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { result = "failed", error = exception.Message }));
    return 1;
}

static void Write(object value) =>
    Console.WriteLine(JsonSerializer.Serialize(value));

internal sealed record CliArguments(
    string Command,
    string Provider,
    string ConnectionEnvironment,
    string? Database,
    string Bundle,
    bool DryRun,
    int? FailAfter)
{
    internal static CliArguments Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: Zumbo.DataTransfer <export|import|verify> --provider <mongo|postgresql> --connection-env <ENV_NAME> [--database <mongo-db>] --bundle <new-or-existing-path> [--dry-run] [--fail-after <N>]");
            Environment.Exit(0);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--dry-run") { flags.Add(argument); continue; }
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException($"Invalid argument '{argument}'.");
            values[argument] = args[++index];
        }

        var failAfter = values.TryGetValue("--fail-after", out var rawFailure)
            ? int.Parse(rawFailure, System.Globalization.CultureInfo.InvariantCulture)
            : (int?)null;
        if (failAfter is <= 0) throw new ArgumentOutOfRangeException("--fail-after");
        return new(
            args[0].Trim().ToLowerInvariant(),
            Required(values, "--provider"),
            Required(values, "--connection-env"),
            values.GetValueOrDefault("--database"),
            Required(values, "--bundle"),
            flags.Contains("--dry-run"),
            failAfter);
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Required option '{name}' is missing.");
}
