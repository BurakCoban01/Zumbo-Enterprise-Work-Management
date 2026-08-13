using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.Integrations;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class DevelopmentIntegrationControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void ManagementRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var types = new[]
        {
            typeof(DevelopmentConnectionsController),
            typeof(DevelopmentConnectionLifecycleController),
            typeof(DevelopmentProviderDiscoveryController),
            typeof(DevelopmentRepositoryMappingsController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => types.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType())).ToList();
        Assert.Equal(12, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Get, "api/integrations/development", "api");
        AssertContract(endpoints, HttpMethods.Post, "api/integrations/development", "api");
        AssertContract(endpoints, HttpMethods.Get, "api/integrations/development/{connectionId}", "api");
        AssertContract(endpoints, HttpMethods.Post, "api/integrations/development/{connectionId}/rotate-credential", "bulk");
        AssertContract(endpoints, HttpMethods.Post, "api/integrations/development/{connectionId}/rotate-webhook-secret", "bulk");
        AssertContract(endpoints, HttpMethods.Post, "api/integrations/development/{connectionId}/health", "bulk");
        AssertContract(endpoints, HttpMethods.Get, "api/integrations/development/{connectionId}/repositories", "bulk");
        AssertContract(endpoints, HttpMethods.Get, "api/integrations/development/{connectionId}/mappings", "api");
        AssertContract(endpoints, HttpMethods.Post, "api/integrations/development/{connectionId}/mappings", "api");
        AssertContract(endpoints, HttpMethods.Delete, "api/integrations/development/mappings/{mappingId}", "api");
        AssertContract(endpoints, HttpMethods.Post, "api/integrations/development/{connectionId}/disconnect", "api");
        AssertContract(endpoints, HttpMethods.Delete, "api/integrations/development/{connectionId}", "api");
    }

    private static void AssertContract(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string rate)
    {
        var endpoint = Assert.Single(endpoints, candidate => string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = Assert.IsAssignableFrom<IEndpointPermissionMetadata>(endpoint.Metadata.GetMetadata<IEndpointPermissionMetadata>());
        Assert.Equal(PermissionCatalog.IntegrationManage, permission.Permission);
        Assert.Equal(rate, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
