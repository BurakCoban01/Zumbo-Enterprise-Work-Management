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
using Zumbo.Api.Presentation.Controllers.Boards;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class BoardControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task BoardRoutes_AreControllerOwnedAndPreservePresentationContracts()
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
            typeof(BoardCatalogController),
            typeof(BoardLifecycleController),
            typeof(BoardViewsController),
            typeof(BoardColumnsController),
            typeof(BoardConfigurationController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(
                endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(14, endpoints.Count);

        AssertEndpoint(endpoints, HttpMethods.Get, "api/boards/by-project/{projectId}", PermissionCatalog.BoardView);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/boards", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Put, "api/boards/{boardId}", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Delete, "api/boards/{boardId}", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/boards/{boardId}/restore", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Patch, "api/boards/{boardId}/swimlane", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Put, "api/boards/{boardId}/workflow-mapping", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/boards/{boardId}/views", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Put, "api/boards/{boardId}/views/{viewId}", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Delete, "api/boards/{boardId}/views/{viewId}", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Post, "api/boards/{boardId}/columns", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Put, "api/boards/{boardId}/columns/{columnId}", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Put, "api/boards/{boardId}/columns/reorder", PermissionCatalog.BoardManage);
        AssertEndpoint(endpoints, HttpMethods.Delete, "api/boards/{boardId}/columns/{columnId}", PermissionCatalog.BoardManage);

        using var anonymous = await client.GetAsync("/api/boards/by-project/project-id");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        using var missingBody = await client.PostAsync("/api/boards", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingBody.StatusCode);
        Assert.Equal(0, missingBody.Content.Headers.ContentLength);

        using var malformedBody = await client.PostAsync(
            "/api/boards",
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
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var stamp = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "board-contract-" + stamp,
            $"board-contract-{stamp}@zumbo.local",
            "P@ssword123",
            "board-contract-org-" + stamp));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
    }
}
