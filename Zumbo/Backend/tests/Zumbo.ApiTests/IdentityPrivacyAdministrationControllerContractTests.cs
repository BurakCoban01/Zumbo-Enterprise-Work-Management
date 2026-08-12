using System.Net;
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
using Zumbo.Api.Presentation.Controllers.Identity;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class IdentityPrivacyAdministrationControllerContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task PrivacyAndAdministrationRoutes_AreControllerOwnedAndPreservePresentationContracts()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();
        var controllerTypes = new[]
        {
            typeof(IdentityPrivacyExportController),
            typeof(IdentityPrivacyLifecycleController),
            typeof(IdentityPrivacyJobsController),
            typeof(IdentityPrivacyStatusController),
            typeof(IdentityDirectoryController),
            typeof(IdentityPermissionAdministrationController),
            typeof(IdentityRoleAdministrationController)
        };
        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => controllerTypes.Contains(
                endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType()))
            .ToList();
        Assert.Equal(19, endpoints.Count);

        var profileRoutes = new (string Method, string Route, string Rate)[]
        {
            (HttpMethods.Get, "api/auth/privacy/export", "api"),
            (HttpMethods.Get, "api/auth/privacy/export.ndjson", "api"),
            (HttpMethods.Post, "api/auth/privacy/anonymize", "password-reset"),
            (HttpMethods.Post, "api/auth/privacy/anonymization-jobs", "password-reset"),
            (HttpMethods.Get, "api/auth/privacy/jobs/{jobId}", "api"),
            (HttpMethods.Post, "api/auth/privacy/jobs/{jobId}/retry", "password-reset"),
            (HttpMethods.Post, "api/auth/privacy/jobs/{jobId}/reconcile", "password-reset"),
            (HttpMethods.Post, "api/auth/privacy/jobs/retention/purge", "api"),
            (HttpMethods.Get, "api/auth/users", "api"),
            (HttpMethods.Get, "api/auth/roles", "api"),
            (HttpMethods.Get, "api/auth/permissions", "api")
        };
        foreach (var contract in profileRoutes)
        {
            AssertEndpoint(endpoints, contract.Method, contract.Route, contract.Rate, PermissionCatalog.ProfileRead, isGlobal: false, anonymous: false);
        }

        var anonymousRoutes = new (string Method, string Route, string Rate)[]
        {
            (HttpMethods.Get, "api/auth/privacy/jobs/{jobId}/status", "api"),
            (HttpMethods.Post, "api/auth/privacy/jobs/{jobId}/status/recover", "password-reset"),
            (HttpMethods.Delete, "api/auth/privacy/jobs/{jobId}/status", "password-reset")
        };
        foreach (var contract in anonymousRoutes)
        {
            AssertEndpoint(endpoints, contract.Method, contract.Route, contract.Rate, permission: null, isGlobal: false, anonymous: true);
        }

        var administrationRoutes = new (string Method, string Route)[]
        {
            (HttpMethods.Put, "api/auth/permissions/{key}"),
            (HttpMethods.Post, "api/auth/roles"),
            (HttpMethods.Put, "api/auth/roles/{roleId}"),
            (HttpMethods.Delete, "api/auth/roles/{roleId}"),
            (HttpMethods.Put, "api/auth/users/{userId}/roles")
        };
        foreach (var contract in administrationRoutes)
        {
            AssertEndpoint(endpoints, contract.Method, contract.Route, "api", PermissionCatalog.UserRoleManage, isGlobal: true, anonymous: false);
        }

        using var missingPrivacyBody = await client.PostAsync("/api/auth/privacy/anonymize", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, missingPrivacyBody.StatusCode);
        using var missingRoleBody = await client.PostAsync("/api/auth/roles", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, missingRoleBody.StatusCode);
    }

    private static void AssertEndpoint(
        IReadOnlyList<RouteEndpoint> endpoints,
        string method,
        string route,
        string rate,
        string? permission,
        bool isGlobal,
        bool anonymous)
    {
        var endpoint = Assert.Single(endpoints, candidate =>
            string.Equals(candidate.RoutePattern.RawText?.TrimStart('/'), route, StringComparison.Ordinal)
            && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.Ordinal) == true);
        Assert.IsType<ControllerActionDescriptor>(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>());
        Assert.Equal(rate, endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>().Last().PolicyName);
        Assert.Contains("Identity", endpoint.Metadata.GetOrderedMetadata<ITagsMetadata>().SelectMany(metadata => metadata.Tags));
        Assert.Equal(anonymous, endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null);
        var permissionMetadata = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().LastOrDefault();
        if (permission is null)
        {
            Assert.Null(permissionMetadata);
            return;
        }

        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.NotNull(permissionMetadata);
        Assert.Equal(permission, permissionMetadata.Permission);
        Assert.Equal(isGlobal, permissionMetadata.IsGlobal);
    }
}
