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
using Zumbo.Api.Presentation.Controllers.Teams;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class TeamControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task TeamRoutes_AreControllerOwnedAndPreservePresentationContracts()
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

        var controllerTypes = new[]
        {
            typeof(TeamCatalogController),
            typeof(TeamMembershipController),
            typeof(TeamLifecycleController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(
                endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(13, endpoints.Count);

        AssertEndpoint(endpoints, HttpMethods.Get, "api/teams", PermissionCatalog.TeamView);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/teams", PermissionCatalog.TeamManage);
        AssertEndpoint(endpoints, HttpMethods.Put, "api/teams/{teamId}", PermissionCatalog.TeamManage);
        AssertEndpoint(endpoints, HttpMethods.Delete, "api/teams/{teamId}", PermissionCatalog.TeamManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/teams/{teamId}/restore", PermissionCatalog.TeamManage);
        AssertEndpoint(endpoints, HttpMethods.Get, "api/teams/{teamId}/members", PermissionCatalog.TeamView);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/teams/{teamId}/members", PermissionCatalog.TeamManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/teams/{teamId}/invites/accept", PermissionCatalog.TeamView);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/teams/{teamId}/invites/decline", PermissionCatalog.TeamView);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/teams/{teamId}/invites/{inviteId}/revoke", PermissionCatalog.TeamManage);
        AssertEndpoint(endpoints, HttpMethods.Patch, "api/teams/{teamId}/members/{userId}/role", PermissionCatalog.TeamManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/teams/{teamId}/ownership-transfer", PermissionCatalog.TeamManage);
        AssertEndpoint(endpoints, HttpMethods.Delete, "api/teams/{teamId}/members/{userIdOrEmail}", PermissionCatalog.TeamManage);

        using var anonymous = await client.GetAsync("/api/teams?organizationId=organization-id");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        using var missingBody = await client.PostAsync("/api/teams", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingBody.StatusCode);
        Assert.Equal(0, missingBody.Content.Headers.ContentLength);

        using var malformedBody = await client.PostAsync(
            "/api/teams",
            new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformedBody.StatusCode);
        Assert.Equal(0, malformedBody.Content.Headers.ContentLength);
    }

    private static void AssertEndpoint(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route,
        string permissionKey)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = Assert.IsAssignableFrom<IEndpointPermissionMetadata>(
            endpoint.Metadata.GetMetadata<IEndpointPermissionMetadata>());
        Assert.Equal(permissionKey, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal("api", endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var stamp = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "team-contract-" + stamp,
            $"team-contract-{stamp}@zumbo.local",
            "P@ssword123",
            "team-contract-org-" + stamp));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
    }
}
