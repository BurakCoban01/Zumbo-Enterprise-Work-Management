using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.Automations;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class AutomationControllersContractTests(WebApplicationFactory<Program> baseFactory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void AutomationRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var types = new[] { typeof(AutomationQueriesController), typeof(AutomationCommandsController), typeof(AutomationRunCommandsController) };
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => types.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType())).ToList();
        Assert.Equal(11, endpoints.Count);

        AssertContract(endpoints, HttpMethods.Get, "api/automations", PermissionCatalog.WorkflowView, "api", false);
        AssertContract(endpoints, HttpMethods.Get, "api/automations/{ruleId}", PermissionCatalog.WorkflowView, "api", false);
        AssertContract(endpoints, HttpMethods.Get, "api/automations/runs", PermissionCatalog.WorkflowView, "api", false);
        AssertContract(endpoints, HttpMethods.Get, "api/automations/runs/{runId}", PermissionCatalog.WorkflowView, "api", false);
        AssertContract(endpoints, HttpMethods.Post, "api/automations", PermissionCatalog.WorkflowManage, "api", true);
        AssertContract(endpoints, HttpMethods.Put, "api/automations/{ruleId}/draft", PermissionCatalog.WorkflowManage, "api", true);
        AssertContract(endpoints, HttpMethods.Post, "api/automations/{ruleId}/publish", PermissionCatalog.WorkflowManage, "api", true);
        AssertContract(endpoints, HttpMethods.Patch, "api/automations/{ruleId}/state", PermissionCatalog.WorkflowManage, "api", true);
        AssertContract(endpoints, HttpMethods.Delete, "api/automations/{ruleId}", PermissionCatalog.WorkflowManage, "api", true);
        AssertContract(endpoints, HttpMethods.Post, "api/automations/{ruleId}/dry-run", PermissionCatalog.WorkflowManage, "report", true);
        AssertContract(endpoints, HttpMethods.Post, "api/automations/runs/{runId}/replay", PermissionCatalog.WorkflowManage, "api", true);
    }

    private static void AssertContract(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string permission, string rate, bool transactional)
    {
        var endpoint = Assert.Single(endpoints, candidate => string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.Equal(permission, endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().Last().Permission);
        Assert.Equal(rate, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.Equal(transactional, endpoint.Metadata.GetMetadata<DurableTransactionAttribute>() is not null);
    }
}
