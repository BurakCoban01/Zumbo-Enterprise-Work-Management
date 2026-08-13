using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.Operations;
using Zumbo.Api.Presentation.Controllers.WorkItems.Schema;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class WorkItemSchemaOperationsControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void SchemaAndDurableMessagingRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var controllerTypes = new[]
        {
            typeof(WorkItemSchemasController),
            typeof(WorkItemCustomFieldsController),
            typeof(WorkItemDurableMessagingController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(8, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Get, "api/work-item-schemas/{projectId}", PermissionCatalog.WorkItemView, false, "api");
        AssertContract(endpoints, HttpMethods.Put, "api/work-item-schemas/{projectId}", PermissionCatalog.WorkItemUpdate, false, "api");
        AssertContract(endpoints, HttpMethods.Get, "api/work-item-schemas/{projectId}/reports/issue-types", PermissionCatalog.WorkItemView, false, "api");
        AssertContract(endpoints, HttpMethods.Get, "api/work-item-schemas/{projectId}/reports/custom-fields/{fieldKey}", PermissionCatalog.WorkItemView, false, "api");
        AssertContract(endpoints, HttpMethods.Put, "api/work-items/{id}/custom-fields", PermissionCatalog.WorkItemUpdate, false, "api");
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/durable-messaging/metrics", PermissionCatalog.OperationsManage, true, "api");
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/durable-messaging/dead-letters", PermissionCatalog.OperationsManage, true, "report");
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/durable-messaging/dead-letter/{messageId}/replay", PermissionCatalog.OperationsManage, true, "api");
    }

    private static void AssertContract(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route,
        string permissionKey,
        bool isGlobal,
        string ratePolicy)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last();
        Assert.Equal(permissionKey, permission.Permission);
        Assert.Equal(isGlobal, permission.IsGlobal);
        Assert.Equal(ratePolicy, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
