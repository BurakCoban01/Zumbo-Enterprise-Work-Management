using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class DurableConsumerFailureTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FailOnceWebhookDelivery _webhook = new();

    public DurableConsumerFailureTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DurableMessaging:BaseRetryDelay"] = "00:00:00.025",
                    ["DurableMessaging:MaximumRetryDelay"] = "00:00:00.100",
                    ["DurableMessaging:RetryJitterRatio"] = "0",
                    ["DurableMessaging:IdleDelay"] = "00:00:00.025"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IWorkItemWebhookDelivery>();
                services.AddSingleton<IWorkItemWebhookDelivery>(_webhook);
            });
        });
    }

    [Fact]
    public async Task TransientWebhookFailure_DoesNotRollbackWrite_AndEventuallyCompletes()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var registration = await PostAsync<AuthResponse>(client, "/api/auth/register", new RegisterUserRequest(
            "durable-" + suffix,
            $"durable-{suffix}@zumbo.local",
            "P@ssword123",
            "org-durable-" + suffix));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);
        _ = await PostAsync<OrganizationResponse>(client, "/api/organizations", new CreateOrganizationRequest(
            "Durable Organization",
            registration.User.OrganizationId));
        var project = await PostAsync<ProjectResponse>(client, "/api/projects", new CreateProjectRequest(
            registration.User.OrganizationId,
            "D" + suffix[..7],
            "Durable consumer failure",
            registration.User.Id));
        var board = await PostAsync<BoardResponse>(client, "/api/boards", new CreateBoardRequest(
            project.Id,
            "Durable board",
            "Kanban"));

        var created = await PostAsync<WorkItemResponse>(client, "/api/work-items", new CreateWorkItemRequest(
            project.Id,
            board.Id,
            "Webhook retry survives",
            "Task",
            "Medium",
            registration.User.Id,
            null));
        var persisted = await GetAsync<WorkItemResponse>(client, $"/api/work-items/{created.Id}");

        Assert.Equal(created.Id, persisted.Id);
        var outbox = _factory.Services.GetRequiredService<IDurableEventOutbox>();
        await EventuallyAsync(async () =>
        {
            var metrics = await outbox.GetMetricsAsync(DateTimeOffset.UtcNow);
            return _webhook.Attempts >= 2 && metrics.Retried >= 1 && metrics.Pending == 0;
        });
        Assert.Equal(2, _webhook.Attempts);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object request)
    {
        var response = await client.PostAsJsonAsync(url, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (await condition()) return;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Durable webhook event did not recover after a transient failure.");
    }

    private sealed class FailOnceWebhookDelivery : IWorkItemWebhookDelivery
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);

        public Task DeliverAsync(WorkItemWebhookEvent message, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new InvalidOperationException("Injected webhook dependency failure.");
            }

            return Task.CompletedTask;
        }
    }
}
