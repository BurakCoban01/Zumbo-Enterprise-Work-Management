using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.Integrations;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class WebhookControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task WebhookRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["RegistrationProvisioning:Mode"] = "LocalDemo" }));
        });
        using var client = factory.CreateClient();
        var types = new[] { typeof(WebhookSubscriptionsController), typeof(WebhookSubscriptionLifecycleController), typeof(WebhookDeliveriesController) };
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => types.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType())).ToList();
        Assert.Equal(12, endpoints.Count);

        AssertEndpoint(endpoints, HttpMethods.Get, "api/integrations/webhooks", "api");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/integrations/webhooks", "api");
        AssertEndpoint(endpoints, HttpMethods.Get, "api/integrations/webhooks/metrics", "api");
        AssertEndpoint(endpoints, HttpMethods.Get, "api/integrations/webhooks/{id}", "api");
        AssertEndpoint(endpoints, HttpMethods.Put, "api/integrations/webhooks/{id}", "api");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/integrations/webhooks/{id}/rotate-secret", "bulk");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/integrations/webhooks/{id}/enable", "api");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/integrations/webhooks/{id}/disable", "api");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/integrations/webhooks/{id}/test-delivery", "bulk");
        AssertEndpoint(endpoints, HttpMethods.Get, "api/integrations/webhooks/{id}/deliveries", "api");
        AssertEndpoint(endpoints, HttpMethods.Get, "api/integrations/webhooks/deliveries/{deliveryId}", "api");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/integrations/webhooks/deliveries/{deliveryId}/replay", "bulk");

        using var anonymous = await client.GetAsync("/api/integrations/webhooks");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var missing = await client.PostAsync("/api/integrations/webhooks", null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(0, missing.Content.Headers.ContentLength);
        using var malformed = await client.PostAsync("/api/integrations/webhooks", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(0, malformed.Content.Headers.ContentLength);
    }

    private static void AssertEndpoint(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string rate)
    {
        var endpoint = Assert.Single(endpoints, candidate => string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = Assert.IsAssignableFrom<IEndpointPermissionMetadata>(endpoint.Metadata.GetMetadata<IEndpointPermissionMetadata>());
        Assert.Equal(PermissionCatalog.IntegrationManage, permission.Permission);
        Assert.Equal(rate, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var stamp = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "webhook-contract-" + stamp, $"webhook-contract-{stamp}@zumbo.local", "P@ssword123", "webhook-contract-org-" + stamp));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
    }
}
