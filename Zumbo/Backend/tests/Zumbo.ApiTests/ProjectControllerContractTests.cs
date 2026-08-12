using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Zumbo.Api.Presentation.Controllers.Projects;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class ProjectControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ProjectRoutes_AreControllerOwnedAndPreservePresentationContracts()
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
            typeof(ProjectCatalogController),
            typeof(ProjectLifecycleController),
            typeof(ProjectMembershipController),
            typeof(ProjectTemplatesController),
            typeof(ProjectComponentsController),
            typeof(ProjectReleasesController),
            typeof(ProjectMilestonesController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(
                endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(26, endpoints.Count);

        var expected = new (string Method, string Route, string Permission)[]
        {
            (HttpMethods.Get, "api/projects", PermissionCatalog.ProjectView),
            (HttpMethods.Post, "api/projects", PermissionCatalog.ProjectManage),
            (HttpMethods.Get, "api/projects/{projectId}", PermissionCatalog.ProjectView),
            (HttpMethods.Put, "api/projects/{projectId}", PermissionCatalog.ProjectManage),
            (HttpMethods.Delete, "api/projects/{projectId}", PermissionCatalog.ProjectManage),
            (HttpMethods.Post, "api/projects/{projectId}/restore", PermissionCatalog.ProjectManage),
            (HttpMethods.Post, "api/projects/{projectId}/members", PermissionCatalog.ProjectManage),
            (HttpMethods.Patch, "api/projects/{projectId}/members/{userId}/role", PermissionCatalog.ProjectManage),
            (HttpMethods.Post, "api/projects/{projectId}/ownership-transfer", PermissionCatalog.ProjectManage),
            (HttpMethods.Delete, "api/projects/{projectId}/members/{userId}", PermissionCatalog.ProjectManage),
            (HttpMethods.Post, "api/projects/{projectId}/teams", PermissionCatalog.ProjectManage),
            (HttpMethods.Delete, "api/projects/{projectId}/teams/{teamId}", PermissionCatalog.ProjectManage),
            (HttpMethods.Post, "api/projects/{projectId}/templates", PermissionCatalog.ProjectManage),
            (HttpMethods.Put, "api/projects/{projectId}/templates/{templateId}", PermissionCatalog.ProjectManage),
            (HttpMethods.Delete, "api/projects/{projectId}/templates/{templateId}", PermissionCatalog.ProjectManage),
            (HttpMethods.Post, "api/projects/{projectId}/components", PermissionCatalog.ProjectManage),
            (HttpMethods.Put, "api/projects/{projectId}/components/{componentId}", PermissionCatalog.ProjectManage),
            (HttpMethods.Delete, "api/projects/{projectId}/components/{componentId}", PermissionCatalog.ProjectManage),
            (HttpMethods.Post, "api/projects/{projectId}/versions", PermissionCatalog.ProjectManage),
            (HttpMethods.Delete, "api/projects/{projectId}/versions/{versionId}", PermissionCatalog.ProjectManage),
            (HttpMethods.Post, "api/projects/{projectId}/releases", PermissionCatalog.ProjectManage),
            (HttpMethods.Post, "api/projects/{projectId}/releases/{releaseId}/approve", PermissionCatalog.ReleaseApprove),
            (HttpMethods.Post, "api/projects/{projectId}/releases/{releaseId}/publish", PermissionCatalog.ReleasePublish),
            (HttpMethods.Post, "api/projects/{projectId}/milestones", PermissionCatalog.ProjectManage),
            (HttpMethods.Put, "api/projects/{projectId}/milestones/{milestoneId}", PermissionCatalog.ProjectManage),
            (HttpMethods.Post, "api/projects/{projectId}/milestones/{milestoneId}/complete", PermissionCatalog.ProjectManage)
        };
        foreach (var contract in expected)
        {
            AssertEndpoint(endpoints, contract.Method, contract.Route, contract.Permission);
        }

        using var anonymous = await client.GetAsync("/api/projects?organizationId=organization-id");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        using var missingQuery = await client.GetAsync("/api/projects");
        Assert.Equal(HttpStatusCode.BadRequest, missingQuery.StatusCode);
        Assert.Equal(0, missingQuery.Content.Headers.ContentLength);
        using var missingBody = await client.PostAsync("/api/projects", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingBody.StatusCode);
        Assert.Equal(0, missingBody.Content.Headers.ContentLength);
    }

    private static void AssertEndpoint(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string permissionKey)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last();
        Assert.Equal(permissionKey, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal("api", endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.Contains("Projects", endpoint.Metadata.GetOrderedMetadata<ITagsMetadata>().SelectMany(metadata => metadata.Tags));
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var stamp = Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterUserRequest(
            "project-contract-" + stamp,
            $"project-contract-{stamp}@zumbo.local",
            "P@ssword123",
            "project-contract-org-" + stamp));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AuthResponse>>())!.Data!;
    }
}
