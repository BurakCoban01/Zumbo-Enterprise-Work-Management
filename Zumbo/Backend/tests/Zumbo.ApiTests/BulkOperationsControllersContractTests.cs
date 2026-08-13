using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.BulkOperations;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class BulkOperationsControllersContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void BulkOperationRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var controllerTypes = new[]
        {
            typeof(BulkWorkItemCommandsController),
            typeof(BulkJobSubmissionsController),
            typeof(BulkJobsController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(10, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Post, "api/work-items/bulk/move", PermissionCatalog.WorkItemMove, "bulk");
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/bulk/assign", PermissionCatalog.WorkItemAssign, "bulk");
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/bulk/archive", PermissionCatalog.WorkItemDelete, "bulk");
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/bulk/jobs", PermissionCatalog.WorkItemUpdate, "bulk");
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/bulk/jobs/export", PermissionCatalog.WorkItemView, "bulk");
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/bulk/jobs/import", PermissionCatalog.WorkItemCreate, "bulk");
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/bulk/jobs", PermissionCatalog.WorkItemView, "api");
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/bulk/jobs/{jobId}", PermissionCatalog.WorkItemView, "api");
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/bulk/jobs/{jobId}/cancel", PermissionCatalog.WorkItemUpdate, "api");
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/bulk/jobs/{jobId}/retry", PermissionCatalog.WorkItemUpdate, "api");
    }

    private static void AssertContract(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route,
        string permissionKey,
        string rateLimit)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last();
        Assert.Equal(permissionKey, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal(rateLimit, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
