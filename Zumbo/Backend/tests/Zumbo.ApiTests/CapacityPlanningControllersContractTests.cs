using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.CapacityPlanning;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class CapacityPlanningControllersContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void CapacityPlanningRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var controllerTypes = new[]
        {
            typeof(CapacityPlanQueriesController),
            typeof(CapacityPlanCommandsController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(8, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Get, "api/capacity-plans", "api");
        AssertContract(endpoints, HttpMethods.Get, "api/capacity-plans/{planId}", "api");
        AssertContract(endpoints, HttpMethods.Get, "api/capacity-plans/{planId}/snapshot", "report");
        AssertContract(endpoints, HttpMethods.Post, "api/capacity-plans/{planId}/scenarios", "report");
        AssertContract(endpoints, HttpMethods.Post, "api/capacity-plans", "api");
        AssertContract(endpoints, HttpMethods.Put, "api/capacity-plans/{planId}", "api");
        AssertContract(endpoints, HttpMethods.Put, "api/capacity-plans/{planId}/sharing", "api");
        AssertContract(endpoints, HttpMethods.Delete, "api/capacity-plans/{planId}", "api");
    }

    private static void AssertContract(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route,
        string rateLimit)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last();
        Assert.Equal(PermissionCatalog.WorkItemView, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal(rateLimit, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
