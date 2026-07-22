using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Zumbo.Capacity;

internal sealed class ScenarioRunner(string mongoConnectionString, string apiBaseUrl, string capacityPassword)
{
    private readonly MongoClient _mongo = new(mongoConnectionString);
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static IReadOnlyList<ScenarioDefinition> Definitions(CapacityProfile profile) => profile.Name switch
    {
        "smoke" =>
        [
            new("load", TimeSpan.FromSeconds(6), 4, 4, 1_000, 3_000, 0.01, 2),
            new("spike", TimeSpan.FromSeconds(4), 12, 12, 3_000, 7_000, 0.05, 5),
            new("soak", TimeSpan.FromSeconds(20), 3, 3, 3_000, 7_000, 0.01, 1.5)
        ],
        "demo" =>
        [
            new("load", TimeSpan.FromSeconds(20), 8, 8, 500, 1_000, 0.01, 5),
            new("spike", TimeSpan.FromSeconds(10), 24, 24, 1_000, 2_000, 0.05, 12),
            new("soak", TimeSpan.FromMinutes(2), 5, 5, 750, 1_500, 0.01, 3)
        ],
        _ =>
        [
            new("load", TimeSpan.FromSeconds(30), 16, 12, 500, 1_000, 0.01, 8),
            new("spike", TimeSpan.FromSeconds(15), 48, 30, 1_000, 2_000, 0.05, 15),
            new("soak", TimeSpan.FromMinutes(5), 8, 6, 750, 1_500, 0.01, 4)
        ]
    };

    public async Task<ScenarioResult> RunAsync(
        CapacityProfile profile,
        ScenarioDefinition definition,
        CancellationToken ct)
    {
        await AuthenticateAsync(profile, ct);
        var outboxBefore = await ReadOutboxAsync(profile, ct);
        var observations = new ConcurrentQueue<Observation>();
        var sequence = 0L;
        var startedAt = DateTimeOffset.UtcNow;
        var overall = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var delay = TimeSpan.FromSeconds(definition.Concurrency / definition.TargetRequestsPerSecond);

        var workers = Enumerable.Range(0, definition.Concurrency).Select(worker => Task.Run(async () =>
        {
            while (overall.Elapsed < definition.Duration && !ct.IsCancellationRequested)
            {
                var current = Interlocked.Increment(ref sequence) - 1;
                var operation = CapacityMath.OperationFor(current);
                var stopwatch = Stopwatch.StartNew();
                var success = false;
                try
                {
                    success = await ExecuteAsync(profile, operation, worker, current, ct);
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or IOException or JsonException
                    || (exception is TaskCanceledException && !ct.IsCancellationRequested))
                {
                    success = false;
                }
                finally
                {
                    stopwatch.Stop();
                    observations.Enqueue(new Observation(operation, stopwatch.Elapsed.TotalMilliseconds, success));
                }

                var remaining = delay - stopwatch.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, ct);
                }
            }
        }, ct)).ToArray();

        await Task.WhenAll(workers);
        overall.Stop();
        process.Refresh();
        var results = observations.ToArray();
        var timings = results.Select(x => x.Milliseconds).Order().ToArray();
        var successes = results.Count(x => x.Success);
        var errors = results.Length - successes;
        var throughput = results.Length / Math.Max(0.001, overall.Elapsed.TotalSeconds);
        var errorRate = results.Length == 0 ? 1 : (double)errors / results.Length;
        var operations = results
            .GroupBy(x => x.Operation, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => ToOperation(group.Key, group.ToArray(), overall.Elapsed.TotalSeconds))
            .ToArray();
        var outboxAfter = await ReadOutboxAsync(profile, ct);
        var resources = CapacityMath.ResourceSince(process, cpuBefore, CapacityMath.GetDiskFreeBytes());
        var p95 = CapacityMath.Percentile(timings, 0.95);
        var p99 = CapacityMath.Percentile(timings, 0.99);
        var passed = results.Length > 0
            && p95 <= definition.P95BudgetMilliseconds
            && p99 <= definition.P99BudgetMilliseconds
            && errorRate <= definition.MaximumErrorRate
            && throughput >= definition.MinimumThroughputPerSecond;
        return new ScenarioResult(
            definition.Name,
            startedAt,
            overall.Elapsed.TotalSeconds,
            definition.Concurrency,
            definition.TargetRequestsPerSecond,
            results.Length,
            successes,
            errors,
            CapacityMath.Percentile(timings, 0.50),
            p95,
            p99,
            timings.Length == 0 ? 0 : timings[^1],
            throughput,
            errorRate,
            operations,
            outboxBefore,
            outboxAfter,
            resources,
            definition.P95BudgetMilliseconds,
            definition.P99BudgetMilliseconds,
            definition.MaximumErrorRate,
            definition.MinimumThroughputPerSecond,
            passed);
    }

    public async Task<DegradedResult> RunDegradedAsync(CapacityProfile profile, int requests, CancellationToken ct)
    {
        await AuthenticateAsync(profile, ct);
        var timings = new List<double>(requests);
        var safe = 0;
        for (var index = 0; index < requests; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await _http.GetAsync(
                $"api/work-items?projectId={Uri.EscapeDataString(CapacityIds.Project(profile, 0))}&text=capacity",
                ct);
            stopwatch.Stop();
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode
                || ((int)response.StatusCode == 503
                    && !body.Contains("password", StringComparison.OrdinalIgnoreCase)
                    && !body.Contains("connectionstring", StringComparison.OrdinalIgnoreCase)))
            {
                safe++;
            }
        }

        using var statusResponse = await _http.GetAsync("api/operations/external-dependencies", ct);
        statusResponse.EnsureSuccessStatusCode();
        using var status = JsonDocument.Parse(await statusResponse.Content.ReadAsStreamAsync(ct));
        var dependencyStatus = status.RootElement.GetProperty("status").GetString() ?? "unknown";
        timings.Sort();
        return new DegradedResult(
            profile.Name,
            profile.RunId,
            requests,
            safe,
            requests - safe,
            CapacityMath.Percentile(timings, 0.50),
            CapacityMath.Percentile(timings, 0.95),
            CapacityMath.Percentile(timings, 0.99),
            timings.Count == 0 ? 0 : timings[^1],
            dependencyStatus,
            safe == requests && dependencyStatus == "degraded");
    }

    private async Task AuthenticateAsync(CapacityProfile profile, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("api/auth/login", new
        {
            usernameOrEmail = CapacityIds.Username(profile, 0),
            password = capacityPassword
        }, ct);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LoginPayload>>(cancellationToken: ct);
        var token = envelope?.Data?.AccessToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Capacity login response did not contain an access token.");
        }
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<bool> ExecuteAsync(
        CapacityProfile profile,
        string operation,
        int worker,
        long sequence,
        CancellationToken ct)
    {
        var projectId = CapacityIds.Project(profile, 0);
        var perProject = Math.Max(1, profile.WorkItemCount / profile.ProjectCount);
        var projectOffset = (int)((sequence + worker * 31L) % perProject);
        var workItemId = CapacityIds.WorkItem(profile, projectOffset * profile.ProjectCount);
        return operation switch
        {
            "read" => await IsSuccessAsync(await _http.GetAsync($"api/work-items/{workItemId}", ct), ct),
            "search" => await IsSuccessAsync(await _http.GetAsync($"api/work-items?projectId={Uri.EscapeDataString(projectId)}&text=capacity", ct), ct),
            "report" => await IsSuccessAsync(await _http.GetAsync($"api/work-items/reports/project-summary/{projectId}", ct), ct),
            "write" => await IsSuccessAsync(await _http.PatchAsJsonAsync(
                $"api/work-items/{workItemId}/planning",
                new { sprintId = (string?)null, estimatePoints = (int)(sequence % 13) + 1 },
                ct), ct),
            "external" => await IsSuccessAsync(await _http.GetAsync("api/operations/external-dependencies", ct), ct),
            "upload" => await UploadAndDeleteAsync(profile, workItemId, sequence, ct),
            _ => throw new InvalidOperationException($"Unknown capacity operation '{operation}'.")
        };
    }

    private async Task<bool> UploadAndDeleteAsync(CapacityProfile profile, string workItemId, long sequence, CancellationToken ct)
    {
        using var multipart = new MultipartFormDataContent();
        var payload = Encoding.UTF8.GetBytes($"capacity {profile.RunId} {sequence:D8}\n".PadRight(1_024, 'x'));
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        multipart.Add(content, "file", $"capacity-{sequence:D8}.txt");
        using var upload = await _http.PostAsync($"api/work-items/{workItemId}/attachments/upload", multipart, ct);
        if (!upload.IsSuccessStatusCode)
        {
            return false;
        }

        using var response = JsonDocument.Parse(await upload.Content.ReadAsStreamAsync(ct));
        var attachments = response.RootElement.GetProperty("data").GetProperty("attachments");
        if (attachments.GetArrayLength() == 0)
        {
            return false;
        }
        var attachmentId = attachments[attachments.GetArrayLength() - 1].GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(attachmentId))
        {
            return false;
        }
        using var delete = await _http.DeleteAsync($"api/work-items/{workItemId}/attachments/{attachmentId}", ct);
        return delete.IsSuccessStatusCode;
    }

    private static async Task<bool> IsSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _ = await response.Content.ReadAsStringAsync(ct);
                return false;
            }
            return true;
        }
    }

    private async Task<OutboxSnapshot> ReadOutboxAsync(CapacityProfile profile, CancellationToken ct)
    {
        var collection = _mongo.GetDatabase("ZumboWorkItems").GetCollection<BsonDocument>("outbox_messages");
        var tenant = Builders<BsonDocument>.Filter.Regex("TenantId", new BsonRegularExpression("^" + profile.Prefix));
        async Task<long> Count(string state) => await collection.CountDocumentsAsync(
            tenant & Builders<BsonDocument>.Filter.Eq("Status", state), cancellationToken: ct);
        var oldest = await collection.Find(tenant & Builders<BsonDocument>.Filter.Eq("Status", "Pending"))
            .Sort(Builders<BsonDocument>.Sort.Ascending("OccurredAtUtc"))
            .Limit(1)
            .FirstOrDefaultAsync(ct);
        double? age = null;
        if (oldest is not null && oldest.TryGetValue("OccurredAtUtc", out var occurred) && occurred.IsValidDateTime)
        {
            age = Math.Max(0, (DateTime.UtcNow - occurred.ToUniversalTime()).TotalSeconds);
        }
        return new OutboxSnapshot(
            await Count("Pending"),
            await Count("Processing"),
            await Count("Completed"),
            await collection.CountDocumentsAsync(tenant & Builders<BsonDocument>.Filter.Gt("AttemptCount", 1), cancellationToken: ct),
            await Count("DeadLetter"),
            age);
    }

    private static OperationResult ToOperation(string name, Observation[] observations, double durationSeconds)
    {
        var timings = observations.Select(x => x.Milliseconds).Order().ToArray();
        var successes = observations.Count(x => x.Success);
        var errors = observations.Length - successes;
        return new OperationResult(
            name,
            observations.Length,
            successes,
            errors,
            CapacityMath.Percentile(timings, 0.50),
            CapacityMath.Percentile(timings, 0.95),
            CapacityMath.Percentile(timings, 0.99),
            timings.Length == 0 ? 0 : timings[^1],
            observations.Length / Math.Max(0.001, durationSeconds),
            observations.Length == 0 ? 1 : (double)errors / observations.Length);
    }

    private sealed record Observation(string Operation, double Milliseconds, bool Success);
}
