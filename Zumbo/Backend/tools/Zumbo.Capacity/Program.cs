using System.Text.Json;
using Zumbo.Capacity;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Zumbo.Capacity <seed|benchmark|query-plan|degraded|gate|clean> <smoke|demo|performance> --run-id <id> [--samples N] [--clients N]");
    return 2;
}

try
{
    var command = args[0].ToLowerInvariant();
    var profile = CapacityProfile.Resolve(args[1], ReadStringOption(args, "--run-id", "manual"));
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    var password = command is "query-plan" or "clean"
        ? Environment.GetEnvironmentVariable("ZUMBO_CAPACITY_PASSWORD") ?? "not-used"
        : RequireEnvironment("ZUMBO_CAPACITY_PASSWORD");

    object result = command switch
    {
        "seed" => await CreateSeedRunner(password).RunAsync(profile, cancellation.Token),
        "clean" => await CreateSeedRunner(password).CleanAsync(profile, cancellation.Token),
        "benchmark" => await new BenchmarkRunner(
                RequireEnvironment("ZUMBO_MONGO_URL"),
                RequireEnvironment("ZUMBO_API_URL"),
                password)
            .RunAsync(
            profile,
            ReadIntOption(args, "--samples", 20, 10, 20),
            ReadIntOption(args, "--clients", profile.RealtimeClientCount, 1, 250),
            cancellation.Token),
        "query-plan" => await new QueryPlanRunner(RequireEnvironment("ZUMBO_MONGO_URL"))
            .RunAsync(profile, cancellation.Token),
        "degraded" => await new ScenarioRunner(
                RequireEnvironment("ZUMBO_MONGO_URL"),
                RequireEnvironment("ZUMBO_API_URL"),
                password)
            .RunDegradedAsync(profile, ReadIntOption(args, "--samples", 12, 10, 50), cancellation.Token),
        "gate" => await new CapacityGateRunner(
                RequireEnvironment("ZUMBO_MONGO_URL"),
                RequireEnvironment("ZUMBO_OPENSEARCH_URL"),
                RequireEnvironment("ZUMBO_API_URL"),
                password)
            .RunAsync(profile, cancellation.Token),
        _ => throw new ArgumentException("Command must be seed, benchmark, query-plan, degraded, gate or clean.")
    };

    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
    return result switch
    {
        CleanupResult cleanup when !cleanup.Passed => 1,
        BenchmarkResult benchmark when !benchmark.AllBudgetsPassed => 1,
        DegradedResult degraded when !degraded.Passed => 1,
        CapacityGateResult gate when !gate.Passed => 1,
        IReadOnlyList<QueryPlanResult> plans when plans.Any(x => !x.Passed) => 1,
        _ => 0
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static int ReadIntOption(string[] values, string name, int defaultValue, int minimum, int maximum)
{
    var index = Array.FindIndex(values, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
        return defaultValue;
    }
    if (index + 1 >= values.Length || !int.TryParse(values[index + 1], out var parsed))
    {
        throw new ArgumentException($"{name} requires an integer value.");
    }
    return Math.Clamp(parsed, minimum, maximum);
}

static string ReadStringOption(string[] values, string name, string defaultValue)
{
    var index = Array.FindIndex(values, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
        return defaultValue;
    }
    if (index + 1 >= values.Length || string.IsNullOrWhiteSpace(values[index + 1]))
    {
        throw new ArgumentException($"{name} requires a value.");
    }
    return values[index + 1];
}

static string RequireEnvironment(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"{name} must be configured for the capacity tool.");

static SeedRunner CreateSeedRunner(string password) => new(
    RequireEnvironment("ZUMBO_MONGO_URL"),
    RequireEnvironment("ZUMBO_OPENSEARCH_URL"),
    password);
