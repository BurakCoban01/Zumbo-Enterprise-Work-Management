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
using Zumbo.Modules.Organizations;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class WebhookApiTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
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
            new RotateWebhookSecretRequest(subscription.Version));
        Assert.True(
            rotatedResponse.IsSuccessStatusCode,
            $"Rotation failed with {(int)rotatedResponse.StatusCode}: {await rotatedResponse.Content.ReadAsStringAsync()}");
        var rotated = (await rotatedResponse.Content
            .ReadFromJsonAsync<ApiResponse<WebhookSecretReceipt>>())!.Data!;
        Assert.NotEqual(receipt.Secret, rotated.Secret);
        Assert.Equal(2, rotated.Subscription.SecretVersion);
        var stale = await client.PostAsJsonAsync(
            $"/api/integrations/webhooks/{subscription.Id}/disable",
            new SetWebhookSubscriptionStateRequest(subscription.Version));
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
