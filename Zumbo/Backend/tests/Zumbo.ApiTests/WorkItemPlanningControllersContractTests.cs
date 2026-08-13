using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.Planning;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class WorkItemPlanningControllersContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void PlanningRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var controllerTypes = new[]
        {
            typeof(WorkItemAssignmentsController),
            typeof(WorkItemPlanningController),
            typeof(WorkItemRankingController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(5, endpoints.Count);

        AssertContract(endpoints, "api/work-items/{id}/assignee", PermissionCatalog.WorkItemAssign);
        AssertContract(endpoints, "api/work-items/{id}/team", PermissionCatalog.WorkItemUpdate);
        AssertContract(endpoints, "api/work-items/{id}/planning", PermissionCatalog.WorkItemUpdate);
        AssertContract(endpoints, "api/work-items/{id}/parent", PermissionCatalog.WorkItemUpdate);
        AssertContract(endpoints, "api/work-items/{id}/rank", PermissionCatalog.WorkItemMove);
    }

    private static void AssertContract(IReadOnlyList<RouteEndpoint> endpoints, string route, string permissionKey)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Patch, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last();
        Assert.Equal(permissionKey, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal("api", endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
