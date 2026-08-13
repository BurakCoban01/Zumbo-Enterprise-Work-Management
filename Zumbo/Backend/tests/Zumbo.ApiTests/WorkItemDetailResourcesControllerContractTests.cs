using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.Approvals;
using Zumbo.Api.Presentation.Controllers.WorkItems.Checklist;
using Zumbo.Api.Presentation.Controllers.WorkItems.Labels;
using Zumbo.Api.Presentation.Controllers.WorkItems.Relations;
using Zumbo.Api.Presentation.Controllers.WorkItems.Worklogs;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class WorkItemDetailResourcesControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void DetailResourceRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var controllerTypes = new[]
        {
            typeof(WorkItemChecklistController),
            typeof(WorkItemLabelsController),
            typeof(WorkItemWorklogsController),
            typeof(WorkItemRelationsController),
            typeof(WorkItemApprovalsController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(11, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Post, "api/work-items/{id}/checklist", PermissionCatalog.WorkItemUpdate);
        AssertContract(endpoints, HttpMethods.Patch, "api/work-items/{id}/checklist/{itemId}", PermissionCatalog.WorkItemUpdate);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/{id}/labels", PermissionCatalog.WorkItemUpdate);
        AssertContract(endpoints, HttpMethods.Delete, "api/work-items/{id}/labels/{label}", PermissionCatalog.WorkItemUpdate);
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/{id}/worklogs", PermissionCatalog.WorkItemView);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/{id}/worklogs", PermissionCatalog.WorkLogCreate);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/{id}/relations", PermissionCatalog.WorkItemLink);
        AssertContract(endpoints, HttpMethods.Delete, "api/work-items/{id}/relations/{relatedWorkItemId}", PermissionCatalog.WorkItemLink);
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/{id}/approvals", PermissionCatalog.WorkItemView);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/{id}/approvals", PermissionCatalog.WorkItemApprove);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/{id}/approvals/{approvalId}/decision", PermissionCatalog.WorkItemApprove);
    }

    private static void AssertContract(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string permission)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Equal(permission, endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last().Permission);
        Assert.Equal("api", endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
