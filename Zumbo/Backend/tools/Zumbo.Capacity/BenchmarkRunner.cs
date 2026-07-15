using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.Modules.Audit;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Capacity;

internal sealed class BenchmarkRunner(string mongoConnectionString, string apiBaseUrl)
{
    private readonly MongoClient _mongo = new(mongoConnectionString);
    private readonly HttpClient _http = new() { BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };

    public async Task<BenchmarkResult> RunAsync(
        CapacityProfile profile,
        int samples,
        int realtimeClients,
        CancellationToken cancellationToken)
    {
        var token = await LoginAsync(profile, cancellationToken);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var projectId = CapacityIds.Project(profile, 0);
        var workItemId = CapacityIds.WorkItem(profile, 0);

        await EnsureSuccessAsync(await _http.GetAsync($"api/workflows/{projectId}", cancellationToken));
        await WarmUpAsync(projectId, workItemId, cancellationToken);
        await WarmUpMovesAsync(workItemId, cancellationToken);

        var metrics = new List<MetricResult>
        {
            await MeasureAsync("ordinary-read", samples, 300, async index =>
                await _http.GetAsync(
                    $"api/work-items/{CapacityIds.WorkItem(profile, (index * profile.ProjectCount) % profile.WorkItemCount)}",
                    cancellationToken)),
            await MeasureAsync("ordinary-write", samples, 500, async index =>
                await _http.PatchAsJsonAsync($"api/work-items/{workItemId}/planning", new { sprintId = "benchmark", estimatePoints = (index % 13) + 1 }, cancellationToken)),
            await MeasureAsync("board-initial-load", samples, 700, async _ =>
                await _http.GetAsync($"api/work-items?projectId={Uri.EscapeDataString(projectId)}", cancellationToken)),
            await MeasureMovesAsync(workItemId, samples, cancellationToken),
            await MeasureAsync("indexed-search", samples, 500, async _ =>
                await _http.GetAsync($"api/work-items?projectId={Uri.EscapeDataString(projectId)}&text=capacity%20common", cancellationToken))
        };

        var realtime = await MeasureRealtimeAsync(profile, token, realtimeClients, cancellationToken);
        var workItems = await CountAsync<WorkItemDocument>("ZumboWorkItems", "workitems", profile.Prefix, cancellationToken);
        var events = await CountAsync<AuditLogDocument>("ZumboAudit", "auditlogs", profile.Prefix, cancellationToken);
        var machine = new ReferenceMachine(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

        return new BenchmarkResult(
            DateTimeOffset.UtcNow,
            profile.Name,
            _http.BaseAddress!.ToString(),
            machine,
            new DatasetCounts(workItems, events),
            metrics,
            realtime,
            metrics.All(metric => metric.Passed) && realtime.Passed);
    }

    private async Task<string> LoginAsync(CapacityProfile profile, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync("api/auth/login", new
        {
            usernameOrEmail = CapacityIds.Username(profile, 0),
            password = "P@ssword123"
        }, ct);
        await EnsureSuccessAsync(response);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LoginPayload>>(cancellationToken: ct);
        return envelope?.Data?.AccessToken is { Length: > 0 } token
            ? token
            : throw new InvalidOperationException("Login response did not contain an access token.");
    }

    private async Task WarmUpAsync(string projectId, string workItemId, CancellationToken ct)
    {
        for (var index = 0; index < 2; index++)
        {
            await EnsureSuccessAsync(await _http.GetAsync($"api/work-items/{workItemId}", ct));
            await EnsureSuccessAsync(await _http.GetAsync($"api/work-items?projectId={projectId}", ct));
            await EnsureSuccessAsync(await _http.GetAsync($"api/work-items?projectId={projectId}&text=capacity", ct));
        }
    }

    private async Task WarmUpMovesAsync(string workItemId, CancellationToken ct)
    {
        using var currentResponse = await _http.GetAsync($"api/work-items/{workItemId}", ct);
        await EnsureSuccessAsync(currentResponse);
        var current = await currentResponse.Content.ReadFromJsonAsync<ApiEnvelope<WorkItemPayload>>(cancellationToken: ct);
        var status = current?.Data?.Status ?? "To Do";
        if (status is not ("To Do" or "In Progress"))
        {
            throw new InvalidOperationException($"Move warm-up item has unsupported status '{status}'. Reseed the profile.");
        }

        for (var index = 0; index < 2; index++)
        {
            status = status == "To Do" ? "In Progress" : "To Do";
            using var response = await _http.PatchAsJsonAsync(
                $"api/work-items/{workItemId}/status",
                new { status },
                ct);
            await EnsureSuccessAsync(response);
        }
    }

    private async Task<MetricResult> MeasureMovesAsync(string workItemId, int samples, CancellationToken ct)
    {
        using var currentResponse = await _http.GetAsync($"api/work-items/{workItemId}", ct);
        await EnsureSuccessAsync(currentResponse);
        var current = await currentResponse.Content.ReadFromJsonAsync<ApiEnvelope<WorkItemPayload>>(cancellationToken: ct);
        var status = current?.Data?.Status ?? "To Do";
        if (status is not ("To Do" or "In Progress"))
        {
            throw new InvalidOperationException($"Move benchmark item has unsupported status '{status}'. Reseed the profile.");
        }

        return await MeasureAsync("board-move", samples, 250, async _ =>
        {
            status = status == "To Do" ? "In Progress" : "To Do";
            return await _http.PatchAsJsonAsync($"api/work-items/{workItemId}/status", new { status }, ct);
        });
    }

    private async Task<MetricResult> MeasureAsync(
        string name,
        int samples,
        double budgetMilliseconds,
        Func<int, Task<HttpResponseMessage>> operation)
    {
        var timings = new List<double>(samples);
        for (var index = 0; index < samples; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var response = await operation(index);
            stopwatch.Stop();
            await EnsureSuccessAsync(response);
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        timings.Sort();
        var p95 = Percentile(timings, 0.95);
        Console.Error.WriteLine($"{name}: p95={p95:F2} ms, max={timings[^1]:F2} ms");
        return new MetricResult(
            name,
            timings.Count,
            Percentile(timings, 0.50),
            p95,
            timings[^1],
            budgetMilliseconds,
            p95 < budgetMilliseconds);
    }

    private async Task<RealtimeResult> MeasureRealtimeAsync(
        CapacityProfile profile,
        string token,
        int requestedClients,
        CancellationToken ct)
    {
        var projectId = CapacityIds.Project(profile, 0);
        var workItemId = CapacityIds.WorkItem(profile, 0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        var clients = await Task.WhenAll(Enumerable.Range(0, requestedClients)
            .Select(index => SignalRClient.ConnectAsync(_http.BaseAddress!, token, projectId, index, timeout.Token)));

        try
        {
            var receiveTasks = clients.Select(client => client.WaitForWorkItemAsync(workItemId, timeout.Token)).ToArray();
            var started = Stopwatch.GetTimestamp();

            using var currentResponse = await _http.GetAsync($"api/work-items/{workItemId}", timeout.Token);
            await EnsureSuccessAsync(currentResponse);
            var current = await currentResponse.Content.ReadFromJsonAsync<ApiEnvelope<WorkItemPayload>>(cancellationToken: timeout.Token);
            var target = current?.Data?.Status == "To Do" ? "In Progress" : "To Do";
            using var moveResponse = await _http.PatchAsJsonAsync(
                $"api/work-items/{workItemId}/status",
                new { status = target },
                timeout.Token);
            await EnsureSuccessAsync(moveResponse);

            var receipts = await Task.WhenAll(receiveTasks);
            var timings = receipts
                .Select(receipt => Stopwatch.GetElapsedTime(started, receipt.Timestamp).TotalMilliseconds)
                .Order()
                .ToList();
            var p95 = Percentile(timings, 0.95);
            var maximumPayloadBytes = receipts.Max(receipt => receipt.PayloadBytes);
            const int payloadBudgetBytes = 2_048;
            Console.Error.WriteLine(
                $"realtime ({requestedClients} clients): p95={p95:F2} ms, max={timings[^1]:F2} ms, payload={maximumPayloadBytes} bytes");
            return new RealtimeResult(
                requestedClients,
                clients.Length,
                timings.Count,
                p95,
                timings[^1],
                1_000,
                maximumPayloadBytes,
                payloadBudgetBytes,
                p95 < 1_000 && maximumPayloadBytes < payloadBudgetBytes);
        }
        finally
        {
            await Task.WhenAll(clients.Select(client => client.DisposeAsync().AsTask()));
        }
    }

    private async Task<long> CountAsync<T>(string database, string collection, string prefix, CancellationToken ct)
    {
        var target = _mongo.GetDatabase(database).GetCollection<T>(collection);
        return await target.CountDocumentsAsync(
            Builders<T>.Filter.Regex("_id", new BsonRegularExpression("^" + prefix)),
            cancellationToken: ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private sealed class SignalRClient(ClientWebSocket socket)
    {
        private const char RecordSeparator = '\u001e';

        public static async Task<SignalRClient> ConnectAsync(
            Uri baseAddress,
            string token,
            string projectId,
            int invocationId,
            CancellationToken ct)
        {
            using var negotiate = new HttpClient { BaseAddress = baseAddress };
            negotiate.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await negotiate.PostAsync("hubs/work-items/negotiate?negotiateVersion=1", null, ct);
            await EnsureSuccessAsync(response);
            var payload = await response.Content.ReadFromJsonAsync<NegotiatePayload>(cancellationToken: ct)
                ?? throw new InvalidOperationException("SignalR negotiate response was empty.");

            var scheme = baseAddress.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
            var webSocketUri = new UriBuilder(baseAddress)
            {
                Scheme = scheme,
                Path = "hubs/work-items",
                Query = $"id={Uri.EscapeDataString(payload.ConnectionToken)}&access_token={Uri.EscapeDataString(token)}"
            }.Uri;
            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            await socket.ConnectAsync(webSocketUri, ct);
            var client = new SignalRClient(socket);
            await client.SendAsync("{\"protocol\":\"json\",\"version\":1}" + RecordSeparator, ct);
            var handshake = await client.ReceiveAsync(ct);
            if (!handshake.StartsWith("{}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"SignalR handshake failed: {handshake}");
            }

            var subscription = JsonSerializer.Serialize(new
            {
                type = 1,
                invocationId = invocationId.ToString(),
                target = "SubscribeProject",
                arguments = new[] { projectId }
            });
            await client.SendAsync(subscription + RecordSeparator, ct);
            while (true)
            {
                foreach (var frame in SplitFrames(await client.ReceiveAsync(ct)))
                {
                    using var document = JsonDocument.Parse(frame);
                    var root = document.RootElement;
                    if (root.TryGetProperty("type", out var type)
                        && type.GetInt32() == 3
                        && root.TryGetProperty("invocationId", out var completedId)
                        && completedId.GetString() == invocationId.ToString())
                    {
                        if (root.TryGetProperty("error", out var error))
                        {
                            throw new InvalidOperationException($"SignalR subscription failed: {error.GetString()}");
                        }
                        return client;
                    }
                }
            }
        }

        public async Task<RealtimeReceipt> WaitForWorkItemAsync(string workItemId, CancellationToken ct)
        {
            while (true)
            {
                foreach (var frame in SplitFrames(await ReceiveAsync(ct)))
                {
                    using var document = JsonDocument.Parse(frame);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("type", out var type)
                        || type.GetInt32() != 1
                        || !root.TryGetProperty("target", out var target)
                        || target.GetString() != "workItemChanged")
                    {
                        continue;
                    }

                    var eventWorkItemId = root.GetProperty("arguments")[0].GetProperty("workItemId").GetString();
                    if (eventWorkItemId == workItemId)
                    {
                        return new RealtimeReceipt(Stopwatch.GetTimestamp(), Encoding.UTF8.GetByteCount(frame));
                    }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "benchmark complete", timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    socket.Abort();
                }
            }
            socket.Dispose();
        }

        private async Task SendAsync(string message, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }

        private async Task<string> ReceiveAsync(CancellationToken ct)
        {
            using var stream = new MemoryStream();
            var buffer = new byte[16 * 1024];
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("SignalR socket closed before the expected message.");
                }
                stream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static IEnumerable<string> SplitFrames(string payload) =>
            payload.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        internal sealed record RealtimeReceipt(long Timestamp, int PayloadBytes);
    }
}
