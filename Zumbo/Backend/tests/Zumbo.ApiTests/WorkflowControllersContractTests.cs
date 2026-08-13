using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.Workflows;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class WorkflowControllersContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void WorkflowRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var controllerTypes = new[] { typeof(WorkflowQueriesController), typeof(WorkflowCommandsController) };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(6, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Get, "api/workflows/{projectId}", PermissionCatalog.WorkflowView);
        AssertContract(endpoints, HttpMethods.Get, "api/workflows/{projectId}/draft", PermissionCatalog.WorkflowView);
        AssertContract(endpoints, HttpMethods.Get, "api/workflows/{projectId}/versions", PermissionCatalog.WorkflowView);
        AssertContract(endpoints, HttpMethods.Put, "api/workflows/{projectId}", PermissionCatalog.WorkflowManage);
        AssertContract(endpoints, HttpMethods.Put, "api/workflows/{projectId}/draft", PermissionCatalog.WorkflowManage);
        AssertContract(endpoints, HttpMethods.Post, "api/workflows/{projectId}/publish", PermissionCatalog.WorkflowManage);
    }

    private static void AssertContract(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string permissionKey)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last();
        Assert.Equal(permissionKey, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal("api", endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
    }
}
