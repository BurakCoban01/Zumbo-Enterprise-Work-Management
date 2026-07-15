using System.Text.Json.Serialization;

namespace Zumbo.Capacity;

internal sealed record CapacityProfile(
    string Name,
    int OrganizationCount,
    int UserCount,
    int ProjectCount,
    int WorkItemCount,
    int ActivityEventCount,
    int RealtimeClientCount)
{
    public string Prefix => $"capacity-{Name}-";

    public static CapacityProfile Resolve(string name) => name.ToLowerInvariant() switch
    {
        "smoke" => new("smoke", 2, 20, 5, 500, 1_000, 100),
        "demo" => new("demo", 5, 200, 50, 25_000, 100_000, 150),
        "performance" => new("performance", 5, 200, 50, 100_000, 1_000_000, 200),
        _ => throw new ArgumentException("Profile must be smoke, demo or performance.")
    };
}

internal static class CapacityIds
{
    public static string Organization(CapacityProfile profile, int index) => $"{profile.Prefix}org-{index:D3}";
    public static string User(CapacityProfile profile, int index) => $"{profile.Prefix}user-{index:D6}";
    public static string Project(CapacityProfile profile, int index) => $"{profile.Prefix}project-{index:D4}";
    public static string Board(CapacityProfile profile, int index) => $"{profile.Prefix}board-{index:D4}";
    public static string Column(CapacityProfile profile, int projectIndex, int columnIndex) =>
        $"{profile.Prefix}column-{projectIndex:D4}-{columnIndex:D2}";
    public static string WorkItem(CapacityProfile profile, int index) => $"{profile.Prefix}workitem-{index:D8}";
    public static string Audit(CapacityProfile profile, int index) => $"{profile.Prefix}audit-{index:D9}";
    public static string Username(CapacityProfile profile, int index) => $"capacity-{profile.Name}-user-{index:D6}";
}

internal sealed record SeedResult(
    string Profile,
    string Prefix,
    int Organizations,
    int Users,
    int Projects,
    int Boards,
    int WorkItems,
    int ActivityEvents,
    long ElapsedMilliseconds,
    string BenchmarkUsername);

internal sealed record MetricResult(
    string Name,
    int Samples,
    double P50Milliseconds,
    double P95Milliseconds,
    double MaximumMilliseconds,
    double BudgetMilliseconds,
    bool Passed);

internal sealed record RealtimeResult(
    int RequestedClients,
    int ConnectedClients,
    int ReceivedClients,
    double P95Milliseconds,
    double MaximumMilliseconds,
    double BudgetMilliseconds,
    int MaximumPayloadBytes,
    int PayloadBudgetBytes,
    bool Passed);

internal sealed record DatasetCounts(long WorkItems, long ActivityEvents);

internal sealed record ReferenceMachine(
    string MachineName,
    string OperatingSystem,
    string Architecture,
    int LogicalProcessors,
    long GcAvailableMemoryBytes);

internal sealed record BenchmarkResult(
    DateTimeOffset RecordedAt,
    string Profile,
    string ApiBaseUrl,
    ReferenceMachine Machine,
    DatasetCounts Dataset,
    IReadOnlyList<MetricResult> Metrics,
    RealtimeResult Realtime,
    bool AllBudgetsPassed);

internal sealed class ApiEnvelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

internal sealed class LoginPayload
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;
}

internal sealed class WorkItemPayload
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

internal sealed class NegotiatePayload
{
    [JsonPropertyName("connectionToken")]
    public string ConnectionToken { get; set; } = string.Empty;
}
