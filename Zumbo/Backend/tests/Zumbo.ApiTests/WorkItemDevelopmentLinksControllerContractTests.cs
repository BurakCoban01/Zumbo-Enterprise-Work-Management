using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.Development;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class WorkItemDevelopmentLinksControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void DevelopmentLinkRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(WorkItemDevelopmentLinksController)).ToList();
        Assert.Equal(4, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Get, "api/work-items/{workItemId}/development-links", PermissionCatalog.WorkItemView);
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/{workItemId}/development-links/mappings", PermissionCatalog.WorkItemLink);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/{workItemId}/development-links", PermissionCatalog.WorkItemLink);
        AssertContract(endpoints, HttpMethods.Delete, "api/work-items/{workItemId}/development-links/{linkId}", PermissionCatalog.WorkItemLink);
    }

    private static void AssertContract(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string permissionKey)
    {
        var endpoint = Assert.Single(endpoints, candidate => string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = Assert.IsAssignableFrom<IEndpointPermissionMetadata>(endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last());
        Assert.Equal(permissionKey, permission.Permission);
        Assert.Equal("api", endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
