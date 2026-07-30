using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class OperationsCenterApiTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Operations_routes_are_global_role_gated_redacted_and_audited()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["IdentityBootstrap:BootstrapToken"] = "operations-center-bootstrap",
                    ["BackgroundJobs:Enabled"] = "false"
                }));
        });
        using var client = factory.CreateClient();
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/operations/external-dependencies")).StatusCode);

        var stamp = Guid.NewGuid().ToString("N");
        var organizationId = "operations-" + stamp;
        var admin = await RegisterAsync(client, new RegisterUserRequest(
            "operations-admin-" + stamp,
            "admin@zumbo.local",
            "P@ssword123",
            organizationId,
            "operations-center-bootstrap"));
        var user = await RegisterAsync(client, new RegisterUserRequest(
            "operations-user-" + stamp,
            $"operations-user-{stamp}@zumbo.local",
            "P@ssword123",
            organizationId));

        Authenticate(client, user.AccessToken);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/work-items/durable-messaging/dead-letters")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync(
                $"/api/operations/storage/security?organizationId={organizationId}")).StatusCode);

        DurableEventEnvelope durableMessage;
        NotificationDocument notification;
        using (var scope = factory.Services.CreateScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IDurableEventOutbox>();
            durableMessage = new DurableEventEnvelope(
                "operations-message-" + stamp,
                "WorkItems",
                "work-item.updated.v1",
                1,
                organizationId,
                "private-correlation-" + stamp,
                null,
                """{"private":"payload"}""",
                DateTimeOffset.UtcNow.AddMinutes(-1));
            await outbox.EnqueueAsync(durableMessage);
            var lease = Assert.Single(await outbox.ClaimAsync(
                "operations-worker",
                1,
                TimeSpan.FromMinutes(1),
                DateTimeOffset.UtcNow));
            _ = await outbox.FailAsync(
                durableMessage.Id,
                lease.LeaseToken,
                "provider password=private",
                1,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(1));

            var notifications = scope.ServiceProvider
                .GetRequiredService<IDocumentRepository<NotificationDocument>>();
            notification = await notifications.CreateAsync(new NotificationDocument
            {
                OrganizationId = organizationId,
                UserId = user.User.Id,
                Type = "Assignment",
                Message = "private notification body",
                EmailAddress = "private@zumbo.local",
                EmailStatus = NotificationEmailStatuses.DeadLetter,
                EmailAttempts = 3,
                EmailLastError = "smtp password=private",
                EmailDeadLetteredAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        Authenticate(client, admin.AccessToken);
        var dependencies = await client.GetStringAsync("/api/operations/external-dependencies");
        Assert.DoesNotContain("connectionString", dependencies, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", dependencies, StringComparison.OrdinalIgnoreCase);

        var messages = await client.GetStringAsync(
            "/api/work-items/durable-messaging/dead-letters?pageSize=20");
        Assert.Contains(durableMessage.Id, messages, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", messages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correlation", messages, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", messages, StringComparison.OrdinalIgnoreCase);

        var notificationList = await client.GetStringAsync(
            $"/api/notifications/delivery/dead-letters?organizationId={organizationId}&pageSize=20");
        Assert.Contains(notification.Id, notificationList, StringComparison.Ordinal);
        Assert.DoesNotContain("private notification body", notificationList, StringComparison.Ordinal);
        Assert.DoesNotContain("private@zumbo.local", notificationList, StringComparison.Ordinal);
        Assert.DoesNotContain("password", notificationList, StringComparison.OrdinalIgnoreCase);

        var messageReplay = await client.PostAsync(
            $"/api/work-items/durable-messaging/dead-letter/{durableMessage.Id}/replay",
            null);
        messageReplay.EnsureSuccessStatusCode();
        var notificationReplay = await client.PostAsync(
            $"/api/notifications/delivery/{notification.Id}/replay?organizationId={organizationId}",
            null);
        notificationReplay.EnsureSuccessStatusCode();
        var maintenance = await client.PostAsync(
            $"/api/operations/storage/security/maintenance?organizationId={organizationId}",
            null);
        maintenance.EnsureSuccessStatusCode();

        var audit = await ReadAsync<AuditLogPageResponse>(await client.GetAsync(
            $"/api/audit?organizationId={organizationId}&pageSize=100"));
        Assert.Contains(audit.Items, item => item.Action == "DurableMessageReplayed");
        Assert.Contains(audit.Items, item => item.Action == "NotificationDeliveryReplayed");
        Assert.Contains(audit.Items, item => item.Action == "AttachmentSecurityMaintenanceRun");
        Assert.DoesNotContain("password", System.Text.Json.JsonSerializer.Serialize(audit), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, RegisterUserRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }

    private static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}
