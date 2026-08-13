using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.Projects.Knowledge;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class KnowledgeControllersContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void KnowledgeRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var controllerTypes = new[] { typeof(KnowledgeQueriesController), typeof(KnowledgeDocumentsController), typeof(KnowledgeCommentsController) };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(9, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Get, "api/knowledge-documents", "search");
        AssertContract(endpoints, HttpMethods.Get, "api/knowledge-documents/{documentId}", "api");
        AssertContract(endpoints, HttpMethods.Get, "api/knowledge-documents/scope-link-options", "search");
        AssertContract(endpoints, HttpMethods.Get, "api/knowledge-documents/{documentId}/versions/{number:int}", "api");
        AssertContract(endpoints, HttpMethods.Post, "api/knowledge-documents", "api");
        AssertContract(endpoints, HttpMethods.Put, "api/knowledge-documents/{documentId}", "api");
        AssertContract(endpoints, HttpMethods.Delete, "api/knowledge-documents/{documentId}", "api");
        AssertContract(endpoints, HttpMethods.Post, "api/knowledge-documents/{documentId}/comments", "api");
        AssertContract(endpoints, HttpMethods.Patch, "api/knowledge-documents/{documentId}/comments/{commentId}/resolve", "api");
    }

    private static void AssertContract(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string rateLimit)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last();
        Assert.Equal(PermissionCatalog.ProjectView, permission.Permission);
        Assert.False(permission.IsGlobal);
        Assert.Equal(rateLimit, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
    }
}
