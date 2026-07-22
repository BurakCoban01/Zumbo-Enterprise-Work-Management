using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class WebhookReceiverIntegrationTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Real_backend_work_item_event_reaches_signed_loopback_receiver()
    {
        await using var receiver = new LoopbackReceiver([204]);
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BackgroundJobs:Enabled"] = "true",
                    ["Webhooks:AllowHttpLoopback"] = "true",
                    ["Webhooks:DispatcherIntervalSeconds"] = "1"
                }));
        });
        using var client = factory.CreateClient();
        var stamp = Guid.NewGuid().ToString("N");
        var tenantId = "webhook-real-" + stamp;
        var owner = await PostAsync<AuthResponse>(client, "/api/auth/register", new RegisterUserRequest(
            "webhook-real-" + stamp,
            $"webhook-real-{stamp}@zumbo.local",
            "P@ssword123",
            tenantId));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        _ = await PostAsync<OrganizationResponse>(client, "/api/organizations", new CreateOrganizationRequest(
            "Real Webhook Organization",
            tenantId));
        var receipt = await PostAsync<WebhookSecretReceipt>(
            client,
            "/api/integrations/webhooks",
            new CreateWebhookSubscriptionRequest("Real E2E", receiver.Url, ["work-item.created"]));
        var project = await PostAsync<ProjectResponse>(client, "/api/projects", new CreateProjectRequest(
            tenantId,
            "WH" + stamp[..6].ToUpperInvariant(),
            "Webhook E2E",
            owner.User.Id));
        var board = await PostAsync<BoardResponse>(client, "/api/boards", new CreateBoardRequest(
            project.Id,
            "Webhook Board",
            "Kanban"));
        var item = await PostAsync<WorkItemResponse>(client, "/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Deliver the real webhook",
            "Task",
            "High",
            null,
            null));

        var request = Assert.Single(await receiver.WaitForRequestsAsync(1, TimeSpan.FromSeconds(15)));
        using var payload = JsonDocument.Parse(request.Body);
        Assert.Equal("work-item.created", payload.RootElement.GetProperty("type").GetString());
        Assert.Equal(tenantId, payload.RootElement.GetProperty("tenantId").GetString());
        Assert.Equal(
            item.Id,
            payload.RootElement.GetProperty("data").GetProperty("workItemId").GetString());
        var timestamp = long.Parse(request.Headers["X-Zumbo-Webhook-Timestamp"]);
        Assert.Equal(
            "v1=" + WorkItemWebhookService.Sign(receipt.Secret, timestamp, request.Body),
            request.Headers["X-Zumbo-Webhook-Signature"]);
    }

    [Fact]
    public async Task Real_loopback_receiver_retries_dead_letters_and_replays_signed_immutable_payload()
    {
        await using var receiver = new LoopbackReceiver([503, 204, 500, 500, 204]);
        var subscriptions = new InMemoryDocumentRepository<WebhookSubscriptionDocument>();
        var deliveries = new InMemoryDocumentRepository<WebhookDeliveryDocument>();
        var protector = new TestProtector();
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new WebhookOptions
        {
            AllowHttpLoopback = true,
            MaximumAttempts = 2,
            BaseRetrySeconds = 1,
            MaximumRetrySeconds = 2,
            LeaseSeconds = 30,
            RequestTimeoutSeconds = 3,
            DispatchBatchSize = 10,
            DispatcherIntervalSeconds = 1,
            RotationOverlapMinutes = 15
        });
        var policy = new WebhookTargetPolicy(options);
        var service = new WorkItemWebhookService(
            subscriptions,
            deliveries,
            protector,
            policy,
            new PinnedWebhookSender(policy, options),
            new AllowAuthorization(),
            options,
            clock,
            new TestCurrentUser());
        var receipt = await service.CreateAsync(new(
            "Real receiver",
            receiver.Url,
            ["work-item.created"]), default);

        await service.QueueAsync("real-event-retry", "tenant-real", Event("retry"), default);
        Assert.Equal(0, await service.DispatchAsync(10, "real-worker-1", default));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await service.DispatchAsync(10, "real-worker-2", default));

        await service.QueueAsync("real-event-dead", "tenant-real", Event("dead"), default);
        Assert.Equal(0, await service.DispatchAsync(10, "real-worker-3", default));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await service.DispatchAsync(10, "real-worker-4", default));
        var deadLetter = Assert.Single(
            await deliveries.ListByFilterAsync(x => x.Status == WebhookDeliveryStatuses.DeadLetter));
        var deadPayload = deadLetter.Payload;
        var deadHash = deadLetter.PayloadSha256;
        await service.ReplayAsync(deadLetter.Id, default);
        Assert.Equal(1, await service.DispatchAsync(10, "real-worker-5", default));

        var requests = await receiver.WaitForRequestsAsync(5, TimeSpan.FromSeconds(5));
        Assert.Equal(5, requests.Count);
        foreach (var request in requests)
        {
            Assert.Equal("POST", request.Method);
            Assert.Equal("application/json; charset=utf-8", request.Headers["Content-Type"]);
            Assert.Equal(receipt.Subscription.SecretVersion.ToString(), request.Headers["X-Zumbo-Webhook-Secret-Version"]);
            var timestamp = long.Parse(request.Headers["X-Zumbo-Webhook-Timestamp"]);
            var expected = "v1=" + WorkItemWebhookService.Sign(receipt.Secret, timestamp, request.Body);
            Assert.Equal(expected, request.Headers["X-Zumbo-Webhook-Signature"]);
        }

        var deadRequests = requests.Where(request =>
            request.Headers["X-Zumbo-Webhook-Id"] == deadLetter.Id).ToList();
        Assert.Equal(3, deadRequests.Count);
        Assert.All(deadRequests, request => Assert.Equal(deadPayload, request.Body));
        var delivered = await deliveries.SelectAsync(x => x.Id == deadLetter.Id);
        Assert.Equal(WebhookDeliveryStatuses.Delivered, delivered!.Status);
        Assert.Equal(deadPayload, delivered.Payload);
        Assert.Equal(deadHash, delivered.PayloadSha256);
    }

    private static WorkItemWebhookEvent Event(string suffix) => new(
        "created",
        "work-item-" + suffix,
        "project-real",
        "correlation-" + suffix,
        new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero),
        "board-real",
        new WorkItemRealtimeItem(
            "work-item-" + suffix, "project-real", "board-real", "column-real", "Real delivery",
            "Task", "High", "Open", null, null, null, 5, null, 1000, 1),
        1);

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private sealed class TestProtector : IWebhookSecretProtector
    {
        public string Protect(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        public string Unprotect(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private sealed class AllowAuthorization : IWebhookAuthorization
    {
        public Task EnsureCanManageAsync(string organizationId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public string? UserId => "user-real";
        public string? OrganizationId => "tenant-real";
        public IReadOnlyCollection<string> Roles => ["OrganizationAdmin"];
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;
        public void Advance(TimeSpan duration) => UtcNow = UtcNow.Add(duration);
    }

    private sealed record ReceivedRequest(
        string Method,
        IReadOnlyDictionary<string, string> Headers,
        string Body);

    private sealed class LoopbackReceiver : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Queue<int> statuses;
        private readonly List<ReceivedRequest> requests = [];
        private readonly CancellationTokenSource stopping = new();
        private readonly Task serverTask;
        private readonly object gate = new();

        public LoopbackReceiver(IEnumerable<int> statuses)
        {
            this.statuses = new Queue<int>(statuses);
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/webhooks";
            serverTask = RunAsync(stopping.Token);
        }

        public string Url { get; }

        public async Task<IReadOnlyList<ReceivedRequest>> WaitForRequestsAsync(int count, TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            while (!cancellation.IsCancellationRequested)
            {
                lock (gate)
                {
                    if (requests.Count >= count) return requests.ToList();
                }
                await Task.Delay(10, cancellation.Token);
            }
            throw new TimeoutException($"Expected {count} webhook requests.");
        }

        public async ValueTask DisposeAsync()
        {
            stopping.Cancel();
            listener.Stop();
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException)
            {
            }
            stopping.Dispose();
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct);
                }
                catch (Exception exception) when (exception is OperationCanceledException or SocketException)
                {
                    return;
                }
                _ = HandleAsync(client, ct);
            }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            await using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true))
            {
                var requestLine = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(requestLine)) return;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (await reader.ReadLineAsync(ct) is { Length: > 0 } line)
                {
                    var separator = line.IndexOf(':');
                    if (separator > 0) headers[line[..separator]] = line[(separator + 1)..].Trim();
                }
                var contentLength = headers.TryGetValue("Content-Length", out var rawLength)
                    ? int.Parse(rawLength)
                    : 0;
                var bodyBuffer = new char[contentLength];
                var offset = 0;
                while (offset < contentLength)
                {
                    var read = await reader.ReadAsync(bodyBuffer.AsMemory(offset, contentLength - offset), ct);
                    if (read == 0) break;
                    offset += read;
                }
                lock (gate)
                {
                    requests.Add(new ReceivedRequest(
                        requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty,
                        headers,
                        new string(bodyBuffer, 0, offset)));
                }

                int status;
                lock (gate) status = statuses.Count > 0 ? statuses.Dequeue() : 204;
                var reason = status switch
                {
                    204 => "No Content",
                    500 => "Internal Server Error",
                    503 => "Service Unavailable",
                    _ => "Response"
                };
                var response = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, ct);
            }
        }
    }
}
