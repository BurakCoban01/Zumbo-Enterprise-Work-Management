using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.Notifications;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class NotificationDeliveryOperationsControllerTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task DeliveryOperations_AreControllerOwnedAndPreserveRequiredQueryContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["IdentityBootstrap:BootstrapToken"] = "notification-delivery-bootstrap",
                    ["BackgroundJobs:Enabled"] = "false"
                }));
        });
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo
                .AsType() == typeof(NotificationDeliveryOperationsController))
            .ToList();
        Assert.Equal(3, endpoints.Count);
        AssertEndpoint(endpoints, HttpMethods.Get, "api/notifications/delivery/status", "report");
        AssertEndpoint(endpoints, HttpMethods.Get, "api/notifications/delivery/dead-letters", "report");
        AssertEndpoint(endpoints, HttpMethods.Post, "api/notifications/delivery/{notificationId}/replay", "bulk");

        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);

        foreach (var path in new[]
        {
            "/api/notifications/delivery/status",
            "/api/notifications/delivery/dead-letters",
            "/api/notifications/delivery/missing/replay"
        })
        {
            using var response = path.EndsWith("/replay", StringComparison.Ordinal)
                ? await client.PostAsync(path, null)
                : await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            Assert.Null(response.Content.Headers.ContentType);
        }

        using var status = await client.GetAsync(
            $"/api/notifications/delivery/status?organizationId={registration.User.OrganizationId}");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);

        using var deadLetters = await client.GetAsync(
            $"/api/notifications/delivery/dead-letters?organizationId={registration.User.OrganizationId}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, deadLetters.StatusCode);

        using var missingReplay = await client.PostAsync(
            $"/api/notifications/delivery/missing/replay?organizationId={registration.User.OrganizationId}",
            null);
        Assert.Equal(HttpStatusCode.NotFound, missingReplay.StatusCode);
    }

    private static void AssertEndpoint(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route,
        string ratePolicy)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        var permission = Assert.IsAssignableFrom<IEndpointPermissionMetadata>(
            endpoint.Metadata.GetMetadata<IEndpointPermissionMetadata>());
        Assert.Equal(PermissionCatalog.OperationsManage, permission.Permission);
        Assert.True(permission.IsGlobal);
        Assert.Equal(ratePolicy, endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var stamp = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "delivery-ops-" + stamp,
            "admin@zumbo.local",
            "P@ssword123",
            "delivery-ops-org-" + stamp,
            "notification-delivery-bootstrap"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
    }
}
