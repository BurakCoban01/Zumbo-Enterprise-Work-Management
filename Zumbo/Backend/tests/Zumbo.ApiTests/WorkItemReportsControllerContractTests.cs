using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.WorkItems.Reports;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class WorkItemReportsControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void ReportRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var controllerTypes = new[]
        {
            typeof(ProjectOverviewReportsController),
            typeof(ProjectPerformanceReportsController),
            typeof(SprintReportsController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(9, endpoints.Count);

        AssertContract(endpoints, "api/work-items/reports/project-summary/{projectId}");
        AssertContract(endpoints, "api/work-items/reports/status-distribution/{projectId}");
        AssertContract(endpoints, "api/work-items/reports/user-workload/{projectId}");
        AssertContract(endpoints, "api/work-items/reports/due-date-risks/{projectId}");
        AssertContract(endpoints, "api/work-items/reports/flow-time/{projectId}");
        AssertContract(endpoints, "api/work-items/reports/completion-rate/{projectId}");
        AssertContract(endpoints, "api/work-items/reports/team-performance/{projectId}");
        AssertContract(endpoints, "api/work-items/reports/sprint-burndown/{projectId}/{sprintId}");
        AssertContract(endpoints, "api/work-items/reports/sprint-velocity/{projectId}");
    }

    private static void AssertContract(IReadOnlyList<RouteEndpoint> endpoints, string route)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Get, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Equal(
            PermissionCatalog.WorkItemView,
            endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last().Permission);
        Assert.Equal("report", endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
