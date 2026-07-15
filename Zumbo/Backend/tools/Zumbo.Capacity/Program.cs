using System.Text.Json;
using Zumbo.Capacity;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: Zumbo.Capacity <seed|benchmark|clean> <smoke|demo|performance> [--samples N] [--clients N]");
    return 2;
}

try
{
    var command = args[0].ToLowerInvariant();
    var profile = CapacityProfile.Resolve(args[1]);
    var mongo = Environment.GetEnvironmentVariable("ZUMBO_MONGO") ?? "mongodb://localhost:27017";
    var openSearch = Environment.GetEnvironmentVariable("ZUMBO_OPENSEARCH") ?? "http://localhost:9200";
    var api = Environment.GetEnvironmentVariable("ZUMBO_API") ?? "http://localhost:5088";
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };

    object result = command switch
    {
        "seed" => await new SeedRunner(mongo, openSearch).RunAsync(profile, CancellationToken.None),
        "clean" => await new SeedRunner(mongo, openSearch).CleanAsync(profile, CancellationToken.None),
        "benchmark" => await new BenchmarkRunner(mongo, api).RunAsync(
            profile,
            ReadIntOption(args, "--samples", 20, 10, 20),
            ReadIntOption(args, "--clients", profile.RealtimeClientCount, 1, 250),
            CancellationToken.None),
        _ => throw new ArgumentException("Command must be seed, benchmark or clean.")
    };

    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
    return 0;
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
