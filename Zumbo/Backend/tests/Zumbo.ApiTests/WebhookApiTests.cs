using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class WebhookApiTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Audit_tenant_resolution_uses_webhook_resource_ownership_without_request_context()
    {
        using var scope = baseFactory.Services.CreateScope();
        var subscriptions = scope.ServiceProvider
            .GetRequiredService<IDocumentRepository<WebhookSubscriptionDocument>>();
        var deliveries = scope.ServiceProvider
            .GetRequiredService<IDocumentRepository<WebhookDeliveryDocument>>();
        var subscription = await subscriptions.CreateAsync(new WebhookSubscriptionDocument
        {
            OrganizationId = "audit-webhook-tenant",
            Name = "Audit ownership",
            TargetUrl = "https://receiver.example.test/events",
            EventScopes = ["work-item.created"],
            CurrentSecretProtected = "protected",
            CurrentSecretFingerprint = "fingerprint",
            CreatedByUserId = "actor-1"
        });
        var delivery = await deliveries.CreateAsync(new WebhookDeliveryDocument
        {
            OrganizationId = "audit-webhook-tenant",
            SubscriptionId = subscription.Id,
            SourceEventId = "audit-source",
            EventScope = "webhook.test",
            TargetUrl = subscription.TargetUrl,
            Payload = "{}",
            PayloadSha256 = "hash"
        });
        var resolver = scope.ServiceProvider.GetRequiredService<IAuditTenantResolver>();

        var subscriptionTenant = await resolver.ResolveAsync(
            "WebhookSubscription", subscription.Id, "actor-1", default);
        var deliveryTenant = await resolver.ResolveAsync(
            "WebhookDelivery", delivery.Id, "actor-1", default);

        Assert.Equal("audit-webhook-tenant", subscriptionTenant.OrganizationId);
        Assert.Equal("audit-webhook-tenant", deliveryTenant.OrganizationId);
    }

    [Fact]
    public async Task Management_routes_require_permission_return_secret_once_and_hide_foreign_tenant()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BackgroundJobs:Enabled"] = "false",
                    ["Webhooks:AllowHttpLoopback"] = "true"
                }));
        });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/integrations/webhooks")).StatusCode);

        var stamp = Guid.NewGuid().ToString("N");
        var owner = await RegisterAsync(client, "owner-" + stamp, "tenant-a-" + stamp);
        Authenticate(client, owner.AccessToken);
        _ = await ReadAsync<OrganizationResponse>(await client.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationRequest("Webhook Tenant A", "tenant-a-" + stamp)));
        var createdResponse = await client.PostAsJsonAsync(
            "/api/integrations/webhooks",
            new CreateWebhookSubscriptionRequest(
                "Build receiver",
                "http://127.0.0.1:65530/events",
                ["work-item.created", "work-item.moved"]));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var receipt = (await createdResponse.Content
            .ReadFromJsonAsync<ApiResponse<WebhookSecretReceipt>>())!.Data!;
        Assert.StartsWith("whsec_", receipt.Secret);

        var listed = await ReadAsync<IReadOnlyList<WebhookSubscriptionResponse>>(
            await client.GetAsync("/api/integrations/webhooks"));
        var subscription = Assert.Single(listed);
        Assert.Equal(receipt.Subscription.Id, subscription.Id);
        var getBody = await (await client.GetAsync($"/api/integrations/webhooks/{subscription.Id}"))
            .Content.ReadAsStringAsync();
        Assert.DoesNotContain(receipt.Secret, getBody, StringComparison.Ordinal);
        Assert.DoesNotContain("currentSecretProtected", getBody, StringComparison.OrdinalIgnoreCase);

        var updated = await ReadAsync<WebhookSubscriptionResponse>(await client.PutAsJsonAsync(
            $"/api/integrations/webhooks/{subscription.Id}",
            new UpdateWebhookSubscriptionRequest(
                "Delivery receiver",
                "http://127.0.0.1:65530/delivery-events",
                ["work-item.created", "work-item.updated"],
                subscription.Version)));
        Assert.Equal("Delivery receiver", updated.Name);
        Assert.Equal(2, updated.Version);

        var testDeliveryResponse = await client.PostAsync(
            $"/api/integrations/webhooks/{subscription.Id}/test-delivery",
            content: null);
        Assert.Equal(HttpStatusCode.Created, testDeliveryResponse.StatusCode);
        var testDelivery = (await testDeliveryResponse.Content
            .ReadFromJsonAsync<ApiResponse<WebhookDeliveryResponse>>())!.Data!;
        Assert.Equal("webhook.test", testDelivery.EventScope);
        Assert.Equal(WebhookDeliveryStatuses.Pending, testDelivery.Status);

        var metrics = await ReadAsync<WebhookDeliveryMetrics>(
            await client.GetAsync("/api/integrations/webhooks/metrics"));
        Assert.True(metrics.Pending >= 1);
        var deliveries = await ReadAsync<WebhookDeliveryPage>(
            await client.GetAsync($"/api/integrations/webhooks/{subscription.Id}/deliveries?pageSize=20"));
        Assert.Contains(deliveries.Items, x => x.Id == testDelivery.Id);
        var delivery = await ReadAsync<WebhookDeliveryResponse>(
            await client.GetAsync($"/api/integrations/webhooks/deliveries/{testDelivery.Id}"));
        Assert.Equal(testDelivery.PayloadSha256, delivery.PayloadSha256);

        var disabled = await ReadAsync<WebhookSubscriptionResponse>(await client.PostAsJsonAsync(
            $"/api/integrations/webhooks/{subscription.Id}/disable",
            new SetWebhookSubscriptionStateRequest(updated.Version)));
        Assert.False(disabled.IsActive);
        var disabledTest = await client.PostAsync(
            $"/api/integrations/webhooks/{subscription.Id}/test-delivery",
            content: null);
        Assert.Equal(HttpStatusCode.Conflict, disabledTest.StatusCode);
        Assert.Contains("WEBHOOK_SUBSCRIPTION_DISABLED", await disabledTest.Content.ReadAsStringAsync());
        var enabled = await ReadAsync<WebhookSubscriptionResponse>(await client.PostAsJsonAsync(
            $"/api/integrations/webhooks/{subscription.Id}/enable",
            new SetWebhookSubscriptionStateRequest(disabled.Version)));
        Assert.True(enabled.IsActive);

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<IDocumentRepository<WebhookSubscriptionDocument>>();
            var stored = await repository.SelectAsync(x => x.Id == subscription.Id);
            Assert.NotNull(stored);
            Assert.DoesNotContain(receipt.Secret, JsonSerializer.Serialize(stored), StringComparison.Ordinal);
        }

        var rotatedResponse = await client.PostAsJsonAsync(
            $"/api/integrations/webhooks/{subscription.Id}/rotate-secret",
            new RotateWebhookSecretRequest(enabled.Version));
        Assert.True(
            rotatedResponse.IsSuccessStatusCode,
            $"Rotation failed with {(int)rotatedResponse.StatusCode}: {await rotatedResponse.Content.ReadAsStringAsync()}");
        var rotated = (await rotatedResponse.Content
            .ReadFromJsonAsync<ApiResponse<WebhookSecretReceipt>>())!.Data!;
        Assert.NotEqual(receipt.Secret, rotated.Secret);
        Assert.Equal(2, rotated.Subscription.SecretVersion);
        var stale = await client.PostAsJsonAsync(
            $"/api/integrations/webhooks/{subscription.Id}/disable",
            new SetWebhookSubscriptionStateRequest(updated.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var member = await RegisterAsync(client, "member-" + stamp, "tenant-a-" + stamp);
        Authenticate(client, member.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/integrations/webhooks")).StatusCode);

        var foreignAdmin = await RegisterAsync(client, "foreign-" + stamp, "tenant-b-" + stamp);
        Authenticate(client, foreignAdmin.AccessToken);
        _ = await ReadAsync<OrganizationResponse>(await client.PostAsJsonAsync(
            "/api/organizations",
            new CreateOrganizationRequest("Webhook Tenant B", "tenant-b-" + stamp)));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/integrations/webhooks/{subscription.Id}")).StatusCode);
    }

    [Fact]
    public async Task Target_policy_fails_closed_for_private_network_and_allows_opted_in_loopback_only()
    {
        var closed = new WebhookTargetPolicy(Options.Create(new WebhookOptions()));
        await Assert.ThrowsAsync<ValidationException>(
            () => closed.ValidateAsync("https://127.0.0.1/events", default));
        await Assert.ThrowsAsync<ValidationException>(
            () => closed.ValidateAsync("http://8.8.8.8/events", default));
        await Assert.ThrowsAsync<ValidationException>(
            () => closed.ValidateAsync("https://169.254.169.254/latest/meta-data", default));

        var development = new WebhookTargetPolicy(Options.Create(new WebhookOptions
        {
            AllowHttpLoopback = true
        }));
        await development.ValidateAsync("http://127.0.0.1:8080/events", default);
        await Assert.ThrowsAsync<ValidationException>(
            () => development.ValidateAsync("http://8.8.8.8/events", default));
        await Assert.ThrowsAsync<ValidationException>(
            () => development.ValidateAsync("https://100.64.0.1/events", default));
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username, string organizationId)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            username,
            $"{username}@zumbo.local",
            "P@ssword123",
            organizationId));
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
