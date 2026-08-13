using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.Comments;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class WorkItemCommentsControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void CommentRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()
                == typeof(WorkItemCommentsController))
            .ToList();
        Assert.Equal(5, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Get, "api/work-items/{id}/comments", PermissionCatalog.WorkItemView);
        AssertContract(endpoints, HttpMethods.Get, "api/work-items/{id}/comments/{commentId}/revisions", PermissionCatalog.WorkItemView);
        AssertContract(endpoints, HttpMethods.Post, "api/work-items/{id}/comments", PermissionCatalog.CommentCreate);
        AssertContract(endpoints, HttpMethods.Put, "api/work-items/{id}/comments/{commentId}", PermissionCatalog.CommentCreate);
        AssertContract(endpoints, HttpMethods.Delete, "api/work-items/{id}/comments/{commentId}", PermissionCatalog.CommentCreate);
    }

    private static void AssertContract(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route,
        string permissionKey)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Equal(permissionKey, endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last().Permission);
        Assert.Equal("api", endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
