using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;
using Zumbo.Api.Presentation.Controllers.Intake;
using Zumbo.Api.Presentation.Middleware.Transactions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class IntakeControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void IntakeRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        _ = factory.CreateClient();
        var types = new[] { typeof(IntakeFormsController), typeof(IntakeSubmissionsController), typeof(PublicIntakeController) };
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => types.Contains(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType())).ToList();
        Assert.Equal(12, endpoints.Count);

        AssertProtected(endpoints, HttpMethods.Get, "api/intake/forms", PermissionCatalog.WorkItemView, "api");
        AssertProtected(endpoints, HttpMethods.Post, "api/intake/forms", PermissionCatalog.WorkflowManage, "api");
        AssertProtected(endpoints, HttpMethods.Get, "api/intake/forms/{formId}", PermissionCatalog.WorkItemView, "api");
        AssertProtected(endpoints, HttpMethods.Put, "api/intake/forms/{formId}", PermissionCatalog.WorkflowManage, "api");
        AssertProtected(endpoints, HttpMethods.Post, "api/intake/forms/{formId}/publish", PermissionCatalog.WorkflowManage, "api");
        AssertProtected(endpoints, HttpMethods.Post, "api/intake/forms/{formId}/archive", PermissionCatalog.WorkflowManage, "api");
        AssertProtected(endpoints, HttpMethods.Get, "api/intake/forms/{formId}/published", PermissionCatalog.WorkItemCreate, "api");
        AssertProtected(endpoints, HttpMethods.Get, "api/intake/forms/{formId}/submissions", PermissionCatalog.WorkItemView, "api");
        AssertProtected(endpoints, HttpMethods.Post, "api/intake/forms/{formId}/submissions", PermissionCatalog.WorkItemCreate, "upload");
        AssertProtected(endpoints, HttpMethods.Post, "api/intake/forms/{formId}/submissions/{submissionId}/triage", PermissionCatalog.WorkItemUpdate, "api");
        AssertPublic(endpoints, HttpMethods.Get, "api/intake/public/forms/{publicId}", "api");
        AssertPublic(endpoints, HttpMethods.Post, "api/intake/public/forms/{publicId}/submissions", "intake-public");
    }

    private static RouteEndpoint Find(IReadOnlyList<RouteEndpoint> endpoints, string method, string route) =>
        Assert.Single(endpoints, candidate => string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);

    private static void AssertProtected(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string permissionKey, string rate)
    {
        var endpoint = Find(endpoints, method, route);
        Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        var permission = Assert.IsAssignableFrom<IEndpointPermissionMetadata>(endpoint.Metadata.GetMetadata<IEndpointPermissionMetadata>());
        Assert.Equal(permissionKey, permission.Permission);
        Assert.Equal(rate, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }

    private static void AssertPublic(IReadOnlyList<RouteEndpoint> endpoints, string method, string route, string rate)
    {
        var endpoint = Find(endpoints, method, route);
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Null(endpoint.Metadata.GetMetadata<IEndpointPermissionMetadata>());
        Assert.Equal(rate, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.NotNull(endpoint.Metadata.GetMetadata<DurableTransactionAttribute>());
    }
}
