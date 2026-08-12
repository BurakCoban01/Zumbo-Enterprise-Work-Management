using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.Activity;
using Zumbo.Api.Presentation.Controllers.WorkItems.Collaboration;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class WorkItemCollaborationControllersContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void ActivityAndCollaborationRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var controllerTypes = new[] { typeof(WorkItemActivityController), typeof(WorkItemCollaborationController) };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(
                endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(5, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Get, "api/work-items/{id}/activity");
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/{id}/timeline");
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/{id}/collaboration");
        AssertContract(endpoints, HttpMethods.Put, "api/work-items/{id}/watch");
        AssertContract(endpoints, HttpMethods.Put, "api/work-items/{id}/vote");
    }

    private static void AssertContract(IReadOnlyList<RouteEndpoint> endpoints, string method, string route)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Equal(PermissionCatalog.WorkItemView, endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last().Permission);
        Assert.Equal("api", endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
