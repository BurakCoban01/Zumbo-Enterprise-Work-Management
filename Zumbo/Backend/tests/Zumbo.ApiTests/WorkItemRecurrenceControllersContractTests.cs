using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.Recurrences;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class WorkItemRecurrenceControllersContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void TemplateAndRecurrenceRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var controllerTypes = new[]
        {
            typeof(WorkItemTemplatesController),
            typeof(WorkItemRecurrenceQueriesController),
            typeof(WorkItemRecurrenceLifecycleController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(11, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Get, "api/work-items/templates", PermissionCatalog.WorkItemView, false);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/templates", PermissionCatalog.WorkItemCreate, false);
        AssertContract(endpoints, HttpMethods.Put, "api/work-items/templates/{templateId}", PermissionCatalog.WorkItemUpdate, false);
        AssertContract(endpoints, HttpMethods.Delete, "api/work-items/templates/{templateId}", PermissionCatalog.WorkItemUpdate, false);
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/recurrences", PermissionCatalog.WorkItemView, false);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/recurrences", PermissionCatalog.WorkItemCreate, false);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/recurrences/preview", PermissionCatalog.WorkItemCreate, false);
        AssertContract(endpoints, HttpMethods.Patch, "api/work-items/recurrences/{recurrenceId}/state", PermissionCatalog.WorkItemUpdate, false);
        AssertContract(endpoints, HttpMethods.Delete, "api/work-items/recurrences/{recurrenceId}", PermissionCatalog.WorkItemUpdate, false);
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/recurrences/{recurrenceId}/occurrences", PermissionCatalog.WorkItemView, false);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/recurrences/process-due", PermissionCatalog.OperationsManage, true);
    }

    private static void AssertContract(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route,
        string permissionKey,
        bool isGlobal)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last();
        Assert.Equal(permissionKey, permission.Permission);
        Assert.Equal(isGlobal, permission.IsGlobal);
        Assert.Equal("api", endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
