using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
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

public sealed class ControllerFoundationTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task NotificationPreferencesGet_IsControllerOwnedAndPreservesHttpContract()
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
            .Where(endpoint => string.Equals(
                endpoint.RoutePattern.RawText?.TrimStart('/'),
                "api/notifications/preferences/me",
                StringComparison.Ordinal)
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(
                    HttpMethods.Get,
                    StringComparer.Ordinal) == true)
            .ToList();
        var endpoint = Assert.Single(endpoints);
        var action = Assert.IsType<ControllerActionDescriptor>(
            endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.Equal(nameof(NotificationPreferencesController), action.ControllerName + "Controller");
        Assert.Equal(nameof(NotificationPreferencesController.GetMine), action.ActionName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = Assert.IsAssignableFrom<IEndpointPermissionMetadata>(
            endpoint.Metadata.GetMetadata<IEndpointPermissionMetadata>());
        Assert.Equal(PermissionCatalog.NotificationView, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal("api", endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.Contains(
            "Notifications",
            endpoint.Metadata.GetMetadata<ITagsMetadata>()?.Tags ?? [],
            StringComparer.Ordinal);

        var anonymous = await client.GetAsync("/api/notifications/preferences/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var stamp = Guid.NewGuid().ToString("N");
        var registration = await PostAsync<AuthResponse>(client, "/api/auth/register", new RegisterUserRequest(
            "controller-user-" + stamp,
            $"controller-user-{stamp}@zumbo.local",
            "P@ssword123",
            "controller-org-" + stamp));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.AccessToken);

        var response = await client.GetAsync("/api/notifications/preferences/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-Id", out var headerValues));
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<NotificationPreferenceResponse>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Null(envelope.Error);
        Assert.Equal(Assert.Single(headerValues), envelope.CorrelationId);

        using var swagger = await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json");
        var operation = swagger!.RootElement
            .GetProperty("paths")
            .GetProperty("/api/notifications/preferences/me")
            .GetProperty("get");
        Assert.Equal("Notifications", operation.GetProperty("tags")[0].GetString());
        var successResponse = operation.GetProperty("responses").GetProperty("200");
        Assert.True(successResponse.GetProperty("content").TryGetProperty("application/json", out _));
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<T>>())!.Data!;
    }
}
