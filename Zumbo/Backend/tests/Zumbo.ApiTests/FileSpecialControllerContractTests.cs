using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.BulkOperations;
using Zumbo.Api.Presentation.Controllers.WorkItems.Dashboards;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class FileSpecialControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void ArtifactAndExportRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToList();

        AssertContract<WorkItemBulkArtifactsController>(endpoints, "api/work-items/bulk/jobs/{jobId}/result", "api", "WorkItems");
        AssertContract<WorkItemBulkArtifactsController>(endpoints, "api/work-items/bulk/jobs/{jobId}/errors", "api", "WorkItems");
        AssertContract<DashboardExportController>(endpoints, "api/dashboards/{dashboardId}/export", "report", "Dashboards");
    }

    private static void AssertContract<TController>(IReadOnlyList<RouteEndpoint> endpoints, string route, string rate, string tag)
    {
        var endpoint = Assert.Single(endpoints, candidate => string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Get, StringComparer.Ordinal) == true);
        Assert.Equal(typeof(TController), Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()).ControllerTypeInfo.AsType());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Equal(PermissionCatalog.WorkItemView, endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last().Permission);
        Assert.Equal(rate, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.Contains(tag, endpoint.Metadata.GetOrderedMetadata<ITagsMetadata>().SelectMany(metadata => metadata.Tags));
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
