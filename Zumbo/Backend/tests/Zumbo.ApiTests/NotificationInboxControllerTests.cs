using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Zumbo.Api.Presentation.Controllers.Notifications;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class NotificationInboxControllerTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task InboxRoutes_AreControllerOwnedAndPreserveContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RegistrationProvisioning:Mode"] = "LocalDemo"
                }));
        });
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo
                .AsType() == typeof(NotificationInboxController))
            .ToList();
        Assert.Equal(3, endpoints.Count);

        AssertEndpoint(endpoints, HttpMethods.Get, "api/notifications", nameof(NotificationInboxController.ListMine), PermissionCatalog.NotificationView);
        AssertEndpoint(endpoints, HttpMethods.Get, "api/notifications/{userId}", nameof(NotificationInboxController.ListForUser), PermissionCatalog.NotificationView);
        AssertEndpoint(endpoints, HttpMethods.Patch, "api/notifications/{notificationId}/read", nameof(NotificationInboxController.MarkAsRead), PermissionCatalog.NotificationManage);

        using var anonymous = await client.GetAsync("/api/notifications?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var first = await RegisterAsync(client, "inbox-first-");
        var second = await RegisterAsync(client, "inbox-second-");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);

        using var mine = await client.GetAsync("/api/notifications?page=1&pageSize=10&unreadOnly=true");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.NotNull((await mine.Content.ReadFromJsonAsync<ApiResponse<IReadOnlyList<NotificationResponse>>>())?.Data);

        using var invalidPage = await client.GetAsync("/api/notifications?page=0&pageSize=10");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        var invalidEnvelope = await invalidPage.Content.ReadFromJsonAsync<ApiResponse<object>>();
        Assert.Equal("REQUEST_LIMIT_EXCEEDED", invalidEnvelope?.Error?.Code);

        using var forbidden = await client.GetAsync($"/api/notifications/{second.User.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var missing = await client.PatchAsJsonAsync(
            "/api/notifications/missing-notification/read",
            new { });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static void AssertEndpoint(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route,
        string actionName,
        string permissionKey)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        var action = Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.Equal(actionName, action.ActionName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = Assert.IsAssignableFrom<IEndpointPermissionMetadata>(
            endpoint.Metadata.GetMetadata<IEndpointPermissionMetadata>());
        Assert.Equal(permissionKey, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal("api", endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string prefix)
    {
        var stamp = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            prefix + stamp,
            $"{prefix}{stamp}@zumbo.local",
            "P@ssword123",
            prefix + "org-" + stamp));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
    }
}
