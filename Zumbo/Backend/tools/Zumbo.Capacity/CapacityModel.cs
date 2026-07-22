using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Zumbo.Capacity;

internal sealed record CapacityProfile(
    string Name,
    string RunId,
    int OrganizationCount,
    int UserCount,
    int ProjectCount,
    int WorkItemCount,
    int ActivityEventCount,
    int RealtimeClientCount)
{
    private static readonly Regex SafeRunId = new("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$", RegexOptions.Compiled);

    public string Prefix => $"capacity-{Name}-{RunId}-";

    public DateTimeOffset SeedTimestamp
    {
        get
        {
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{Name}:{RunId}"));
            var offsetSeconds = BitConverter.ToUInt32(digest, 0) % (365u * 24u * 60u * 60u);
            return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(offsetSeconds);
        }
    }

    public static CapacityProfile Resolve(string name, string runId = "manual")
    {
        var normalizedRunId = runId.Trim().ToLowerInvariant();
        if (!SafeRunId.IsMatch(normalizedRunId))
        {
            throw new ArgumentException("Run id must be 1-40 lowercase alphanumeric/hyphen characters without edge hyphens.");
        }

        return name.ToLowerInvariant() switch
        {
            "smoke" => new("smoke", normalizedRunId, 2, 20, 5, 500, 1_000, 20),
            "demo" => new("demo", normalizedRunId, 5, 200, 50, 25_000, 100_000, 75),
            "performance" => new("performance", normalizedRunId, 5, 200, 50, 100_000, 1_000_000, 200),
            _ => throw new ArgumentException("Profile must be smoke, demo or performance.")
        };
    }
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
    public static string Username(CapacityProfile profile, int index) =>
        $"capacity-{profile.Name}-{profile.RunId}-user-{index:D6}";
}

internal sealed record SeedResult(
    string Profile,
    string RunId,
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
    int Successes,
    int Errors,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaximumMilliseconds,
    double ThroughputPerSecond,
    double ErrorRate,
    double BudgetMilliseconds,
    double P99BudgetMilliseconds,
    double MaximumErrorRate,
    bool Passed);

internal sealed record RealtimeResult(
    int RequestedClients,
    int ConnectedClients,
    int ReceivedClients,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
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

internal sealed record ScenarioDefinition(
    string Name,
    TimeSpan Duration,
    int Concurrency,
    double TargetRequestsPerSecond,
    double P95BudgetMilliseconds,
    double P99BudgetMilliseconds,
    double MaximumErrorRate,
    double MinimumThroughputPerSecond);

internal sealed record OperationResult(
    string Name,
    int Requests,
    int Successes,
    int Errors,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaximumMilliseconds,
    double ThroughputPerSecond,
    double ErrorRate);

internal sealed record OutboxSnapshot(
    long Pending,
    long Processing,
    long Completed,
    long Retried,
    long DeadLetter,
    double? OldestPendingAgeSeconds);

internal sealed record ResourceSnapshot(
    double ClientCpuSeconds,
    long ClientWorkingSetBytes,
    long ClientPeakWorkingSetBytes,
    long ManagedMemoryBytes,
    long DiskFreeBytes);

internal sealed record ScenarioResult(
    string Name,
    DateTimeOffset StartedAtUtc,
    double DurationSeconds,
    int Concurrency,
    double TargetRequestsPerSecond,
    int Requests,
    int Successes,
    int Errors,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaximumMilliseconds,
    double ThroughputPerSecond,
    double ErrorRate,
    IReadOnlyList<OperationResult> Operations,
    OutboxSnapshot OutboxBefore,
    OutboxSnapshot OutboxAfter,
    ResourceSnapshot Resources,
    double P95BudgetMilliseconds,
    double P99BudgetMilliseconds,
    double MaximumErrorRate,
    double MinimumThroughputPerSecond,
    bool Passed);

internal sealed record QueryPlanResult(
    string Name,
    string ExpectedIndex,
    bool IndexUsed,
    bool CollectionScan,
    long DocumentsExamined,
    long DocumentsReturned,
    long ExecutionMilliseconds,
    long MaximumDocumentsExamined,
    long MaximumExecutionMilliseconds,
    bool Passed);

internal sealed record CleanupResult(
    string Profile,
    string RunId,
    string Prefix,
    long RemainingMongoDocuments,
    long RemainingSearchDocuments,
    bool Passed);

internal sealed record DegradedResult(
    string Profile,
    string RunId,
    int Requests,
    int SafeResponses,
    int UnsafeResponses,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaximumMilliseconds,
    string DependencyStatus,
    bool Passed);

internal sealed record DiskUsageResult(
    long BeforeSeedFreeBytes,
    long AfterSeedFreeBytes,
    long AfterCleanupFreeBytes,
    long BeforeSeedStorageBytes,
    long AfterSeedStorageBytes,
    long AfterCleanupStorageBytes,
    long DatasetBytes,
    long MaximumDatasetBytes,
    bool Passed);

internal sealed record CapacityGateResult(
    DateTimeOffset RecordedAtUtc,
    string Profile,
    string RunId,
    string Prefix,
    ReferenceMachine Machine,
    SeedResult? Seed,
    BenchmarkResult? Benchmark,
    IReadOnlyList<ScenarioResult> Scenarios,
    IReadOnlyList<QueryPlanResult> QueryPlans,
    DiskUsageResult Disk,
    CleanupResult Cleanup,
    string? ErrorStage,
    string? ErrorType,
    bool Passed);

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
